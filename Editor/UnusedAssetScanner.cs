using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Maaaaa.Akm.Editor
{
    /// <summary>
    /// 中核アルゴリズム（要件 §2）。GC の Mark &amp; Sweep と同型。
    ///   ルート R  … RootCollector が返す起点
    ///   到達 M   … GetDependencies(R, recursive) の推移閉包
    ///   保護 P   … ProtectionRules
    ///   退避候補 … 全アセット − M − P（導入単位フォルダ粒度、§5）
    ///
    /// 本クラスは常に非破壊（ドライラン）。ファイル移動は AssetRelocator が担う。
    /// </summary>
    internal static class UnusedAssetScanner
    {
        public static ScanResult Scan(AkmSettings settings, RootSet roots)
        {
            var result = new ScanResult { RootCount = roots.AvatarRoots.Count };

            // --- Mark: ルートからの到達集合（NFR-01: キャッシュ利用）---
            EditorUtility.DisplayProgressBar(AkmStrings.ProgressTitle, AkmStrings.ProgressBuildReachable, 0.2f);
            var reachable = DependencyCache.GetReachable(roots.AllRoots);

            // --- 全アセット列挙（Assets 配下のファイルのみ）---
            EditorUtility.DisplayProgressBar(AkmStrings.ProgressTitle, AkmStrings.ProgressEnumerate, 0.4f);
            var allFiles = new List<string>();
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/")) continue;
                if (path.EndsWith(".meta")) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;
                allFiles.Add(path);
            }

            // --- 導入単位フォルダへ集約（§5）---
            // ファイル単位モード（F-GRAN-03）でも、保護判定は「その導入単位フォルダの中身」を
            // 文脈として使う。これによりコード/シェーダーを含むフォルダ内の個別ファイルも保護され、
            // 安全性（P-4）を保ったまま列挙粒度だけを細かくできる。
            EditorUtility.DisplayProgressBar(AkmStrings.ProgressTitle, AkmStrings.ProgressClassify, 0.6f);
            var unitOf = new UnitResolver(settings, allFiles);
            var folderUnits = new Dictionary<string, List<string>>();
            var folderUnitOf = new Dictionary<string, string>();
            foreach (var f in allFiles)
            {
                var unit = unitOf.Resolve(f);
                folderUnitOf[f] = unit;
                if (!folderUnits.TryGetValue(unit, out var list))
                {
                    list = new List<string>();
                    folderUnits[unit] = list;
                }
                list.Add(f);
            }

            var whitelist = settings.userWhitelistGlobs;

            if (settings.fileUnitMode)
            {
                result.TotalUnits = allFiles.Count;
                int fi = 0;
                foreach (var f in allFiles)
                {
                    if ((fi++ & 255) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            AkmStrings.ProgressTitle, AkmStrings.ProgressClassify,
                            0.6f + 0.4f * fi / Math.Max(1, allFiles.Count));
                    }

                    if (reachable.Contains(f)) { result.UsedUnits++; continue; }

                    // 保護文脈 = このファイルが属する導入単位フォルダの中身
                    var ctxFiles = folderUnits[folderUnitOf[f]];
                    if (ProtectionRules.IsProtectedUnit(f, ctxFiles, whitelist, out _))
                    {
                        result.ProtectedUnits++;
                        continue;
                    }

                    var single = new List<string> { f };
                    result.Candidates.Add(new ScanResultEntry
                    {
                        UnitPath = f,
                        ContainedFiles = single,
                        SizeBytes = AkmUtil.FileSize(f),
                        Kind = ComputeKind(single, out var kd),
                        KindDetail = kd,
                        Reason = AkmStrings.ReasonUnreachable,
                        Selected = false,
                    });
                }
            }
            else
            {
                result.TotalUnits = folderUnits.Count;
                int i = 0;
                foreach (var kv in folderUnits)
                {
                    if ((i++ & 63) == 0)
                    {
                        EditorUtility.DisplayProgressBar(
                            AkmStrings.ProgressTitle, AkmStrings.ProgressClassify,
                            0.6f + 0.4f * i / folderUnits.Count);
                    }

                    var unitPath = kv.Key;
                    var files = kv.Value;

                    // 使用中: フォルダ内に到達アセットが1つでもあれば使用中（F-GRAN-01）
                    if (files.Any(f => reachable.Contains(f))) { result.UsedUnits++; continue; }

                    // 保護（§4）
                    if (ProtectionRules.IsProtectedUnit(unitPath, files, whitelist, out _))
                    {
                        result.ProtectedUnits++;
                        continue;
                    }

                    result.Candidates.Add(new ScanResultEntry
                    {
                        UnitPath = unitPath,
                        ContainedFiles = files,
                        SizeBytes = files.Sum(AkmUtil.FileSize),
                        Kind = ComputeKind(files, out var kindDetail),
                        KindDetail = kindDetail,
                        Reason = AkmStrings.ReasonUnreachable,
                        Selected = false, // 既定は全選択 OFF（§7.3）
                    });
                }
            }

            // サイズ降順ソートを既定（§7.3）
            result.Candidates.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            return result;
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
                var k = AkmUtil.ClassifyKind(f);
                if (k == AssetKind.Other) continue;
                counts[k] = counts.TryGetValue(k, out var c) ? c + 1 : 1;
            }

            if (counts.Count == 0)
            {
                detail = AkmUtil.KindLabel(AssetKind.Other);
                return AssetKind.Other;
            }

            detail = string.Join(", ", KindDetailOrder
                .Where(counts.ContainsKey)
                .Select(k => $"{AkmUtil.KindLabel(k)} {counts[k]}"));

            // 2 種類以上の意味のある種別が混ざっていれば「混在」
            return counts.Count >= 2 ? AssetKind.Mixed : counts.Keys.First();
        }
    }

    /// <summary>
    /// アセットを導入単位フォルダへ対応付ける（F-GRAN-02）。
    /// - 固定深度モード: Assets から granularityDepth 階層目のフォルダ。
    /// - 自動推定モード: 単一子フォルダの「ラッパー」を畳んだ、最も浅いコンテンツフォルダ。
    /// </summary>
    internal class UnitResolver
    {
        private readonly AkmSettings _settings;

        // 自動推定用: 各フォルダの直下ファイル有無 / 直下子フォルダ集合
        private readonly HashSet<string> _foldersWithDirectFiles = new HashSet<string>();
        private readonly Dictionary<string, HashSet<string>> _childFolders =
            new Dictionary<string, HashSet<string>>();

        public UnitResolver(AkmSettings settings, List<string> allFiles)
        {
            _settings = settings;
            if (settings.autoEstimateGranularity)
            {
                BuildFolderTree(allFiles);
            }
        }

        public string Resolve(string assetPath)
        {
            return _settings.autoEstimateGranularity
                ? ResolveAuto(assetPath)
                : ResolveFixedDepth(assetPath, _settings.granularityDepth);
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
