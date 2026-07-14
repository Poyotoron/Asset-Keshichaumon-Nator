using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Maaaaa.Akn.Editor
{
    /// <summary>ルート集合の収集結果。</summary>
    internal class RootSet
    {
        /// <summary>依存グラフの起点にする全アセットパス（アバター系 + 暗黙ルート）。</summary>
        public readonly List<string> AllRoots = new List<string>();

        /// <summary>アバター Prefab / Scene 由来のルート（空ルートのガード判定に使う）。</summary>
        public readonly List<string> AvatarRoots = new List<string>();

        /// <summary>暗黙ルート（Resources 等）由来のルート。</summary>
        public readonly List<string> ImplicitRoots = new List<string>();

        /// <summary>収集中に出たメッセージ（UI 表示用）。</summary>
        public readonly List<string> Messages = new List<string>();

        /// <summary>アバターの起点が1つも無い＝スキャンをブロックすべき状態。</summary>
        public bool HasNoAvatarRoots => AvatarRoots.Count == 0;
    }

    /// <summary>
    /// ルート集合の決定。取りこぼすと使用中アセットを誤判定するため最重要。
    /// </summary>
    internal static class RootCollector
    {
        // 暗黙ルートとみなす特殊フォルダ名
        private static readonly string[] ImplicitFolderMarkers =
        {
            "/Resources/",
            "/Editor Default Resources/",
            "/StreamingAssets/",
        };

        public static RootSet Collect(AknSettings settings)
        {
            var set = new RootSet();
            var avatar = new HashSet<string>();
            var implicitRoots = new HashSet<string>();

            // --- アバタールートディレクトリを再帰走査して .prefab / .unity を採用 ---
            foreach (var dir in settings.avatarRootDirectories.Where(d => !string.IsNullOrEmpty(d)))
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    set.Messages.Add($"登録フォルダが見つかりません: {dir}");
                    continue;
                }
                foreach (var p in FindPrefabsAndScenesUnder(dir))
                {
                    avatar.Add(p);
                }
            }

            // --- 個別ファイル指定 ---
            foreach (var p in settings.additionalRootAssets.Where(a => !string.IsNullOrEmpty(a)))
            {
                var ext = AknUtil.Ext(p);
                if (ext == ".prefab" || ext == ".unity")
                {
                    avatar.Add(p);
                }
            }

            // --- 自動検出（除外されていないもの）---
            var excluded = new HashSet<string>(settings.excludedAutoDetectedRoots ?? new List<string>());
            foreach (var p in AvatarAutoDetector.FindAvatarPrefabs())
            {
                if (!excluded.Contains(p)) avatar.Add(p);
            }

            // --- Build Settings の有効な Scene もアバター系ルート扱い ---
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled && !string.IsNullOrEmpty(scene.path) && File.Exists(AknUtil.ToAbsolute(scene.path)))
                {
                    avatar.Add(scene.path);
                }
            }

            // --- 暗黙ルート ---
            CollectImplicitRoots(implicitRoots);

            set.AvatarRoots.AddRange(avatar.OrderBy(x => x));
            set.ImplicitRoots.AddRange(implicitRoots.OrderBy(x => x));
            set.AllRoots.AddRange(avatar);
            foreach (var p in implicitRoots)
            {
                if (!avatar.Contains(p)) set.AllRoots.Add(p);
            }

            set.Messages.Add($"アバター系ルート: {set.AvatarRoots.Count} 件、暗黙ルート: {set.ImplicitRoots.Count} 件");
            return set;
        }

        private static IEnumerable<string> FindPrefabsAndScenesUnder(string folderAssetPath)
        {
            // t:Prefab / t:Scene を対象フォルダに絞って検索
            var guids = AssetDatabase.FindAssets("t:Prefab t:Scene", new[] { folderAssetPath });
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var ext = AknUtil.Ext(path);
                if (ext == ".prefab" || ext == ".unity")
                {
                    yield return path;
                }
            }
        }

        private static void CollectImplicitRoots(HashSet<string> implicitRoots)
        {
            foreach (var path in AssetDatabase.GetAllAssetPaths())
            {
                if (!path.StartsWith("Assets/")) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;
                if (path.EndsWith(".meta")) continue;

                // Resources / Editor Default Resources / StreamingAssets 配下
                if (ImplicitFolderMarkers.Any(m => path.Contains(m)))
                {
                    implicitRoots.Add(path);
                    continue;
                }

                // Assets 直下の *.asset（設定系 ScriptableObject の可能性）
                if (AknUtil.Ext(path) == ".asset")
                {
                    // "Assets/xxx.asset" のように直下だけを対象
                    var rel = path.Substring("Assets/".Length);
                    if (!rel.Contains("/"))
                    {
                        implicitRoots.Add(path);
                    }
                }
            }
        }
    }
}
