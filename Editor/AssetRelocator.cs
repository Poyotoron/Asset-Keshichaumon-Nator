using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Maaaaa.Akm.Editor
{
    [Serializable]
    internal class RelocationEntry
    {
        /// <summary>元のアセットパス（例: Assets/作者名/商品名）。</summary>
        public string originalPath;

        /// <summary>退避フォルダ内の相対パス（元の構造を保持）。</summary>
        public string archivedRelativePath;

        /// <summary>ディレクトリなら true、単一ファイルなら false。</summary>
        public bool isDirectory;
    }

    [Serializable]
    internal class RelocationManifest
    {
        public string createdAt;
        public string toolVersion = "0.1.0";
        public List<RelocationEntry> entries = new List<RelocationEntry>();
    }

    /// <summary>
    /// 退避（F-DEL-01）と復元（F-DEL-02）。破壊的操作を担う。
    /// - 退避先は ProjectRoot 直下 _UnusedAssets_yyyyMMdd_HHmmss（Assets 外＝再インポート対象外）。
    /// - .meta を必ず同伴して GUID を保持する。
    /// - 移動マッピングを退避フォルダ内 JSON に記録し、復元を可能にする。
    /// この操作は削除ではなく「移動」であり、常に可逆（設計原則 P-1）。
    /// </summary>
    internal static class AssetRelocator
    {
        private const string ManifestFileName = ".akm-relocation.json";
        private const string TrashPrefix = "_UnusedAssets_";
        private const string BackupPackageName = "backup.unitypackage";

        /// <summary>退避を実行し、作成した退避フォルダの絶対パスを返す。</summary>
        public static string Relocate(IReadOnlyList<ScanResultEntry> targets, out int movedCount)
        {
            return Relocate(targets, false, out movedCount, out _);
        }

        /// <summary>
        /// 退避を実行する（F-DEL-01）。exportPackage=true なら移動前に .unitypackage を書き出す（F-DEL-03）。
        /// </summary>
        /// <param name="exportedPackagePath">書き出した .unitypackage の絶対パス（未書き出しは null）。</param>
        public static string Relocate(
            IReadOnlyList<ScanResultEntry> targets, bool exportPackage,
            out int movedCount, out string exportedPackagePath)
        {
            movedCount = 0;
            exportedPackagePath = null;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var trashRoot = AkmUtil.Normalize(Path.Combine(AkmUtil.ProjectRoot, TrashPrefix + timestamp));
            Directory.CreateDirectory(trashRoot);

            // F-DEL-03: 移動前（アセットがまだ Assets/ にある間）に .unitypackage を書き出す。
            if (exportPackage)
            {
                exportedPackagePath = ExportBackup(targets, trashRoot);
            }

            var manifest = new RelocationManifest { createdAt = timestamp };

            try
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    var t = targets[i];
                    EditorUtility.DisplayProgressBar(
                        AkmStrings.ToolName, AkmStrings.ProgressRelocate + "\n" + t.UnitPath,
                        (float)i / Math.Max(1, targets.Count));

                    var absSource = AkmUtil.ToAbsolute(t.UnitPath);
                    bool isDir = Directory.Exists(absSource);
                    bool isFile = File.Exists(absSource);
                    if (!isDir && !isFile)
                    {
                        Debug.LogWarning($"[{AkmStrings.ToolName}] 退避対象が存在しません: {t.UnitPath}");
                        continue;
                    }

                    // 退避先は元の構造をそのまま保持（trashRoot/Assets/...）
                    var absDest = AkmUtil.Normalize(Path.Combine(trashRoot, t.UnitPath));
                    var absDestParent = Path.GetDirectoryName(absDest);
                    if (!string.IsNullOrEmpty(absDestParent)) Directory.CreateDirectory(absDestParent);

                    if (isDir) Directory.Move(absSource, absDest);
                    else File.Move(absSource, absDest);

                    // .meta を必ず同伴する（GUID 保持）
                    MoveMetaIfExists(absSource, absDest);

                    manifest.entries.Add(new RelocationEntry
                    {
                        originalPath = t.UnitPath,
                        archivedRelativePath = t.UnitPath,
                        isDirectory = isDir,
                    });
                    movedCount++;
                }

                WriteManifest(trashRoot, manifest);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }

            return trashRoot;
        }

        /// <summary>退避フォルダから復元する。復元件数を返す。</summary>
        /// <param name="trashFolderRemoved">全件復元し退避フォルダを削除した場合 true。</param>
        public static int Restore(string trashRootAbs, out bool trashFolderRemoved)
        {
            trashFolderRemoved = false;
            var manifest = ReadManifest(trashRootAbs);
            if (manifest == null) return -1; // マニフェスト無し

            int restored = 0;
            try
            {
                for (int i = 0; i < manifest.entries.Count; i++)
                {
                    var e = manifest.entries[i];
                    EditorUtility.DisplayProgressBar(
                        AkmStrings.ToolName, AkmStrings.ProgressRestore + "\n" + e.originalPath,
                        (float)i / Math.Max(1, manifest.entries.Count));

                    var absSource = AkmUtil.Normalize(Path.Combine(trashRootAbs, e.archivedRelativePath));
                    var absDest = AkmUtil.ToAbsolute(e.originalPath);

                    if (e.isDirectory ? Directory.Exists(absDest) : File.Exists(absDest))
                    {
                        Debug.LogWarning($"[{AkmStrings.ToolName}] 復元先が既に存在します。スキップ: {e.originalPath}");
                        continue;
                    }

                    var absDestParent = Path.GetDirectoryName(absDest);
                    if (!string.IsNullOrEmpty(absDestParent)) Directory.CreateDirectory(absDestParent);

                    if (e.isDirectory)
                    {
                        if (!Directory.Exists(absSource)) continue;
                        Directory.Move(absSource, absDest);
                    }
                    else
                    {
                        if (!File.Exists(absSource)) continue;
                        File.Move(absSource, absDest);
                    }
                    MoveMetaIfExists(absSource, absDest);
                    restored++;
                }

                // 全件戻せて、マニフェスト以外にファイルが残っていなければ退避フォルダを削除する。
                // （残るのは退避時に作った空のディレクトリ構造とマニフェストのみ、という想定。）
                if (restored == manifest.entries.Count && !HasRemainingFiles(trashRootAbs))
                {
                    trashFolderRemoved = TryDeleteTrashFolder(trashRootAbs);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
            return restored;
        }

        /// <summary>退避フォルダ内に、マニフェスト以外の実ファイルが残っているか。</summary>
        private static bool HasRemainingFiles(string trashRootAbs)
        {
            foreach (var f in Directory.GetFiles(trashRootAbs, "*", SearchOption.AllDirectories))
            {
                if (!string.Equals(Path.GetFileName(f), ManifestFileName,
                        System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static bool TryDeleteTrashFolder(string trashRootAbs)
        {
            try
            {
                Directory.Delete(trashRootAbs, true);
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning(
                    $"[{AkmStrings.ToolName}] 退避フォルダの削除に失敗しました（手動で削除してください）: {ex.Message}");
                return false;
            }
        }

        /// <summary>指定フォルダが退避フォルダ（マニフェストを持つ）か。</summary>
        public static bool HasManifest(string folderAbs)
        {
            return File.Exists(Path.Combine(folderAbs, ManifestFileName));
        }

        /// <summary>
        /// F-DEL-03: 退避対象を .unitypackage としてエクスポートする。書き出したパスを返す（失敗時 null）。
        /// 依存は含めず（IncludeDependencies を使わない）、退避する範囲のみを再帰的に含める。
        /// </summary>
        private static string ExportBackup(IReadOnlyList<ScanResultEntry> targets, string trashRoot)
        {
            try
            {
                EditorUtility.DisplayProgressBar(
                    AkmStrings.ToolName, AkmStrings.ProgressExportPackage, 0f);
                var paths = targets.Select(t => t.UnitPath)
                    .Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
                if (paths.Length == 0) return null;

                var outPath = AkmUtil.Normalize(Path.Combine(trashRoot, BackupPackageName));
                AssetDatabase.ExportPackage(paths, outPath, ExportPackageOptions.Recurse);
                return outPath;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{AkmStrings.ToolName}] .unitypackage の書き出しに失敗しました: {ex.Message}");
                return null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        /// <summary>退避フォルダ内のアセット数（マニフェスト件数）と合計サイズを見積もる。</summary>
        public static bool TryGetTrashFolderStats(string trashRootAbs, out int count, out long sizeBytes)
        {
            count = 0;
            sizeBytes = 0;
            var manifest = ReadManifest(trashRootAbs);
            if (manifest == null) return false;
            count = manifest.entries.Count;
            sizeBytes = AkmUtil.DirectorySize(trashRootAbs);
            return true;
        }

        /// <summary>
        /// F-DEL-04: 退避フォルダを完全に削除する（取り消し不可）。
        /// 安全のため、本ツールの退避フォルダ（マニフェストを持つ）以外は削除を拒否する。
        /// </summary>
        public static bool PurgeTrashFolder(string trashRootAbs, out string error)
        {
            error = null;
            if (!HasManifest(trashRootAbs))
            {
                error = AkmStrings.PurgeNotTrashFolder;
                return false;
            }
            try
            {
                Directory.Delete(trashRootAbs, true);
                // 退避フォルダは Assets 外なので .meta は無い（Refresh 不要だが念のため）。
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                Debug.LogWarning($"[{AkmStrings.ToolName}] 退避フォルダの完全削除に失敗: {ex.Message}");
                return false;
            }
        }

        private static void MoveMetaIfExists(string absSource, string absDest)
        {
            var srcMeta = absSource + ".meta";
            var dstMeta = absDest + ".meta";
            if (File.Exists(srcMeta))
            {
                if (File.Exists(dstMeta)) File.Delete(dstMeta);
                File.Move(srcMeta, dstMeta);
            }
        }

        private static void WriteManifest(string trashRootAbs, RelocationManifest manifest)
        {
            var json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(Path.Combine(trashRootAbs, ManifestFileName), json);
        }

        private static RelocationManifest ReadManifest(string trashRootAbs)
        {
            var path = Path.Combine(trashRootAbs, ManifestFileName);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<RelocationManifest>(File.ReadAllText(path));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[{AkmStrings.ToolName}] マニフェスト読み込み失敗: {ex.Message}");
                return null;
            }
        }
    }
}
