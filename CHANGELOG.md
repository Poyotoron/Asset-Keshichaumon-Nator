# Changelog

このプロジェクトの変更履歴。バージョニングは [Semantic Versioning](https://semver.org/lang/ja/) に従う。

## [0.1.0] - Unreleased

### Added
- 初回リリース（Phase 1 / MVP）。
- アバターから参照されていない未使用アセットの検出（Mark & Sweep）。
- ルート集合の指定: フォルダ登録・個別ファイル・`VRCAvatarDescriptor` 自動検出・暗黙ルート（Resources 等）。
- 保護ルール: 構造ヒューリスティック・既知ツール名・拡張子・ユーザーホワイトリスト（glob）。
- 導入単位フォルダ判定（固定深度／自動推定）。
- 退避（`.meta` 同伴・プロジェクトルート直下へ移動）と復元。
- 3タブ構成の Editor ウィンドウ、確認ダイアログ、進捗表示。
