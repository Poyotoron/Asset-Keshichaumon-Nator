using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Maaaaa.Akn.Editor
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

        /// <summary>退避によって空になったため畳んだフォルダ（中身は無く、.meta だけを退避している）。</summary>
        public bool wasEmptyFolder;
    }

    [Serializable]
    internal class RelocationManifest
    {
        public string createdAt;
        public string toolVersion = "0.3.0";
        public List<RelocationEntry> entries = new List<RelocationEntry>();
    }

    internal class TrashFolderInfo
    {
        public string AbsPath;
        public string CreatedAt;
        public int EntryCount;
        public long SizeBytes;
        public bool HasBackupPackage;
    }

    /// <summary>
    /// 退避と復元。破壊的操作を担う。
    /// - 退避先は ProjectRoot 直下 _UnusedAssets_yyyyMMdd_HHmmss（Assets 外＝再インポート対象外）。
    /// - .meta を必ず同伴して GUID を保持する。
    /// - 移動マッピングを退避フォルダ内 JSON に記録し、復元を可能にする。
    /// この操作は削除ではなく「移動」であり、常に可逆（元に戻せる）。
    /// </summary>
    internal static class AssetRelocator
    {
        internal const string ManifestFileName = ".akn-relocation.json";
        internal const string TrashPrefix = "_UnusedAssets_";
        private const string BackupPackageName = "backup.unitypackage";

        /// <summary>退避を実行し、作成した退避フォルダの絶対パスを返す。</summary>
        public static string Relocate(IReadOnlyList<ScanResultEntry> targets, out int movedCount)
        {
            return Relocate(targets, false, out movedCount, out _, out _);
        }

        /// <summary>
        /// 退避を実行する。exportPackage=true なら移動前に .unitypackage を書き出す。
        /// </summary>
        /// <param name="exportedPackagePath">書き出した .unitypackage の絶対パス（未書き出しは null）。</param>
        public static string Relocate(
            IReadOnlyList<ScanResultEntry> targets, bool exportPackage,
            out int movedCount, out string exportedPackagePath)
        {
            return Relocate(targets, exportPackage, out movedCount, out exportedPackagePath, out _);
        }

        public static string Relocate(
            IReadOnlyList<ScanResultEntry> targets, bool exportPackage,
            out int movedCount, out string exportedPackagePath, out int foldedFolderCount)
        {
            movedCount = 0;
            exportedPackagePath = null;
            foldedFolderCount = 0;
            var foldersToFold = CollectFoldersThatBecomeEmpty(targets.Select(t => t.UnitPath).ToList());
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var trashRoot = AknUtil.Normalize(Path.Combine(AknUtil.ProjectRoot, TrashPrefix + timestamp));
            Directory.CreateDirectory(trashRoot);

            // 移動前（アセットがまだ Assets/ にある間）に .unitypackage を書き出す。
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
                        AknStrings.ToolName, AknStrings.ProgressRelocate + "\n" + t.UnitPath,
                        (float)i / Math.Max(1, targets.Count));

                    var absSource = AknUtil.ToAbsolute(t.UnitPath);
                    bool isDir = Directory.Exists(absSource);
                    bool isFile = File.Exists(absSource);
                    if (!isDir && !isFile)
                    {
                        Debug.LogWarning($"[{AknStrings.ToolName}] 退避対象が存在しません: {t.UnitPath}");
                        continue;
                    }

                    // 退避先は元の構造をそのまま保持（trashRoot/Assets/...）
                    var absDest = AknUtil.Normalize(Path.Combine(trashRoot, t.UnitPath));
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

                foreach (var folderPath in foldersToFold)
                {
                    var absFolder = AknUtil.ToAbsolute(folderPath);
                    if (!Directory.Exists(absFolder) || Directory.GetFileSystemEntries(absFolder).Length != 0)
                        continue;

                    var absArchivedFolder = AknUtil.Normalize(Path.Combine(trashRoot, folderPath));
                    var absArchivedParent = Path.GetDirectoryName(absArchivedFolder);
                    if (!string.IsNullOrEmpty(absArchivedParent)) Directory.CreateDirectory(absArchivedParent);
                    MoveMetaIfExists(absFolder, absArchivedFolder);
                    Directory.Delete(absFolder);

                    manifest.entries.Add(new RelocationEntry
                    {
                        originalPath = folderPath,
                        archivedRelativePath = folderPath,
                        isDirectory = true,
                        wasEmptyFolder = true,
                    });
                    foldedFolderCount++;
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

        /// <summary>
        /// 指定したユニットを退避したとき、中身が完全に空になる親フォルダを列挙する。
        /// ファイルシステムを読むだけで、何も変更しない。戻り値は深い順。
        /// </summary>
        internal static List<string> CollectFoldersThatBecomeEmpty(IReadOnlyList<string> unitPaths)
        {
            var units = new HashSet<string>(unitPaths.Where(p => !string.IsNullOrEmpty(p)));
            var candidates = new HashSet<string>();
            foreach (var unitPath in units)
            {
                var folderPath = Path.GetDirectoryName(unitPath)?.Replace('\\', '/');
                while (!string.IsNullOrEmpty(folderPath) && folderPath != "Assets")
                {
                    if (!folderPath.StartsWith("Assets/")) break;
                    candidates.Add(folderPath);
                    folderPath = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
                }
            }

            var orderedCandidates = candidates
                .OrderByDescending(path => path.Count(c => c == '/'))
                .ToList();
            var foldersThatBecomeEmpty = new HashSet<string>();

            foreach (var folderPath in orderedCandidates)
            {
                var absFolder = AknUtil.ToAbsolute(folderPath);
                if (!Directory.Exists(absFolder)) continue;

                var entries = Directory.GetFileSystemEntries(absFolder);
                if (entries.Length == 0) continue;

                bool allEntriesWillMove = entries.All(entry =>
                {
                    var assetPath = AknUtil.ToAssetPath(entry);
                    if (Directory.Exists(entry))
                        return units.Contains(assetPath) || foldersThatBecomeEmpty.Contains(assetPath);

                    return units.Contains(assetPath)
                        || (assetPath.EndsWith(".meta")
                            && (units.Contains(assetPath.Substring(0, assetPath.Length - ".meta".Length))
                                || foldersThatBecomeEmpty.Contains(assetPath.Substring(0, assetPath.Length - ".meta".Length))));
                });

                if (allEntriesWillMove) foldersThatBecomeEmpty.Add(folderPath);
            }

            return orderedCandidates.Where(foldersThatBecomeEmpty.Contains).ToList();
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
                var normalEntries = manifest.entries.Where(e => !e.wasEmptyFolder).ToList();
                var emptyFolderEntries = manifest.entries.Where(e => e.wasEmptyFolder).ToList();
                for (int i = 0; i < normalEntries.Count; i++)
                {
                    var e = normalEntries[i];
                    EditorUtility.DisplayProgressBar(
                        AknStrings.ToolName, AknStrings.ProgressRestore + "\n" + e.originalPath,
                        (float)i / Math.Max(1, manifest.entries.Count));

                    var absSource = AknUtil.Normalize(Path.Combine(trashRootAbs, e.archivedRelativePath));
                    var absDest = AknUtil.ToAbsolute(e.originalPath);

                    if (e.isDirectory ? Directory.Exists(absDest) : File.Exists(absDest))
                    {
                        Debug.LogWarning($"[{AknStrings.ToolName}] 復元先が既に存在します。スキップ: {e.originalPath}");
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

                foreach (var e in emptyFolderEntries)
                {
                    var absSource = AknUtil.Normalize(Path.Combine(trashRootAbs, e.archivedRelativePath));
                    var absDest = AknUtil.ToAbsolute(e.originalPath);
                    var sourceMeta = absSource + ".meta";
                    var destinationMeta = absDest + ".meta";

                    if (!Directory.Exists(absDest)) Directory.CreateDirectory(absDest);
                    if (!File.Exists(sourceMeta)) continue;
                    if (File.Exists(destinationMeta))
                    {
                        Debug.LogWarning($"[{AknStrings.ToolName}] {string.Format(AknStrings.RestoreMetaAlreadyExistsFormat, e.originalPath)}");
                        continue;
                    }
                    File.Move(sourceMeta, destinationMeta);
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
                    $"[{AknStrings.ToolName}] 退避フォルダの削除に失敗しました（手動で削除してください）: {ex.Message}");
                return false;
            }
        }

        /// <summary>指定フォルダが退避フォルダ（マニフェストを持つ）か。</summary>
        public static bool HasManifest(string folderAbs)
        {
            return File.Exists(Path.Combine(folderAbs, ManifestFileName));
        }

        /// <summary>プロジェクト直下の退避フォルダを新しい順に列挙する。非破壊。</summary>
        public static List<TrashFolderInfo> FindTrashFolders()
        {
            var folders = new List<TrashFolderInfo>();
            try
            {
                foreach (var path in Directory.GetDirectories(AknUtil.ProjectRoot, TrashPrefix + "*",
                    SearchOption.TopDirectoryOnly))
                {
                    var absPath = AknUtil.Normalize(path);
                    if (!HasManifest(absPath)) continue;

                    var manifest = ReadManifest(absPath);
                    var name = Path.GetFileName(absPath);
                    folders.Add(new TrashFolderInfo
                    {
                        AbsPath = absPath,
                        CreatedAt = !string.IsNullOrEmpty(manifest?.createdAt)
                            ? manifest.createdAt
                            : name.Substring(TrashPrefix.Length),
                        EntryCount = manifest?.entries?.Count ?? 0,
                        SizeBytes = AknUtil.DirectorySize(absPath),
                        HasBackupPackage = File.Exists(Path.Combine(absPath, BackupPackageName)),
                    });
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{AknStrings.ToolName}] {string.Format(AknStrings.RestoreTrashFoldersFindFailedFormat, ex.Message)}");
            }

            return folders
                .OrderByDescending(folder => folder.CreatedAt, StringComparer.Ordinal)
                .ThenByDescending(folder => Path.GetFileName(folder.AbsPath), StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// 退避対象を .unitypackage としてエクスポートする。書き出したパスを返す（失敗時 null）。
        /// 依存は含めず（IncludeDependencies を使わない）、退避する範囲のみを再帰的に含める。
        /// </summary>
        private static string ExportBackup(IReadOnlyList<ScanResultEntry> targets, string trashRoot)
        {
            try
            {
                EditorUtility.DisplayProgressBar(
                    AknStrings.ToolName, AknStrings.ProgressExportPackage, 0f);
                var paths = targets.Select(t => t.UnitPath)
                    .Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray();
                if (paths.Length == 0) return null;

                var outPath = AknUtil.Normalize(Path.Combine(trashRoot, BackupPackageName));
                AssetDatabase.ExportPackage(paths, outPath, ExportPackageOptions.Recurse);
                return outPath;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[{AknStrings.ToolName}] .unitypackage の書き出しに失敗しました: {ex.Message}");
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
            sizeBytes = AknUtil.DirectorySize(trashRootAbs);
            return true;
        }

        /// <summary>
        /// 退避フォルダを完全に削除する（取り消し不可）。
        /// 安全のため、本ツールの退避フォルダ（マニフェストを持つ）以外は削除を拒否する。
        /// </summary>
        public static bool PurgeTrashFolder(string trashRootAbs, out string error)
        {
            error = null;
            if (!HasManifest(trashRootAbs))
            {
                error = AknStrings.PurgeNotTrashFolder;
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
                Debug.LogWarning($"[{AknStrings.ToolName}] 退避フォルダの完全削除に失敗: {ex.Message}");
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
                Debug.LogError($"[{AknStrings.ToolName}] マニフェスト読み込み失敗: {ex.Message}");
                return null;
            }
        }
    }
}
