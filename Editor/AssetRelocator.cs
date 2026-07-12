using System;
using System.Collections.Generic;
using System.IO;
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

        /// <summary>退避を実行し、作成した退避フォルダの絶対パスを返す。</summary>
        public static string Relocate(IReadOnlyList<ScanResultEntry> targets, out int movedCount)
        {
            movedCount = 0;
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var trashRoot = AkmUtil.Normalize(Path.Combine(AkmUtil.ProjectRoot, TrashPrefix + timestamp));
            Directory.CreateDirectory(trashRoot);

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
        public static int Restore(string trashRootAbs)
        {
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
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                AssetDatabase.Refresh();
            }
            return restored;
        }

        /// <summary>指定フォルダが退避フォルダ（マニフェストを持つ）か。</summary>
        public static bool HasManifest(string folderAbs)
        {
            return File.Exists(Path.Combine(folderAbs, ManifestFileName));
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
