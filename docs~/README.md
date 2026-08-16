# ドキュメントサイトのローカルプレビュー

このフォルダは GitHub Pages で公開するドキュメントの一式です。MkDocs (Material) で組んでいます。

## 環境（初回のみ）

ドキュメント用の Python 環境は **兄弟リポジトリと共用**します。リポジトリごとに作りません。

```powershell
uv venv --python 3.13 "$env:USERPROFILE\.venvs\vpm-docs"
$env:VIRTUAL_ENV = "$env:USERPROFILE\.venvs\vpm-docs"
uv pip install -r "docs~/requirements.txt"
```

作成先は `%USERPROFILE%\.venvs\vpm-docs` です。**Unity プロジェクトの中に置かないでください**（Unity が取り込もうとするため）。

## プレビュー

リポジトリのルートから実行します。

```powershell
& "$env:USERPROFILE\.venvs\vpm-docs\Scripts\mkdocs.exe" serve -f "docs~/mkdocs.yml"
```

環境をアクティベートしてから使う場合:

```powershell
& "$env:USERPROFILE\.venvs\vpm-docs\Scripts\Activate.ps1"
mkdocs serve -f "docs~/mkdocs.yml"
```

## 公開前の確認

CI と同じ条件（警告をエラー扱い）でビルドします。リンク切れがあればここで落ちます。

```powershell
$env:AKN_VERSION = "0.3.3"   # フッタに出る対応バージョン
mkdocs build --strict -f "docs~/mkdocs.yml"
```

`AKN_VERSION` を渡さないとフッタは `dev` と表示されます。公開時は GitHub Actions が `package.json` の `version` を読んで渡すので、**ページ側にバージョンを書かないでください。**

## 注意

- ビルド成果物 `docs~/site/` はコミットしません（`.gitignore` 済み）。
- **フォルダ名の末尾の `~` を消さないでください。** Unity は末尾が `~` のフォルダを取り込まないため、この名前のおかげで配下に `.meta` が作られません。名前を変えると `.meta` が大量に生成され、配布物にも混ざります。
- 画像は使わず、表と注記ブロックで説明します。
