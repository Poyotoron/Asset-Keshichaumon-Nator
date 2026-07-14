using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace Maaaaa.Akn.Editor
{
    /// <summary>
    /// 依存グラフ（到達集合）のキャッシュ。
    /// GetDependencies は数万アセットで重いため、同一ルート集合での2回目以降は再利用する。
    /// アセットの変更（インポート・削除・移動）を AssetPostprocessor で検知してキャッシュを無効化する。
    ///
    /// セッション内メモリキャッシュ。ドメインリロードで消えるが、それは安全側（作り直すだけ）。
    /// </summary>
    internal static class DependencyCache
    {
        private static bool _dirty = true;
        private static string _cachedKey;
        private static HashSet<string> _cachedReachable;

        /// <summary>キャッシュを無効化する（アセット変更時に呼ばれる）。</summary>
        public static void Invalidate()
        {
            _dirty = true;
        }

        /// <summary>
        /// ルート集合からの到達集合（推移閉包）を返す。ルートが同一かつ未変更ならキャッシュを返す。
        /// </summary>
        public static HashSet<string> GetReachable(IReadOnlyList<string> allRoots)
        {
            var key = ComputeKey(allRoots);
            if (!_dirty && _cachedReachable != null && key == _cachedKey)
            {
                return _cachedReachable;
            }

            var reachable = new HashSet<string>(
                AssetDatabase.GetDependencies(allRoots.ToArray(), true));
            // ルート自身も到達扱い（GetDependencies は入力を含むが念のため）
            foreach (var r in allRoots) reachable.Add(r);

            _cachedReachable = reachable;
            _cachedKey = key;
            _dirty = false;
            return reachable;
        }

        // ルート集合の同一性キー（順序非依存）。
        private static string ComputeKey(IReadOnlyList<string> allRoots)
        {
            return string.Join("\n", allRoots.OrderBy(x => x, StringComparer.Ordinal));
        }

        /// <summary>アセット変更を検知してキャッシュを無効化する。</summary>
        private class Invalidator : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if ((imported != null && imported.Length > 0) ||
                    (deleted != null && deleted.Length > 0) ||
                    (moved != null && moved.Length > 0))
                {
                    Invalidate();
                }
            }
        }
    }
}
