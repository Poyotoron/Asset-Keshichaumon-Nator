using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Maaaaa.Akn.Editor
{
    /// <summary>
    /// 中核アルゴリズム。GC の Mark &amp; Sweep と同型。
    ///   ルート R  … RootCollector が返す起点
    ///   到達 M   … GetDependencies(R, recursive) の推移閉包
    ///   保護 P   … ProtectionRules
    ///   退避候補 … 全アセット − M − P（導入単位フォルダ粒度）
    ///
    /// 本クラスは常に非破壊（ドライラン）。ファイル移動は AssetRelocator が担う。
    /// </summary>
    internal static class UnusedAssetScanner
    {
        private const int ImplicitRootAttributionLimit = 200;

        public static ScanResult Scan(AknSettings settings, RootSet roots)
        {
            var result = new ScanResult
            {
                RootCount = roots.AvatarRoots.Count,
                ScopeDirectories = new List<string>(settings.scanScopeDirectories),
            };

            // --- Mark: ルートからの到達集合（キャッシュ利用）---
            EditorUtility.DisplayProgressBar(AknStrings.ProgressTitle, AknStrings.ProgressBuildReachable, 0.2f);
            var reachable = DependencyCache.GetReachable(roots.AllRoots);

            // 説明表示にだけ使う。候補判定は従来どおり全ルートの reachable で行う。
            EditorUtility.DisplayProgressBar(
                AknStrings.ProgressTitle, AknStrings.ProgressBuildAvatarReachable, 0.3f);
            var reachableAvatarOnly = new HashSet<string>(
                AssetDatabase.GetDependencies(roots.AvatarRoots.ToArray(), true), StringComparer.Ordinal);

            // --- 全アセット列挙（Assets 配下のファイルのみ）---
            EditorUtility.DisplayProgressBar(AknStrings.ProgressTitle, AknStrings.ProgressEnumerate, 0.4f);
            var allFiles = new List<string>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/")) continue;
                if (path.EndsWith(".meta")) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;
                allFiles.Add(path);
            }

            // --- 判定単位と保護判定の文脈へ集約 ---
            // ツール出力フォルダ内では判定単位をファイルまで細かくする一方、保護判定は
            // 従来どおり出力フォルダ直下のエントリ全体を文脈として安全側に判定する。
            EditorUtility.DisplayProgressBar(AknStrings.ProgressTitle, AknStrings.ProgressClassify, 0.6f);
            var unitOf = new UnitResolver(settings, allFiles);
            var folderUnits = new Dictionary<string, List<string>>();
            var protectionContexts = new Dictionary<string, List<string>>();
            var protectionContextOf = new Dictionary<string, string>();
            foreach (var f in allFiles)
            {
                var unit = unitOf.Resolve(f);
                var protectionContext = unitOf.ResolveProtectionContext(f);
                protectionContextOf[f] = protectionContext;
                if (!folderUnits.TryGetValue(unit, out var list))
                {
                    list = new List<string>();
                    folderUnits[unit] = list;
                }
                list.Add(f);
                if (!protectionContexts.TryGetValue(protectionContext, out var contextFiles))
                {
                    contextFiles = new List<string>();
                    protectionContexts[protectionContext] = contextFiles;
                }
                contextFiles.Add(f);
            }

            var whitelist = settings.userWhitelistGlobs;
            var excludedImplicitRoots = new HashSet<string>(
                roots.ExcludedImplicitRoots ?? new List<string>(), StringComparer.Ordinal);
            var implicitRoots = new HashSet<string>(roots.ImplicitRoots, StringComparer.Ordinal);
            var protectedByUnit = new Dictionary<string, ProtectedUnitEntry>();
            var implicitOnlyUsedByUnit = new Dictionary<string, ImplicitOnlyUsedEntry>();

            if (settings.fileUnitMode)
            {
                int fi = 0;
                foreach (var f in allFiles)
                {
                    if ((fi++ & 255) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            AknStrings.ProgressTitle, AknStrings.ProgressClassify,
                            0.6f + 0.4f * fi / Math.Max(1, allFiles.Count));
                    }

                    if (!IsInScope(f, settings.scanScopeDirectories))
                    {
                        result.OutOfScopeUnits++;
                        continue;
                    }

                    result.TotalUnits++;
                    if (reachable.Contains(f))
                    {
                        if (reachableAvatarOnly.Contains(f))
                        {
                            result.UsedUnits++;
                            continue;
                        }

                        var usedContextPath = protectionContextOf[f];
                        var usedContextFiles = protectionContexts[usedContextPath];
                        var usedProtectionPath = unitOf.IsInToolOutput(f) ? usedContextPath : f;
                        if (ProtectionRules.IsProtectedUnit(
                                usedProtectionPath, usedContextFiles, whitelist, out var usedReason))
                        {
                            result.ProtectedCount++;
                            AddProtectedEntry(
                                protectedByUnit, usedContextPath, usedReason, f, false);
                        }
                        else if (implicitRoots.Contains(f))
                        {
                            result.ProtectedCount++;
                            AddProtectedEntry(protectedByUnit, usedContextPath,
                                AknStrings.ReasonImplicitRoot, f, false);
                        }
                        else
                        {
                            result.UsedUnits++;
                            AddImplicitOnlyUsedEntry(
                                implicitOnlyUsedByUnit, usedContextPath, new[] { f });
                        }
                        continue;
                    }

                    var unitFiles = new List<string> { f };
                    var contextPath = protectionContextOf[f];
                    var ctxFiles = protectionContexts[contextPath];
                    bool excludedRoot = ProtectionRules.IsExcludedRootUnit(
                        unitFiles, excludedImplicitRoots, out var reason);
                    var protectionPath = unitOf.IsInToolOutput(f) ? contextPath : f;
                    if (excludedRoot ||
                        ProtectionRules.IsProtectedUnit(protectionPath, ctxFiles, whitelist, out reason))
                    {
                        result.ProtectedCount++;
                        AddProtectedEntry(protectedByUnit, contextPath, reason, f, excludedRoot);
                        continue;
                    }

                    result.Candidates.Add(new ScanResultEntry
                    {
                        UnitPath = f,
                        ContainedFiles = unitFiles,
                        SizeBytes = AknUtil.FileSize(f),
                        Kind = ComputeKind(unitFiles, out var kd),
                        KindDetail = kd,
                        Reason = AknStrings.ReasonUnreachable,
                        Selected = false,
                    });
                }
            }
            else
            {
                int i = 0;
                foreach (var kv in folderUnits)
                {
                    if ((i++ & 63) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            AknStrings.ProgressTitle, AknStrings.ProgressClassify,
                            0.6f + 0.4f * i / folderUnits.Count);
                    }

                    var unitPath = kv.Key;
                    var files = kv.Value;

                    if (!IsInScope(unitPath, settings.scanScopeDirectories))
                    {
                        result.OutOfScopeUnits++;
                        continue;
                    }

                    result.TotalUnits++;

                    // 使用中: フォルダ内に到達アセットが1つでもあれば使用中とみなす
                    if (files.Any(f => reachable.Contains(f)))
                    {
                        if (files.Any(f => reachableAvatarOnly.Contains(f)))
                        {
                            result.UsedUnits++;
                            continue;
                        }

                        var usedContextPath = protectionContextOf[files[0]];
                        var usedContextFiles = protectionContexts[usedContextPath];
                        if (ProtectionRules.IsProtectedUnit(
                                usedContextPath, usedContextFiles, whitelist, out var usedReason))
                        {
                            result.ProtectedCount++;
                            foreach (var file in files)
                                AddProtectedEntry(
                                    protectedByUnit, usedContextPath, usedReason, file, false);
                        }
                        else if (files.Any(implicitRoots.Contains))
                        {
                            result.ProtectedCount++;
                            foreach (var file in files)
                                AddProtectedEntry(protectedByUnit, usedContextPath,
                                    AknStrings.ReasonImplicitRoot, file, false);
                        }
                        else
                        {
                            result.UsedUnits++;
                            AddImplicitOnlyUsedEntry(implicitOnlyUsedByUnit, unitPath, files);
                        }
                        continue;
                    }

                    var contextPath = protectionContextOf[files[0]];
                    var ctxFiles = protectionContexts[contextPath];
                    bool excludedRoot = ProtectionRules.IsExcludedRootUnit(
                        files, excludedImplicitRoots, out var reason);
                    if (excludedRoot ||
                        ProtectionRules.IsProtectedUnit(contextPath, ctxFiles, whitelist, out reason))
                    {
                        result.ProtectedCount++;
                        foreach (var file in files)
                            AddProtectedEntry(protectedByUnit, contextPath, reason, file, excludedRoot);
                        continue;
                    }

                    result.Candidates.Add(new ScanResultEntry
                    {
                        UnitPath = unitPath,
                        ContainedFiles = files,
                        SizeBytes = files.Sum(AknUtil.FileSize),
                        Kind = ComputeKind(files, out var kindDetail),
                        KindDetail = kindDetail,
                        Reason = AknStrings.ReasonUnreachable,
                        Selected = false, // 既定は全選択 OFF（明示選択させる）
                    });
                }
            }

            result.ProtectedEntries.AddRange(protectedByUnit.Values);
            result.ImplicitOnlyUsedEntries.AddRange(implicitOnlyUsedByUnit.Values);
            AssignImplicitRoots(result, roots);

            BuildCandidateGroups(result.Candidates);
            SortCandidateGroups(result.Candidates);
            result.ProtectedEntries.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            result.ImplicitOnlyUsedEntries.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            return result;
        }

        private static void AddImplicitOnlyUsedEntry(
            IDictionary<string, ImplicitOnlyUsedEntry> entries,
            string unitPath,
            IEnumerable<string> files)
        {
            if (!entries.TryGetValue(unitPath, out var entry))
            {
                entry = new ImplicitOnlyUsedEntry { UnitPath = unitPath };
                entries[unitPath] = entry;
            }
            foreach (var file in files)
            {
                entry.ContainedFiles.Add(file);
                entry.FileCount++;
                entry.SizeBytes += AknUtil.FileSize(file);
            }
        }

        private static void AssignImplicitRoots(ScanResult result, RootSet roots)
        {
            if (result.ImplicitOnlyUsedEntries.Count == 0) return;
            if (roots.ImplicitRoots.Count > ImplicitRootAttributionLimit)
            {
                result.ImplicitRootAttributionSkipped = true;
                return;
            }

            for (int i = 0; i < roots.ImplicitRoots.Count; i++)
            {
                EditorUtility.DisplayProgressBar(
                    AknStrings.ProgressTitle, AknStrings.ProgressAssignImplicitRoots,
                    (float)i / Math.Max(1, roots.ImplicitRoots.Count));
                var implicitRoot = roots.ImplicitRoots[i];
                var reachableFromRoot = new HashSet<string>(
                    AssetDatabase.GetDependencies(new[] { implicitRoot }, true), StringComparer.Ordinal);
                foreach (var entry in result.ImplicitOnlyUsedEntries)
                {
                    if (entry.ContainedFiles.Any(reachableFromRoot.Contains))
                        entry.PinnedByImplicitRoots.Add(implicitRoot);
                }
            }
        }

        private static void AddProtectedEntry(
            IDictionary<string, ProtectedUnitEntry> protectedByUnit,
            string contextPath,
            string reason,
            string file,
            bool excludedRoot)
        {
            if (!protectedByUnit.TryGetValue(contextPath, out var entry))
            {
                entry = new ProtectedUnitEntry { UnitPath = contextPath, Reason = reason };
                protectedByUnit[contextPath] = entry;
            }
            else if (excludedRoot)
            {
                // 起点から外したというユーザー操作を、最も説明的な保護理由として優先する。
                entry.Reason = reason;
            }
            entry.FileCount++;
            entry.SizeBytes += AknUtil.FileSize(file);
        }

        private static void BuildCandidateGroups(List<ScanResultEntry> candidates)
        {
            int count = candidates.Count;
            var parent = Enumerable.Range(0, count).ToArray();
            var candidateOfFile = new Dictionary<string, int>();
            for (int i = 0; i < count; i++)
                foreach (var file in candidates[i].ContainedFiles) candidateOfFile[file] = i;

            for (int i = 0; i < count; i++)
            {
                if ((i & 63) == 0)
                {
                    EditorUtility.DisplayProgressBar(AknStrings.ProgressTitle,
                        AknStrings.ProgressBuildCandidateGroups, (float)i / count);
                }
                var dependencies = AssetDatabase.GetDependencies(candidates[i].ContainedFiles.ToArray(), true);
                foreach (var dependency in dependencies)
                {
                    if (!candidateOfFile.TryGetValue(dependency, out var j) || i == j) continue;
                    Union(parent, i, j);
                    if (!candidates[j].ReferencedByUnits.Contains(candidates[i].UnitPath))
                        candidates[j].ReferencedByUnits.Add(candidates[i].UnitPath);
                }
            }

            var groupIds = new Dictionary<int, int>();
            int nextGroupId = 0;
            for (int i = 0; i < count; i++)
            {
                int root = Find(parent, i);
                if (!groupIds.TryGetValue(root, out var groupId))
                    groupIds[root] = groupId = nextGroupId++;
                candidates[i].GroupId = groupId;
                candidates[i].ReferencedByUnits.Sort(StringComparer.Ordinal);
            }
        }

        private static int Find(int[] parent, int x)
        {
            while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; }
            return x;
        }

        private static void Union(int[] parent, int a, int b)
        {
            a = Find(parent, a); b = Find(parent, b);
            if (a != b) parent[b] = a;
        }

        private static void SortCandidateGroups(List<ScanResultEntry> candidates)
        {
            var sorted = candidates.GroupBy(c => c.GroupId)
                .Select(g => new { Total = g.Sum(c => c.SizeBytes), Members = g.OrderByDescending(c => c.SizeBytes) })
                .OrderByDescending(g => g.Total)
                .SelectMany(g => g.Members).ToList();
            candidates.Clear();
            candidates.AddRange(sorted);
        }

        private static bool IsInScope(string path, List<string> scopeDirectories)
        {
            if (scopeDirectories == null || scopeDirectories.Count == 0) return true;
            return scopeDirectories.Any(scope =>
                path == scope || path.StartsWith(scope + "/", StringComparison.Ordinal));
        }

        // 内訳表示の並び順
        private static readonly AssetKind[] KindDetailOrder =
        {
            AssetKind.Model, AssetKind.Texture, AssetKind.Material, AssetKind.Animation,
        };

        /// <summary>
        /// フォルダの代表種別を決める。意味のある種別（Model/Texture/Material/Animation）が
        /// 2 種類以上含まれていれば「混在」とする。単一種類ならその種別、無ければ「その他」。
        /// あわせて内訳文字列（"Model 1, Texture 12" 等）を返す。
        /// </summary>
        private static AssetKind ComputeKind(List<string> files, out string detail)
        {
            var counts = new Dictionary<AssetKind, int>();
            foreach (var f in files)
            {
                var k = AknUtil.ClassifyKind(f);
                if (k == AssetKind.Other) continue;
                counts[k] = counts.TryGetValue(k, out var c) ? c + 1 : 1;
            }

            if (counts.Count == 0)
            {
                detail = AknUtil.KindLabel(AssetKind.Other);
                return AssetKind.Other;
            }

            detail = string.Join(", ", KindDetailOrder
                .Where(counts.ContainsKey)
                .Select(k => $"{AknUtil.KindLabel(k)} {counts[k]}"));

            // 2 種類以上の意味のある種別が混ざっていれば「混在」
            return counts.Count >= 2 ? AssetKind.Mixed : counts.Keys.First();
        }
    }

    /// <summary>
    /// アセットを導入単位フォルダへ対応付ける。
    /// - 固定深度モード: Assets から granularityDepth 階層目のフォルダ。
    /// - 自動推定モード: 単一子フォルダの「ラッパー」を畳んだ、最も浅いコンテンツフォルダ。
    /// </summary>
    internal class UnitResolver
    {
        private readonly AknSettings _settings;
        private readonly List<string> _toolOutputDirectories;

        // 自動推定用: 各フォルダの直下ファイル有無 / 直下子フォルダ集合
        private readonly HashSet<string> _foldersWithDirectFiles = new HashSet<string>();
        private readonly Dictionary<string, HashSet<string>> _childFolders =
            new Dictionary<string, HashSet<string>>();

        public UnitResolver(AknSettings settings, List<string> allFiles)
        {
            _settings = settings;
            _toolOutputDirectories = (settings.toolOutputDirectories ?? new List<string>())
                .Where(p => !string.IsNullOrEmpty(p)).OrderByDescending(p => p.Length).ToList();
            if (settings.autoEstimateGranularity)
            {
                BuildFolderTree(allFiles);
            }
        }

        public string Resolve(string assetPath)
        {
            if (IsInToolOutput(assetPath)) return assetPath;
            return _settings.autoEstimateGranularity
                ? ResolveAuto(assetPath)
                : ResolveFixedDepth(assetPath, _settings.granularityDepth);
        }

        public string ResolveProtectionContext(string assetPath)
        {
            foreach (var output in _toolOutputDirectories)
            {
                var prefix = output.TrimEnd('/') + "/";
                if (!assetPath.StartsWith(prefix, StringComparison.Ordinal)) continue;
                var remainder = assetPath.Substring(prefix.Length);
                int slash = remainder.IndexOf('/');
                return slash < 0 ? assetPath : prefix + remainder.Substring(0, slash);
            }
            return _settings.autoEstimateGranularity
                ? ResolveAuto(assetPath)
                : ResolveFixedDepth(assetPath, _settings.granularityDepth);
        }

        public bool IsInToolOutput(string assetPath)
        {
            return _toolOutputDirectories.Any(output => assetPath.StartsWith(
                output.TrimEnd('/') + "/", StringComparison.Ordinal));
        }

        private static string ResolveFixedDepth(string assetPath, int depth)
        {
            if (depth < 1) depth = 1;
            var parts = assetPath.Split('/'); // ["Assets", ...seg..., "file.ext"]
            int folderSegments = parts.Length - 2; // "Assets" と ファイル名 を除く
            if (folderSegments <= 0)
            {
                // Assets 直下のファイル → ファイル自身を単位にする
                return assetPath;
            }
            int take = System.Math.Min(depth, folderSegments);
            return string.Join("/", parts.Take(1 + take));
        }

        private string ResolveAuto(string assetPath)
        {
            var parts = assetPath.Split('/');
            int folderSegments = parts.Length - 2;
            if (folderSegments <= 0) return assetPath;

            // Assets からアセットへ向かって降り、最初の「コンテンツフォルダ」で止まる。
            // コンテンツフォルダ = 直下にファイルを持つ or 子フォルダが2つ以上ある。
            // それ以外（単一子フォルダの純ラッパー）は畳んで降りる。
            string cur = "Assets";
            for (int i = 1; i <= folderSegments; i++)
            {
                cur = cur + "/" + parts[i];
                bool hasDirectFiles = _foldersWithDirectFiles.Contains(cur);
                int childCount = _childFolders.TryGetValue(cur, out var kids) ? kids.Count : 0;
                bool isDeepest = i == folderSegments;
                if (hasDirectFiles || childCount >= 2 || isDeepest)
                {
                    return cur;
                }
            }
            return cur;
        }

        private void BuildFolderTree(List<string> allFiles)
        {
            foreach (var f in allFiles)
            {
                var parts = f.Split('/');
                int folderSegments = parts.Length - 2;
                string cur = "Assets";
                for (int i = 1; i <= folderSegments; i++)
                {
                    string parent = cur;
                    cur = cur + "/" + parts[i];
                    if (!_childFolders.TryGetValue(parent, out var kids))
                    {
                        kids = new HashSet<string>();
                        _childFolders[parent] = kids;
                    }
                    kids.Add(cur);
                }
                // 直下ファイルを持つフォルダ = ファイルの親フォルダ
                if (folderSegments >= 1)
                {
                    string parentFolder = string.Join("/", parts.Take(parts.Length - 1));
                    _foldersWithDirectFiles.Add(parentFolder);
                }
            }
        }
    }
}
