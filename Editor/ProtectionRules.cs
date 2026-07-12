using System.Collections.Generic;
using System.Linq;

namespace Maaaaa.Akm.Editor
{
    /// <summary>
    /// 保護集合（要件 §4）。ツール・シェーダー・コード資産を退避対象から除外する。
    /// 設計原則 P-4「疑わしきは保護する」に従い、迷う場合は保護する。
    /// </summary>
    internal static class ProtectionRules
    {
        // ---- F-PROT-01: 構造ヒューリスティック（この拡張子を含むフォルダは丸ごと保護）----
        // .cs / .asmdef / .asmref / .dll / シェーダー系 / パッケージ定義
        public static readonly string[] StructureProtectExtensions =
        {
            ".cs", ".asmdef", ".asmref", ".dll",
            ".shader", ".hlsl", ".cginc", ".shadergraph", ".shadersubgraph",
        };

        // package.json / *.vpm.json を含むフォルダも保護
        public static readonly string[] StructureProtectFileNames =
        {
            "package.json",
        };

        // ---- F-PROT-04: 常に保護する拡張子（参照解析対象外）----
        public static readonly string[] AlwaysProtectedExtensions =
        {
            ".cs", ".asmdef", ".asmref", ".dll",
            ".shader", ".hlsl", ".cginc",
            ".json", ".txt", ".md", ".xml", ".yml", ".gitignore",
        };

        // ---- F-PROT-02: 既知ツール名デフォルト保護リスト（パスに含まれれば保護）----
        // 大文字小文字を区別せず部分一致で判定する。
        public static readonly string[] DefaultToolNames =
        {
            "VRCSDK", "VRChat SDK", "com.vrchat",
            "lilToon", "Poiyomi", "Silent", "UTS2", "UnityChanToonShader",
            "Modular Avatar", "nadena.dev.modular-avatar",
            "NDMF", "nadena.dev.ndmf",
            "Avatar Optimizer", "com.anatawa12.avatar-optimizer",
            "VRCFury",
            "Dynamic Bone", "DynamicBone",
            "FinalIK",
            "TextMesh Pro", "TextMeshPro",
            // 慣例フォルダ
            "/Plugins/", "/Gizmos/", "/Editor/",
        };

        /// <summary>ファイル単位で常に保護される拡張子か（F-PROT-04）。</summary>
        public static bool IsAlwaysProtectedFile(string assetPath)
        {
            return AlwaysProtectedExtensions.Contains(AkmUtil.Ext(assetPath));
        }

        /// <summary>
        /// 導入単位フォルダを保護すべきか判定する。
        /// </summary>
        /// <param name="unitPath">導入単位フォルダのアセットパス。</param>
        /// <param name="containedFiles">そのフォルダ以下に含まれる全アセットファイル。</param>
        /// <param name="whitelistGlobs">ユーザーホワイトリスト（glob）。</param>
        /// <param name="reason">保護理由（保護される場合のみ）。</param>
        public static bool IsProtectedUnit(
            string unitPath,
            IReadOnlyList<string> containedFiles,
            IReadOnlyList<string> whitelistGlobs,
            out string reason)
        {
            // 本ツール自身の設定フォルダは常に保護（自己言及の回避）
            if (unitPath == AkmSettings.SettingsFolder ||
                unitPath.StartsWith(AkmSettings.SettingsFolder + "/"))
            {
                reason = "本ツールの設定フォルダ";
                return true;
            }

            // F-PROT-03: ユーザーホワイトリスト（glob）
            if (whitelistGlobs != null)
            {
                foreach (var glob in whitelistGlobs)
                {
                    if (string.IsNullOrWhiteSpace(glob)) continue;
                    if (GlobMatcher.IsMatch(unitPath, glob) ||
                        containedFiles.Any(f => GlobMatcher.IsMatch(f, glob)))
                    {
                        reason = $"ホワイトリスト: {glob}";
                        return true;
                    }
                }
            }

            // F-PROT-02: 既知ツール名
            var lowerUnit = unitPath.ToLowerInvariant();
            foreach (var name in DefaultToolNames)
            {
                if (lowerUnit.Contains(name.ToLowerInvariant()))
                {
                    reason = $"既知ツール名／慣例フォルダ: {name}";
                    return true;
                }
            }

            // F-PROT-01: 構造ヒューリスティック（コード・シェーダー・パッケージを含む）
            foreach (var file in containedFiles)
            {
                var ext = AkmUtil.Ext(file);
                if (StructureProtectExtensions.Contains(ext))
                {
                    reason = $"コード／シェーダー資産を含む（{ext}）";
                    return true;
                }
                var fileName = System.IO.Path.GetFileName(file).ToLowerInvariant();
                if (StructureProtectFileNames.Contains(fileName) || fileName.EndsWith(".vpm.json"))
                {
                    reason = $"パッケージ定義を含む（{fileName}）";
                    return true;
                }
            }

            // Editor/ ディレクトリを持つ（エディタ拡張の慣例配置）
            if (containedFiles.Any(f => f.Contains("/Editor/") || f.EndsWith("/Editor")))
            {
                reason = "Editor/ ディレクトリを持つ";
                return true;
            }

            reason = null;
            return false;
        }
    }
}
