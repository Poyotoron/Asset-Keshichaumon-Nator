using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Maaaaa.Akm.Editor
{
    /// <summary>
    /// ビルドキャッシュ掃除の中核。
    ///
    /// 重要な制約:
    ///   Unity 実行中は Library/ / Temp/ がロックされ、Unity プロセス内から削除するとクラッシュする。
    ///   EditorApplication.quitting の時点でも Unity は生きているため削除できない。
    ///   したがって「予約 → 終了時に外部ヘルパープロセスを detached 起動 → ヘルパーが PID 終了を待って削除」
    ///   という方式を採る。ヘルパーは PowerShell スクリプト（Windows 専用）。
    ///
    /// 予約・完了フラグは削除対象外かつ再インポートを誘発しない領域（ProjectRoot/.akm/）に置く。
    /// </summary>
    internal static class CacheClean
    {
        // ---- .akm/ 配下のフラグ / ログ（Library・Assets の外に置く）----
        public const string AkmDirName = ".akm";
        public const string PendingFileName = "pending-clean.json";
        public const string DoneFileName = "clean-done.json";
        public const string LogFileName = "clean-log.txt";
        public const string HelperFileName = "akm-clean-helper.ps1";
        public const string FallbackBatName = "clean-cache.bat";

        public const int HelperTimeoutSeconds = 120;

        /// <summary>
        /// 即時実行（今すぐ終了して掃除）で自ら Exit する場合に、終了時の確認ダイアログの
        /// 二重表示を避けるためのフラグ。ユーザーは直前に最終確認済みのため。
        /// </summary>
        public static bool SuppressQuitPrompt = false;

        // ヘルパーの二重起動防止（即時実行で自ら起動した後、quitting でも呼ばれ得るため）。
        private static bool _helperLaunched = false;

        public static bool IsWindows =>
            Application.platform == RuntimePlatform.WindowsEditor;

        // ------------------------------------------------------------ パス

        public static string AkmDir => AkmUtil.Normalize(Path.Combine(AkmUtil.ProjectRoot, AkmDirName));
        public static string PendingPath => AkmUtil.Normalize(Path.Combine(AkmDir, PendingFileName));
        public static string DonePath => AkmUtil.Normalize(Path.Combine(AkmDir, DoneFileName));
        public static string LogPath => AkmUtil.Normalize(Path.Combine(AkmDir, LogFileName));
        public static string HelperPath => AkmUtil.Normalize(Path.Combine(AkmDir, HelperFileName));
        public static string FallbackBatPath =>
            AkmUtil.Normalize(Path.Combine(AkmUtil.ProjectRoot, FallbackBatName));

        private static void EnsureAkmDir()
        {
            if (!Directory.Exists(AkmDir)) Directory.CreateDirectory(AkmDir);
        }

        // ------------------------------------------------------------ 削除対象

        /// <summary>削除対象1件（フォルダ）。</summary>
        internal class CacheTarget
        {
            public string RelativePath;  // 例: "Library"
            public string AbsPath;       // 絶対パス
            public bool Enabled;         // 削除するか
            public bool Exists;
            public long SizeBytes = -1;  // 未計測は -1
        }

        /// <summary>
        /// 掃除対象フォルダを列挙する（存在するもののみ）。Logs は設定に応じて。
        /// Assets/ Packages/ ProjectSettings/ は絶対に含めない。
        /// </summary>
        public static List<CacheTarget> EnumerateTargets(AkmSettings settings)
        {
            var root = AkmUtil.ProjectRoot;
            var list = new List<CacheTarget>();

            void Add(string rel, bool enabled)
            {
                var abs = AkmUtil.Normalize(Path.Combine(root, rel));
                list.Add(new CacheTarget
                {
                    RelativePath = rel,
                    AbsPath = abs,
                    Enabled = enabled,
                    Exists = Directory.Exists(abs),
                });
            }

            Add("Library", true);
            Add("Temp", true);
            Add("obj", true);
            Add(".vs", true);
            Add("Logs", settings != null && settings.cacheCleanIncludeLogs);

            return list;
        }

        /// <summary>各対象のサイズを計測する（プログレス付き）。</summary>
        public static void MeasureSizes(List<CacheTarget> targets)
        {
            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    EditorUtility.DisplayProgressBar(
                        AkmStrings.ToolName,
                        AkmStrings.ProgressMeasureCache + "\n" + t.RelativePath,
                        (float)i / Math.Max(1, targets.Count));
                    t.Exists = Directory.Exists(t.AbsPath);
                    t.SizeBytes = t.Exists ? AkmUtil.DirectorySize(t.AbsPath) : 0;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>Library/ の実測サイズ。</summary>
        public static long MeasureLibrarySize()
        {
            var abs = AkmUtil.Normalize(Path.Combine(AkmUtil.ProjectRoot, "Library"));
            return AkmUtil.DirectorySize(abs);
        }

        /// <summary>Assets/ 配下のアセット数と合計サイズ。</summary>
        public static void MeasureAssets(out int count, out long totalBytes)
        {
            count = 0;
            totalBytes = 0;
            foreach (var p in AssetDatabase.GetAllAssetPaths())
            {
                if (!p.StartsWith("Assets/")) continue;
                if (p.EndsWith(".meta")) continue;
                if (AssetDatabase.IsValidFolder(p)) continue;
                count++;
                totalBytes += AkmUtil.FileSize(p);
            }
        }

        // ------------------------------------------------------------ 予約フラグ

        [Serializable]
        internal class PendingClean
        {
            public string reservedAt;
            public List<string> targetsRelative = new List<string>();
            public bool includeSolutionFiles = true; // *.csproj / *.sln も削除
        }

        public static bool IsReserved() => File.Exists(PendingPath);

        public static PendingClean ReadPending()
        {
            try
            {
                if (!File.Exists(PendingPath)) return null;
                return JsonUtility.FromJson<PendingClean>(File.ReadAllText(PendingPath));
            }
            catch { return null; }
        }

        /// <summary>掃除を予約する。削除対象は列挙済みの enabled なフォルダ。</summary>
        public static void Reserve(List<CacheTarget> targets)
        {
            EnsureAkmDir();
            var pending = new PendingClean
            {
                reservedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                targetsRelative = targets.Where(t => t.Enabled).Select(t => t.RelativePath).ToList(),
            };
            File.WriteAllText(PendingPath, JsonUtility.ToJson(pending, true));
            AppendLog($"reserved: {string.Join(", ", pending.targetsRelative)}");
        }

        public static void CancelReservation()
        {
            try { if (File.Exists(PendingPath)) File.Delete(PendingPath); }
            catch (Exception ex) { Debug.LogWarning($"[{AkmStrings.ToolName}] 予約解除に失敗: {ex.Message}"); }
        }

        // ------------------------------------------------------------ 完了マーカー

        [Serializable]
        internal class CleanDone
        {
            public string deletedAt;
        }

        public static bool HasDoneMarker() => File.Exists(DonePath);

        public static void ClearDoneMarker()
        {
            try { if (File.Exists(DonePath)) File.Delete(DonePath); }
            catch { /* 次回消せればよい */ }
        }

        // ------------------------------------------------------------ ヘルパー起動

        /// <summary>
        /// 外部ヘルパー（PowerShell）を detached 起動する。EditorApplication.quitting から呼ぶ。
        /// Unity の PID を渡し、ヘルパーはプロセス終了を待ってから削除する。
        /// </summary>
        public static void LaunchHelper()
        {
            if (_helperLaunched) return; // 二重起動防止
            if (!IsWindows)
            {
                AppendLog("not Windows; helper not launched.");
                return;
            }
            var pending = ReadPending();
            if (pending == null)
            {
                AppendLog("no pending reservation; helper not launched.");
                return;
            }

            try
            {
                EnsureAkmDir();
                WriteHelperScript(pending);

                int pid = Process.GetCurrentProcess().Id;
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments =
                        $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{HelperPath}\" -UnityPid {pid}",
                    UseShellExecute = true,          // 親から独立させる
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    WorkingDirectory = AkmUtil.ProjectRoot,
                };
                Process.Start(psi);
                _helperLaunched = true;
                AppendLog($"helper launched (unity pid={pid}).");
            }
            catch (Exception ex)
            {
                // ここで失敗しても予約フラグは残るので、次回起動時に検知して再提案できる。
                AppendLog($"helper launch failed: {ex.Message}");
                Debug.LogError($"[{AkmStrings.ToolName}] キャッシュ掃除ヘルパーの起動に失敗しました: {ex.Message}");
            }
        }

        private static void WriteHelperScript(PendingClean pending)
        {
            var proj = AkmUtil.ProjectRoot;
            var sb = new StringBuilder();
            sb.AppendLine("param([int]$UnityPid)");
            sb.AppendLine("$ErrorActionPreference = 'SilentlyContinue'");
            sb.AppendLine($"$proj = '{PsEscape(proj)}'");
            sb.AppendLine($"$akm = '{PsEscape(AkmDir)}'");
            sb.AppendLine($"$log = '{PsEscape(LogPath)}'");
            sb.AppendLine($"$pending = '{PsEscape(PendingPath)}'");
            sb.AppendLine($"$done = '{PsEscape(DonePath)}'");
            sb.AppendLine($"$timeout = {HelperTimeoutSeconds}");
            sb.AppendLine("function Log($m){ Add-Content -LiteralPath $log -Value ((Get-Date -Format s) + ' [helper] ' + $m) }");
            sb.AppendLine("Log \"started, waiting for Unity PID $UnityPid\"");
            sb.AppendLine("try { Wait-Process -Id $UnityPid -Timeout $timeout -ErrorAction Stop } catch { }");
            // ロックファイル解放待ち
            sb.AppendLine("$lock = Join-Path $proj 'Temp/UnityLockfile'");
            sb.AppendLine("$elapsed = 0");
            sb.AppendLine("while ((Test-Path $lock) -and ($elapsed -lt $timeout)) {");
            sb.AppendLine("  try { $fs=[System.IO.File]::Open($lock,'Open','ReadWrite','None'); $fs.Close(); break } catch { Start-Sleep -Seconds 1; $elapsed++ }");
            sb.AppendLine("}");
            // まだ生存していたら中断（中途半端な削除を避ける）
            sb.AppendLine("if (Get-Process -Id $UnityPid -ErrorAction SilentlyContinue) { Log 'Unity still alive after timeout; abort (no deletion).'; exit 0 }");
            // 対象フォルダ削除
            var targets = pending.targetsRelative ?? new List<string>();
            sb.Append("$targets = @(");
            sb.Append(string.Join(", ", targets.Select(rel =>
                $"'{PsEscape(AkmUtil.Normalize(Path.Combine(proj, rel)))}'")));
            sb.AppendLine(")");
            sb.AppendLine("foreach ($t in $targets) {");
            sb.AppendLine("  if (Test-Path -LiteralPath $t) {");
            sb.AppendLine("    Log \"deleting $t\"");
            sb.AppendLine("    try { Remove-Item -LiteralPath $t -Recurse -Force -ErrorAction Stop } catch { Log ('failed: ' + $_.Exception.Message) }");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            // *.csproj / *.sln も削除（再生成される）
            if (pending.includeSolutionFiles)
            {
                sb.AppendLine("Get-ChildItem -LiteralPath $proj -Filter *.csproj -File -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }");
                sb.AppendLine("Get-ChildItem -LiteralPath $proj -Filter *.sln -File -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force -ErrorAction SilentlyContinue }");
            }
            // 予約フラグ削除 + 完了マーカー書き込み
            sb.AppendLine("Remove-Item -LiteralPath $pending -Force -ErrorAction SilentlyContinue");
            sb.AppendLine("Set-Content -LiteralPath $done -Value ('{\"deletedAt\":\"' + (Get-Date -Format s) + '\"}')");
            sb.AppendLine("Log 'done'");

            File.WriteAllText(HelperPath, sb.ToString(), new UTF8Encoding(false));
        }

        private static string PsEscape(string s) => s?.Replace("'", "''");

        // ------------------------------------------------------------ フォールバック

        /// <summary>
        /// clean-cache.bat をプロジェクトルートに生成する。Unity を終了してから手動実行するためのフォールバック。
        /// </summary>
        public static string WriteFallbackBat(List<CacheTarget> targets)
        {
            var enabled = targets.Where(t => t.Enabled).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("rem いらないアセット消しちゃうもんネーター — キャッシュ掃除フォールバック");
            sb.AppendLine("rem 使い方: Unity を完全に終了してから、このバッチをダブルクリックしてください。");
            sb.AppendLine("setlocal");
            sb.AppendLine("echo.");
            sb.AppendLine("echo ================================================================");
            sb.AppendLine("echo  ビルドキャッシュを削除します（Library / Temp 等）。");
            sb.AppendLine("echo  次回 Unity 起動時に全アセットの再インポートが発生します。");
            sb.AppendLine("echo  Unity を完全に終了していることを確認してください。");
            sb.AppendLine("echo ================================================================");
            sb.AppendLine("echo.");
            sb.AppendLine("pause");
            foreach (var t in enabled)
            {
                // バッチはこの .bat の場所（プロジェクトルート）を基準にする
                sb.AppendLine($"if exist \"%~dp0{t.RelativePath}\" (");
                sb.AppendLine($"  echo Deleting {t.RelativePath} ...");
                sb.AppendLine($"  rmdir /s /q \"%~dp0{t.RelativePath}\"");
                sb.AppendLine(")");
            }
            sb.AppendLine("del /q \"%~dp0*.csproj\" 2>nul");
            sb.AppendLine("del /q \"%~dp0*.sln\" 2>nul");
            sb.AppendLine("echo.");
            sb.AppendLine("echo 完了しました。Unity を起動すると再インポートが始まります。");
            sb.AppendLine("pause");

            File.WriteAllText(FallbackBatPath, sb.ToString(), Encoding.Default);
            return FallbackBatPath;
        }

        // ------------------------------------------------------------ ログ

        public static void AppendLog(string message)
        {
            try
            {
                EnsureAkmDir();
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:s} [editor] {message}{Environment.NewLine}");
            }
            catch { /* ログ失敗は致命的でない */ }
        }

        public static string ReadLogTail(int maxChars = 4000)
        {
            try
            {
                if (!File.Exists(LogPath)) return "";
                var text = File.ReadAllText(LogPath);
                return text.Length <= maxChars ? text : text.Substring(text.Length - maxChars);
            }
            catch { return ""; }
        }
    }
}
