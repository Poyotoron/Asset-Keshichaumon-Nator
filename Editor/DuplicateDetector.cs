using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;

namespace Maaaaa.Akm.Editor
{
    /// <summary>重複グループ内の1ファイル。</summary>
    internal class DuplicateFile
    {
        public string Path;
        public bool? Used; // null = ルート未設定で判定不能
    }

    /// <summary>内容が同一（同一 SHA-256）のファイル群。</summary>
    internal class DuplicateGroup
    {
        public string Hash;
        public long FileSize;
        public List<DuplicateFile> Files = new List<DuplicateFile>();

        /// <summary>重複により余分に消費している容量（1つ残す前提）。</summary>
        public long WastedBytes => FileSize * (Files.Count - 1);
    }

    internal class DuplicateReport
    {
        public List<DuplicateGroup> Groups = new List<DuplicateGroup>();
        public bool UsedKnown;
        public long TotalWasted => Groups.Sum(g => g.WastedBytes);
    }

    /// <summary>
    /// 重複アセット検出（要件 §10 Phase 2「使用中だが重複しているアセット」）。
    /// SHA-256 で内容が完全一致するファイルを検出する。検出（レポート）のみで、破壊操作はしない。
    /// 参照されている重複を消すと壊れるため、判断はユーザーに委ねる（設計原則 P-4）。
    ///
    /// 最適化: まずファイルサイズでグルーピングし、同一サイズが2つ以上ある群だけをハッシュする。
    /// </summary>
    internal static class DuplicateDetector
    {
        public static DuplicateReport Scan(AkmSettings settings)
        {
            var report = new DuplicateReport();

            // 使用中判定用の到達集合（ルートがあれば）
            HashSet<string> reachable = null;
            try
            {
                var roots = RootCollector.Collect(settings);
                if (!roots.HasNoAvatarRoots)
                {
                    reachable = DependencyCache.GetReachable(roots.AllRoots);
                    report.UsedKnown = true;
                }
            }
            catch { /* 判定不能でもレポート自体は出す */ }

            // --- 全アセット列挙 + サイズ別グルーピング ---
            EditorUtility.DisplayProgressBar(AkmStrings.ToolName, AkmStrings.ProgressEnumerate, 0.1f);
            var bySize = new Dictionary<long, List<string>>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/")) continue;
                if (path.EndsWith(".meta")) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;

                long size = AkmUtil.FileSize(path);
                if (size <= 0) continue; // 空ファイルは対象外

                if (!bySize.TryGetValue(size, out var list))
                {
                    list = new List<string>();
                    bySize[size] = list;
                }
                list.Add(path);
            }

            // 同一サイズが2件以上ある群だけをハッシュ対象にする
            var sizeGroups = bySize.Where(kv => kv.Value.Count >= 2).ToList();
            int totalToHash = sizeGroups.Sum(kv => kv.Value.Count);
            int hashed = 0;

            var byHash = new Dictionary<string, DuplicateGroup>();
            try
            {
                foreach (var kv in sizeGroups)
                {
                    foreach (var path in kv.Value)
                    {
                        if ((hashed++ & 31) == 0)
                        {
                            EditorUtility.DisplayProgressBar(
                                AkmStrings.ToolName, AkmStrings.ProgressHashing,
                                0.1f + 0.9f * hashed / System.Math.Max(1, totalToHash));
                        }

                        var hash = ComputeSha256(AkmUtil.ToAbsolute(path));
                        if (hash == null) continue;

                        if (!byHash.TryGetValue(hash, out var group))
                        {
                            group = new DuplicateGroup { Hash = hash, FileSize = kv.Key };
                            byHash[hash] = group;
                        }
                        group.Files.Add(new DuplicateFile
                        {
                            Path = path,
                            Used = reachable != null ? reachable.Contains(path) : (bool?)null,
                        });
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            report.Groups = byHash.Values
                .Where(g => g.Files.Count >= 2)
                .OrderByDescending(g => g.WastedBytes)
                .ToList();
            foreach (var g in report.Groups)
                g.Files.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

            return report;
        }

        private static string ComputeSha256(string absPath)
        {
            try
            {
                using (var sha = SHA256.Create())
                using (var stream = File.OpenRead(absPath))
                {
                    var bytes = sha.ComputeHash(stream);
                    var sb = new System.Text.StringBuilder(bytes.Length * 2);
                    foreach (var b in bytes) sb.Append(b.ToString("x2"));
                    return sb.ToString();
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
