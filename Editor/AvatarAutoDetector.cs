using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Maaaaa.Akm.Editor
{
    /// <summary>
    /// VRCAvatarDescriptor を持つ Prefab を検索する（F-ROOT-02）。
    ///
    /// VRChat SDK への「コンパイル時」の依存を避けるため、型はリフレクションで解決する。
    /// これにより asmdef に SDK 参照が無くてもコンパイルが通り、SDK 未導入環境でも壊れない。
    /// （SDK が存在する場合のみ自動検出が機能する。）
    /// </summary>
    internal static class AvatarAutoDetector
    {
        private const string DescriptorTypeName = "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor";

        private static Type ResolveDescriptorType()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(DescriptorTypeName, false);
                    if (t != null) return t;
                }
                catch
                {
                    // 一部アセンブリは型解決で例外を投げることがある。無視して続行。
                }
            }
            return null;
        }

        /// <summary>SDK（VRCAvatarDescriptor 型）が利用可能か。</summary>
        public static bool IsSdkAvailable => ResolveDescriptorType() != null;

        /// <summary>
        /// VRCAvatarDescriptor を持つ Prefab のアセットパス一覧を返す。
        /// SDK が無い場合は空リスト。
        /// </summary>
        public static List<string> FindAvatarPrefabs()
        {
            var results = new List<string>();
            var descriptorType = ResolveDescriptorType();
            if (descriptorType == null) return results;

            var guids = AssetDatabase.FindAssets("t:Prefab");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/")) continue;

                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                // includeInactive=true で子まで含めて探す
                var comp = go.GetComponentInChildren(descriptorType, true);
                if (comp != null)
                {
                    results.Add(path);
                }
            }
            return results;
        }
    }
}
