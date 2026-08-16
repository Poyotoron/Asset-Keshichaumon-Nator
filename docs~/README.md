# ドキュメントサイトのローカルプレビュー

このフォルダは GitHub Pages で公開するドキュメント一式です。MkDocs (Material) で組んでいます。

以下はすべて **PowerShell** で、**リポジトリのルート**（`package.json` がある階層）から実行します。

---

## 毎回の手順

### 1. 仮想環境をアクティベートする

```powershell
& "$env:USERPROFILE\.venvs\vpm-docs\Scripts\Activate.ps1"
```

成功するとプロンプトの先頭に `(vpm-docs)` が付きます。

<details>
<summary>「このシステムではスクリプトの実行が無効になっているため…」と出る場合</summary>

PowerShell の実行ポリシーがスクリプトを弾いています。**そのセッションだけ**許可します（永続的な設定は変えません）。

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

を実行してから、もう一度アクティベートしてください。

アクティベートせずに済ませることもできます（この場合は以降のコマンドも `mkdocs` の代わりにフルパスを使います）。

```powershell
& "$env:USERPROFILE\.venvs\vpm-docs\Scripts\mkdocs.exe" serve -f "docs~/mkdocs.yml"
```
</details>

### 2. プレビューする

```powershell
mkdocs serve -f "docs~/mkdocs.yml"
```

`http://127.0.0.1:8000/` が表示されるので、ブラウザで開きます。**ファイルを保存すると自動で再読み込み**されるので、開いたまま編集できます。

止めるときは `Ctrl + C`。

### 3. 公開前に、CI と同じ条件で確認する

`serve` は多少の問題を見逃します。**リンク切れなどは `--strict` を付けたビルドで検出します**（GitHub Actions もこの条件でビルドします）。

```powershell
$env:AKN_VERSION = "0.3.3"
mkdocs build --strict -f "docs~/mkdocs.yml"
```

`Documentation built in ...` と出れば成功。警告が 1 つでもあれば失敗します。

`AKN_VERSION` はフッタに出る「対応バージョン」です。渡さなければ `dev` と表示されます。公開時は GitHub Actions が `package.json` の `version` を読んで渡すので、**ページ側にバージョン番号を書かないでください。**

### 4. 終わったら

```powershell
deactivate
```

---

## 目視で確認したいこと

`mkdocs serve` で開いたら、次を見てください。ビルドが通っても、ここは自動では検出できません。

| 見る場所 | 確認する内容 |
|---|---|
| 右上のテーマ切り替え | ライト／ダークの**両方**でリンク・見出しが読めるか |
| ヘッダ | クリムゾンの背景に白文字が乗って読めるか |
| 検索ボックス | 日本語（例:「退避」「保護」）で結果が出るか |
| 注記ブロック | 警告（橙）と危険（赤）と補足（青）が**色で区別できる**か |
| フッタ | 「対応バージョン: …」が出ているか |

---

## 環境の作成（初回のみ / 環境を作り直すとき）

ドキュメント用の Python 環境は **兄弟リポジトリと共用**します。リポジトリごとに作りません。

```powershell
uv venv --python 3.13 "$env:USERPROFILE\.venvs\vpm-docs"
$env:VIRTUAL_ENV = "$env:USERPROFILE\.venvs\vpm-docs"
uv pip install -r "docs~/requirements.txt"
```

- 作成先は `%USERPROFILE%\.venvs\vpm-docs`。**Unity プロジェクトの中に作らないでください**（Unity が取り込もうとします）。
- Python 3.13 を指定しているのは、GitHub Actions 側と揃えるためです。
- 他のリポジトリで使うときも、同じ環境をアクティベートするだけで済みます。依存が増えていたら `uv pip install -r "docs~/requirements.txt"` をやり直してください。

---

## 注意

- ビルド成果物 `docs~/site/` はコミットしません（`.gitignore` 済み）。
- **フォルダ名の末尾の `~` を消さないでください。** Unity は末尾が `~` のフォルダを取り込まないため、この名前のおかげで配下に `.meta` が作られません。名前を変えると `.meta` が大量に生成され、配布物にも混ざります。
- 画像は使わず、表と注記ブロックで説明します。
