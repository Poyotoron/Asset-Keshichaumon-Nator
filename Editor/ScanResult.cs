using System.Collections.Generic;

namespace Maaaaa.Akn.Editor
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

        /// <summary>種別の内訳（例: "Model 1, Material 3, Texture 12"）。ツールチップ表示用。</summary>
        public string KindDetail;

        /// <summary>判定根拠（なぜ未使用と判断したか。UI に表示する）。</summary>
        public string Reason;

        /// <summary>UI 選択状態（既定 false = 明示選択させる）。</summary>
        public bool Selected;

        /// <summary>まとめて退避すべき候補のグループ番号。関係が無い候補は単独グループになる。</summary>
        public int GroupId;

        /// <summary>この候補を参照している他の候補の単位パス（表示用）。</summary>
        public List<string> ReferencedByUnits = new List<string>();
    }

    /// <summary>保護されて候補から除外された単位 1 件。</summary>
    internal class ProtectedUnitEntry
    {
        /// <summary>導入単位フォルダのアセットパス。</summary>
        public string UnitPath;
        /// <summary>保護理由。</summary>
        public string Reason;
        /// <summary>この単位で保護されたファイル数。</summary>
        public int FileCount;
        /// <summary>この単位で保護されたファイルの合計サイズ。</summary>
        public long SizeBytes;
    }

    /// <summary>アバター系ルートからは到達せず、暗黙ルートだけで使用中になっている単位。</summary>
    internal class ImplicitOnlyUsedEntry
    {
        public string UnitPath;
        public List<string> ContainedFiles = new List<string>();
        public int FileCount;
        public long SizeBytes;
        public List<string> PinnedByImplicitRoots = new List<string>();
    }

    /// <summary>スキャン結果全体。</summary>
    internal class ScanResult
    {
        public List<ScanResultEntry> Candidates = new List<ScanResultEntry>();
        public int TotalUnits;
        public int UsedUnits;
        /// <summary>保護されて除外された数。TotalUnits / UsedUnits と同じ粒度で数える。</summary>
        public int ProtectedCount;
        public List<ProtectedUnitEntry> ProtectedEntries = new List<ProtectedUnitEntry>();
        public int ProtectedUnits => ProtectedEntries.Count;
        public List<ImplicitOnlyUsedEntry> ImplicitOnlyUsedEntries = new List<ImplicitOnlyUsedEntry>();
        public bool ImplicitRootAttributionSkipped;
        /// <summary>スキャン範囲の外だったため、判定せずに除外した単位の数。</summary>
        public int OutOfScopeUnits;
        public int RootCount;
        /// <summary>このスキャンで適用された範囲（空ならプロジェクト全体）。結果表示用。</summary>
        public List<string> ScopeDirectories = new List<string>();
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
