# ドキュメントサイトのローカルプレビュー

PowerShell でリポジトリのルートから次を実行します。

```powershell
pip install -r "docs~/requirements.txt"
mkdocs serve -f "docs~/mkdocs.yml"
```

ビルドで生成される `docs~/site/` はコミットしないでください。

`docs~/` の末尾の `~` は消さないでください。消すと Unity が配下に `.meta` ファイルを作り始めます。
