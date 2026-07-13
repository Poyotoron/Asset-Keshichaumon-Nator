namespace Maaaaa.Akm.Editor
{
    /// <summary>
    /// ユーザーに見える日本語文字列を一箇所に集約する（将来の多言語化のため）。
    /// コーディング規約: 文字列はここに集める。
    /// </summary>
    internal static class AkmStrings
    {
        // ---- ツール識別 ----
        public const string ToolName = "いらないアセット消しちゃうもんネーター";
        public const string MenuPath = "Tools/いらないアセット消しちゃうもんネーター";
        public const string WindowTitle = "消しちゃうもんネーター";

        // ---- タブ ----
        public const string TabRoots = "ルート (Roots)";
        public const string TabProtection = "保護 (Protection)";
        public const string TabScan = "スキャン結果 (Scan Result)";

        // ---- Roots タブ ----
        public const string RootsHeader = "アバタールートディレクトリ";
        public const string RootsHelp =
            "アバターの .prefab / .unity を含むディレクトリを登録してください。\n" +
            "登録したフォルダを再帰的に走査し、見つかった Prefab / Scene を「使用の起点（ルート）」として扱います。\n" +
            "ここを取りこぼすと、使用中のアセットを誤って未使用と判定します。";
        public const string RootsDropArea = "ここにフォルダをドラッグ＆ドロップ";
        public const string RootsSelectFolder = "フォルダを選択して追加";
        public const string RootsAddIndividualHeader = "個別ファイルをルートに追加（任意）";
        public const string RootsAddIndividualHelp =
            "ディレクトリ単位ではなく、単一の Prefab / Scene をルートに追加できます。";
        public const string RootsAutoDetectHeader = "自動検出されたアバター (VRCAvatarDescriptor)";
        public const string RootsAutoDetectButton = "アバターを自動検出";
        public const string RootsAutoDetectNone = "自動検出されたアバターはありません（または VRChat SDK 未導入）。";
        public const string RootsAutoDetectExcludeHint = "チェックを外すとルートから除外されます。";
        public const string RemoveButton = "削除";

        // ---- Protection タブ ----
        public const string ProtHeader = "保護ルール";
        public const string ProtHelp =
            "以下に該当するフォルダ／ファイルは、参照されていなくても退避対象から除外されます。\n" +
            "設計原則 P-4「疑わしきは保護する」に基づき、迷う場合は保護側に倒します。";
        public const string ProtStructureHeader = "構造ヒューリスティック保護（自動）";
        public const string ProtToolListHeader = "既知ツール名リスト（デフォルト保護）";
        public const string ProtExtHeader = "常に保護する拡張子";
        public const string ProtWhitelistHeader = "ユーザーホワイトリスト（glob）";
        public const string ProtWhitelistHelp =
            "例: Assets/MyTools/**  ——  マッチしたパスは常に保護されます。";
        public const string ProtWhitelistAdd = "追加";
        public const string ProtWhitelistPlaceholder = "Assets/…/**";

        // ---- Scan タブ ----
        public const string ScanButton = "スキャン実行（非破壊）";
        public const string ScanNoRootsError =
            "ルート（アバター Prefab / Scene）が1つも見つかりません。\n" +
            "このままスキャンすると全アセットが削除候補になり非常に危険なため、実行をブロックしました。\n" +
            "「ルート」タブでアバターのフォルダを登録するか、自動検出を実行してください。";
        public const string ScanColSelect = "選択";
        public const string ScanColPath = "パス";
        public const string ScanColSize = "サイズ";
        public const string ScanColType = "種別";
        public const string ScanColReason = "判定根拠";
        public const string ScanColProtect = "保護";
        public const string ReasonUnreachable = "どのルートからも到達不能";
        public const string ScanProtectButton = "保護に追加";
        public const string ScanEmpty = "退避候補は見つかりませんでした。";
        public const string ScanNotRunYet = "まだスキャンしていません。「スキャン実行」を押してください。";
        public const string ScanSelectAll = "全選択";
        public const string ScanSelectNone = "全解除";
        public const string ScanSummaryFormat =
            "退避候補: {0} 件 / 合計 {1}   （使用中: {2} / 保護: {3} / ルート: {4} 件）";
        public const string ScanSelectedSummaryFormat = "選択中: {0} 件 / {1}";

        // ---- 退避 / 復元 ----
        public const string RelocateButton = "選択項目を退避（Move to Trash Folder）";
        public const string RelocateConfirmTitle = "退避の確認";
        public const string RelocateConfirmFormat =
            "{0} 個のフォルダ / {1} をプロジェクトルート直下の退避フォルダへ移動します。\n\n" +
            "これは削除ではありません。退避フォルダから Restore で元に戻せます。\n" +
            "移動後、Console に Missing 参照の警告が出ていないか確認してください。\n\n" +
            "続行しますか？";
        public const string RelocateConfirmOk = "退避する";
        public const string RelocateConfirmCancel = "キャンセル";
        public const string RelocateNothingSelected = "退避対象が選択されていません。";
        public const string RelocateDoneFormat =
            "{0} 件を退避しました。\n退避先: {1}\n\n" +
            "・Console に Missing 参照の警告が出ていないか確認してください。\n" +
            "・問題があれば「復元」から元に戻せます。";
        public const string RelocateDoneTitle = "退避完了";

        public const string RestoreHeader = "退避したアセットの復元";
        public const string RestoreHelp =
            "過去に退避したフォルダ（プロジェクトルート直下の _UnusedAssets_… ）を選択すると、元の場所へ戻します。";
        public const string RestoreSelectButton = "退避フォルダを選んで復元";
        public const string RestoreConfirmTitle = "復元の確認";
        public const string RestoreConfirmFormat =
            "{0} 件を元の場所（Assets/ 配下）へ戻します。続行しますか？";
        public const string RestoreDoneFormat = "{0} 件を復元しました。";
        public const string RestoreFolderRemoved = "\n退避フォルダが空になったため削除しました。";
        public const string RestoreFolderKept =
            "\n一部が戻せなかったため、退避フォルダは残しています（残りの内容を確認してください）。";
        public const string RestoreInvalidFolder =
            "選択したフォルダに退避マッピング（.akm-relocation.json）が見つかりません。";

        // ---- 進捗 ----
        public const string ProgressTitle = "スキャン中…";
        public const string ProgressCollectRoots = "ルート集合を収集しています…";
        public const string ProgressBuildReachable = "依存グラフを構築しています…";
        public const string ProgressEnumerate = "アセットを列挙しています…";
        public const string ProgressClassify = "判定しています…";
        public const string ProgressRelocate = "退避しています…";
        public const string ProgressRestore = "復元しています…";

        public const string Ok = "OK";
    }
}
