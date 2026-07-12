using System.Collections.Generic;

namespace Maaaaa.Akm.Editor
{
    internal enum AssetKind
    {
        Model,
        Texture,
        Material,
        Animation,
        Other,
        Mixed,
    }

    /// <summary>退避候補1件（導入単位フォルダ）。</summary>
    internal class ScanResultEntry
    {
        /// <summary>導入単位フォルダのアセットパス（例: Assets/作者名/商品名）。単一ファイルの場合はそのパス。</summary>
        public string UnitPath;

        /// <summary>このユニット以下に含まれる全アセットファイル。</summary>
        public List<string> ContainedFiles = new List<string>();

        /// <summary>フォルダ合計サイズ（bytes、.meta 除く）。</summary>
        public long SizeBytes;

        /// <summary>代表種別。</summary>
        public AssetKind Kind;

        /// <summary>判定根拠（P-2）。</summary>
        public string Reason;

        /// <summary>UI 選択状態（既定 false = 明示選択させる、§7.3）。</summary>
        public bool Selected;
    }

    /// <summary>スキャン結果全体。</summary>
    internal class ScanResult
    {
        public List<ScanResultEntry> Candidates = new List<ScanResultEntry>();
        public int TotalUnits;
        public int UsedUnits;
        public int ProtectedUnits;
        public int RootCount;
        public List<string> Messages = new List<string>();

        public long TotalCandidateSize
        {
            get
            {
                long sum = 0;
                foreach (var e in Candidates) sum += e.SizeBytes;
                return sum;
            }
        }
    }
}
