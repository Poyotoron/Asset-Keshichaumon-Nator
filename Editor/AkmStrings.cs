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
        public const string TabDuplicates = "重複検出 (Duplicates)";
        public const string TabCache = "キャッシュ掃除 (Cache)";

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
            "判定に迷う場合は、誤って消してしまわないよう保護側に倒します。";
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
        public const string ProgressMeasureCache = "キャッシュサイズを計測しています…";
        public const string ProgressHashing = "ファイルのハッシュを計算しています…";
        public const string ProgressExportPackage = ".unitypackage をエクスポートしています…";

        public const string Ok = "OK";
        public const string Cancel = "キャンセル";

        // ---- 退避オプション ----
        public const string RelocateExportPackageToggle = "退避前に .unitypackage としてバックアップする";
        public const string RelocateExportPackageHelp =
            "退避対象を .unitypackage として書き出してから退避します。復元とは別の、もう一段の安全網です。\n" +
            "書き出し先はプロジェクトルート直下の退避フォルダ内（_UnusedAssets_…/backup.unitypackage）です。";
        public const string RelocateExportedFormat = "\n.unitypackage を書き出しました:\n{0}";

        // ---- 完全削除 ----
        public const string PurgeHeader = "退避フォルダの完全削除（取り消し不可）";
        public const string PurgeHelp =
            "退避フォルダ（_UnusedAssets_…）を完全に削除します。この操作は取り消せません。\n" +
            "削除するとアセットは失われ、復元（Restore）できなくなります。本当に不要と確認できたフォルダにのみ使用してください。";
        public const string PurgeSelectButton = "退避フォルダを選んで完全削除";
        public const string PurgeConfirmStage1Title = "完全削除の確認（1/2）";
        public const string PurgeConfirmStage1Format =
            "次の退避フォルダを完全に削除します。\n{0}\n\n" +
            "対象: {1} 件 / {2}\n\n" +
            "これは取り消せません。復元できなくなります。本当に続行しますか？";
        public const string PurgeConfirmStage2Title = "完全削除の最終確認（2/2）";
        public const string PurgeConfirmStage2 =
            "最終確認です。このフォルダとその中身を完全に削除します。\n復元はできません。実行しますか？";
        public const string PurgeConfirmProceed = "完全に削除する";
        public const string PurgeDoneFormat = "退避フォルダを完全に削除しました:\n{0}";
        public const string PurgeNotTrashFolder =
            "選択したフォルダは本ツールの退避フォルダ（.akm-relocation.json を含む）ではありません。\n" +
            "安全のため、退避フォルダ以外は完全削除できません。";
        public const string PurgeFailedFormat = "削除に失敗しました: {0}";

        // ---- ファイル単位モード ----
        public const string ScanFileUnitToggle = "ファイル単位モード（上級者向け）";
        public const string ScanFileUnitHelp =
            "フォルダ集約を無効化し、アセットを1ファイルずつ列挙します。\n" +
            "「使用中の衣装フォルダから未使用テクスチャだけ退避」といった細かい操作ができますが、\n" +
            "フォルダ単位判定が持つ安全性（1商品=1フォルダの保護）が弱まります。既定は OFF のままを推奨します。";

        // ---- 重複検出（SHA-256） ----
        public const string DupHeader = "重複アセット検出（内容が同一のファイル）";
        public const string DupHelp =
            "Assets/ 配下を SHA-256 で照合し、内容が完全に一致するファイル群を検出します。\n" +
            "共通基盤（シェーダー・テクスチャ等）を複数の配布物が同梱している場合などに見つかります。\n" +
            "これは検出（レポート）のみで、自動では何も移動・削除しません。参照されている重複を消すと壊れるため、\n" +
            "内容を確認したうえで手動で判断してください。";
        public const string DupScanButton = "重複を検出（非破壊）";
        public const string DupNotRunYet = "まだ検出していません。「重複を検出」を押してください。";
        public const string DupEmpty = "内容が重複するファイルは見つかりませんでした。";
        public const string DupSummaryFormat =
            "重複グループ: {0} 件   （重複により余分に消費している容量の目安: {1}）";
        public const string DupGroupHeaderFormat = "同一内容 {0} ファイル / 各 {1}   （余剰 {2}）";
        public const string DupColUsed = "使用";
        public const string DupUsedYes = "使用中";
        public const string DupUsedNo = "未参照";

        // ---- キャッシュ掃除 ----
        public const string CacheHeader = "ビルドキャッシュ（Library / Temp）の掃除";
        public const string CacheWindowsOnly =
            "この機能は現在 Windows のみ対応です。この環境では利用できません。";
        public const string CacheIntro =
            "Assets/ からアセットを退避しても Library/ 配下のキャッシュは縮みません。ここを全削除すると\n" +
            "ディスク容量を大きく取り戻せる場合があります。ただし次回 Unity 起動時に全アセットの再インポートが\n" +
            "発生します。";
        // 過小表現を避け、目立つ警告として出す
        public const string CacheWarnMain =
            "⚠ 次回 Unity 起動時に、全アセットの再インポートが発生します。\n" +
            "プロジェクト規模により 数分〜数十分（それ以上になることもあります）かかります。\n" +
            "失うのはデータではなく「時間」です。「一瞬で終わる」「安全」といった軽い操作ではありません。";
        // 見積りの根拠を数値で示す
        public const string CacheStatsFormat =
            "あなたのプロジェクトの規模:\n" +
            "・現在の Library/ サイズ: {0}\n" +
            "・Assets/ のアセット数: {1} 個 / 合計 {2}";
        // 所要時間の目安（環境差が大きいので断定しない）
        public const string CacheTimeEstimateGeneric =
            "所要時間の目安: 環境（CPU / ストレージ / アセット構成）で大きく変わるため断定できません。\n" +
            "SSD でも数分、規模が大きい・HDD の場合は数十分以上かかることがあります。";
        public const string CacheTimeEstimateMeasuredFormat =
            "前回このプロジェクトでは、掃除後の起動に約 {0} かかりました（実測値）。";
        // 実行タイミングの助言
        public const string CacheTimingAdvice =
            "推奨タイミング: 作業終了時、離席前、就寝前など、しばらく Unity を使わないときに実行してください。";
        // 削除対象（全件を列挙して見せる）
        public const string CacheTargetsHeader = "削除対象（全件）";
        public const string CacheMeasureButton = "サイズを計測 / 更新";
        public const string CacheReserveButton = "次回 Unity 終了時にキャッシュを掃除する（予約）…";
        public const string CacheReservedNoteFormat =
            "掃除が予約されています。次回 Unity を終了すると、ヘルパーがキャッシュを削除します。\n予約日時: {0}";
        public const string CacheCancelReserveButton = "予約をキャンセルする";
        public const string CacheCleanNowButton = "今すぐ Unity を終了して掃除する…";
        public const string CacheFallbackButton = "clean-cache.bat を生成（手動実行用フォールバック）";
        public const string CacheIncludeLogsToggle = "Logs/ も削除する（既定 OFF: 不具合調査に使う可能性があるため）";
        public const string CacheNextButton = "次へ（最終確認）";
        public const string CacheTargetsCsprojNote = "・*.csproj / *.sln （再生成されます）";
        public const string CacheFallbackDoneFormat =
            "clean-cache.bat をプロジェクトルートに生成しました:\n{0}\n\n" +
            "Unity を完全に終了してから、このバッチをダブルクリックで実行してください。";

        // 予約ダイアログ（第1段階 / 第2段階は専用ウィンドウ）
        public const string CacheConfirmStage2Title = "キャッシュ掃除の最終確認";
        public const string CacheAckCheckbox = "再インポートに時間がかかることを理解しました";
        public const string CacheReserveConfirmButton = "予約する";
        public const string CacheCleanNowConfirmButton = "今すぐ終了して掃除する";
        public const string CacheReservedDialogFormat =
            "キャッシュ掃除を予約しました。\n次回 Unity を終了したときに、外部ヘルパーが Library/ 等を削除します。\n\n{0}";

        // 終了時の再警告
        public const string CacheQuitWarnTitle = "キャッシュ掃除が予約されています";
        public const string CacheQuitWarnMessage =
            "キャッシュ掃除が予約されています。\n" +
            "このまま Unity を終了すると外部ヘルパーが起動し、次回起動時に長時間の再インポートが発生します。\n\n" +
            "どうしますか？";
        public const string CacheQuitProceed = "掃除して終了";
        public const string CacheQuitCancelReservation = "予約を取り消して終了";
        public const string CacheQuitAbort = "終了しない";

        // 起動時の再インポート通知
        public const string CacheJustCleanedTitle = "キャッシュを掃除しました";
        public const string CacheJustCleanedMessage =
            "前回終了時にビルドキャッシュを削除しました。\n" +
            "この起動では全アセットの再インポートが実行されています（または直前に完了しました）。\n" +
            "「フリーズした」と誤解して強制終了しないでください。完了までお待ちください。";
        public const string CacheJustCleanedMeasuredFormat =
            "\n\n今回の掃除後の起動には約 {0} かかりました。";

        // 起動時検証: 予約が残っている（前回掃除が完了しなかった）
        public const string CacheLeftoverTitle = "前回のキャッシュ掃除が完了しませんでした";
        public const string CacheLeftoverMessage =
            "キャッシュ掃除の予約が残っています。前回の掃除が実行されなかった可能性があります\n" +
            "（Unity のクラッシュ・強制終了時など）。害はありません（掃除されなかっただけです）。\n\n" +
            "「キャッシュ掃除」タブから再度実行できます。予約は解除しました。";
    }
}
