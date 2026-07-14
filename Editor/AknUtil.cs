using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Maaaaa.Akn.Editor
{
    /// <summary>汎用ユーティリティ。パス変換・サイズ計算など。</summary>
    internal static class AknUtil
    {
        /// <summary>プロジェクトルート（Assets の親）の絶対パス。区切りは '/'。</summary>
        public static string ProjectRoot
        {
            get
            {
                // Application.dataPath = ".../<Project>/Assets"
                var root = Path.GetDirectoryName(Application.dataPath);
                return Normalize(root);
            }
        }

        /// <summary>パス区切りを '/' に統一する。</summary>
        public static string Normalize(string path)
        {
            return string.IsNullOrEmpty(path) ? path : path.Replace('\\', '/');
        }

        /// <summary>"Assets/Foo/Bar" のようなアセットパスを絶対パスへ変換する。</summary>
        public static string ToAbsolute(string assetPath)
        {
            return Normalize(Path.Combine(ProjectRoot, assetPath));
        }

        /// <summary>絶対パスをプロジェクトルート基準のアセットパスへ変換する。</summary>
        public static string ToAssetPath(string absolutePath)
        {
            absolutePath = Normalize(absolutePath);
            var projectRoot = ProjectRoot;
            return absolutePath.StartsWith(projectRoot + "/")
                ? absolutePath.Substring(projectRoot.Length + 1)
                : null;
        }

        /// <summary>退避日時を読みやすい表記にする。</summary>
        public static string HumanDateTime(string value)
        {
            if (DateTime.TryParseExact(value, "yyyyMMdd_HHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var dateTime))
            {
                return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
            }
            return value;
        }

        /// <summary>人間可読なサイズ表記。</summary>
        public static string HumanSize(long bytes)
        {
            if (bytes < 0) return "-";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024.0 && i < units.Length - 1)
            {
                v /= 1024.0;
                i++;
            }
            return i == 0 ? $"{(long)v} {units[i]}" : $"{v:0.##} {units[i]}";
        }

        /// <summary>人間可読な所要時間表記（秒 → 「約 X 分 Y 秒」等）。</summary>
        public static string HumanDuration(double seconds)
        {
            if (seconds < 0) return "-";
            if (seconds < 60) return $"{seconds:0} 秒";
            int totalSec = (int)System.Math.Round(seconds);
            int min = totalSec / 60;
            int sec = totalSec % 60;
            if (min < 60) return sec == 0 ? $"{min} 分" : $"{min} 分 {sec} 秒";
            int hour = min / 60;
            min %= 60;
            return min == 0 ? $"{hour} 時間" : $"{hour} 時間 {min} 分";
        }

        /// <summary>ディレクトリ配下の合計バイト数（再帰）。存在しなければ 0。</summary>
        public static long DirectorySize(string absDir)
        {
            try
            {
                if (!Directory.Exists(absDir)) return 0;
                long sum = 0;
                foreach (var f in Directory.EnumerateFiles(absDir, "*", SearchOption.AllDirectories))
                {
                    try { sum += new FileInfo(f).Length; }
                    catch { /* アクセス不可 / ロック中は無視 */ }
                }
                return sum;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>アセットファイル1つの実サイズ（.meta は含めない）。</summary>
        public static long FileSize(string assetPath)
        {
            try
            {
                var abs = ToAbsolute(assetPath);
                var fi = new FileInfo(abs);
                return fi.Exists ? fi.Length : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>アセットの拡張子を小文字で返す（先頭ドット付き）。</summary>
        public static string Ext(string assetPath)
        {
            var e = Path.GetExtension(assetPath);
            return string.IsNullOrEmpty(e) ? string.Empty : e.ToLowerInvariant();
        }

        /// <summary>フォルダかどうか（.meta を除いた実体で判定）。</summary>
        public static bool IsFolder(string assetPath)
        {
            return AssetDatabase.IsValidFolder(assetPath);
        }

        public static AssetKind ClassifyKind(string assetPath)
        {
            switch (Ext(assetPath))
            {
                case ".fbx":
                case ".obj":
                case ".blend":
                case ".dae":
                case ".max":
                    return AssetKind.Model;
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd":
                case ".tif":
                case ".tiff":
                case ".exr":
                case ".gif":
                case ".bmp":
                    return AssetKind.Texture;
                case ".mat":
                    return AssetKind.Material;
                case ".anim":
                case ".controller":
                case ".overridecontroller":
                    return AssetKind.Animation;
                default:
                    return AssetKind.Other;
            }
        }

        public static string KindLabel(AssetKind kind)
        {
            switch (kind)
            {
                case AssetKind.Model: return "Model";
                case AssetKind.Texture: return "Texture";
                case AssetKind.Material: return "Material";
                case AssetKind.Animation: return "Animation";
                case AssetKind.Mixed: return "混在";
                default: return "その他";
            }
        }
    }
}
