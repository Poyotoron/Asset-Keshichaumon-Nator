using System.Text;
using System.Text.RegularExpressions;

namespace Maaaaa.Akm.Editor
{
    /// <summary>
    /// 単純な glob マッチャ。
    ///   *  … スラッシュを跨がない任意文字列
    ///   ** … スラッシュを跨ぐ任意文字列
    ///   ?  … 任意の1文字（スラッシュを除く）
    /// マッチはパス全体に対する完全一致（大文字小文字は区別しない）。
    /// </summary>
    internal static class GlobMatcher
    {
        public static bool IsMatch(string path, string glob)
        {
            if (string.IsNullOrEmpty(glob)) return false;
            var regex = ToRegex(glob);
            return regex.IsMatch(AkmUtil.Normalize(path));
        }

        private static Regex ToRegex(string glob)
        {
            glob = AkmUtil.Normalize(glob);
            var sb = new StringBuilder();
            sb.Append('^');
            for (int i = 0; i < glob.Length; i++)
            {
                char c = glob[i];
                if (c == '*')
                {
                    bool doubleStar = i + 1 < glob.Length && glob[i + 1] == '*';
                    if (doubleStar)
                    {
                        sb.Append(".*");
                        i++;
                    }
                    else
                    {
                        sb.Append("[^/]*");
                    }
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                }
                else
                {
                    sb.Append(Regex.Escape(c.ToString()));
                }
            }
            sb.Append('$');
            return new Regex(sb.ToString(), RegexOptions.IgnoreCase);
        }
    }
}
