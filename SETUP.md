# 公開・配布の設定（作者用）

このリポジトリを公開して、Web ページ（GitHub Pages）と Unity パッケージ（Releases）を配るための設定と、新しい版の出し方です。

| もの | どこ |
|---|---|
| Web ページ | `https://shiox0.github.io/StageKobo/`（Actions の「Webページを公開」が置く） |
| Unity パッケージ・`stage-kobo.html` | **Releases**（Actions の「Release を作る」が作る） |

## A. はじめての 1 回

### 1. ファイルを GitHub に送る（GitHub Desktop）

1. GitHub Desktop → **File → Add local repository…** → このフォルダを選ぶ → **Add repository**
2. 上の **Publish branch**（または **Push origin**）を押す

うまくいかないとき

- 「**Repository not found**」→ GitHub のサイトで **New repository** → 名前 `StageKobo`・**Public**・README などは何も付けずに **Create repository** → もう一度 2.
- 「**rejected**」「**the remote contains work**」など → GitHub 側にすでにファイル（作ったときの README など）があります。GitHub Desktop の **Repository → Open in Command Prompt** で次を入れて Enter → もう一度 **Push origin**

  ```
  git pull origin main --allow-unrelated-histories --no-edit -X ours
  ```

### 2. 公開にする

リポジトリの **Settings → General** → いちばん下の **Danger Zone → Change repository visibility → Make public**（すでに Public なら不要）

### 3. Web ページを公開する（GitHub Pages）

1. **Settings → Pages → Build and deployment → Source** を **GitHub Actions** にする
2. **Actions** タブ → 左の **Webページを公開** → 右の **Run workflow** → **Run workflow**
3. 1〜2 分で `https://shiox0.github.io/StageKobo/` が開けば完成

- このあとは、`web/stage-kobo.html` を新しくして Push すると、自動で公開し直されます
- Pages をオンにする前に Push しても、エラーにはならずに「まだオンになっていない」と出るだけです

### 4. Release を作る（Unity パッケージの配布）

1. **Actions** タブ → 左の **Release を作る** → 右の **Run workflow** → バージョンは空のまま → **Run workflow**
2. 1 分ほどで、右の **Releases** に `v0.9.0`（`StageKobo_Importer_v0.9.0.unitypackage` と `stage-kobo.html`）ができます

- Actions が動かないとき：**Settings → Actions → General** で「Allow all actions and reusable workflows」
- 同じバージョンでもう一度押すと、ファイルと説明を上書きします
- 「おためし版にする」にチェックすると Pre-release になります

## B. 新しい版を出すとき

1. このフォルダの `unity/…/StageKobo/` と `web/` を新しくする（Claude に頼むときは「GitHub 用のフォルダも更新して」）
2. バージョンを上げる：Unity 側の `README.md` の 1 行目（`インポーター v〇.〇.〇`）と、`CHANGELOG.md` のいちばん上に `## v〇.〇.〇（日付）` の段
3. GitHub Desktop で **Commit to main** → **Push origin**（Web ページは自動で更新）
4. **Actions → Release を作る → Run workflow**

## 公開する前に気をつけること

このリポジトリは、だれでも中身と変更の履歴を見られます。次のものは入れないでください（一度 Push すると、消しても履歴に残ります）。

- 自分の PC の場所（`C:\Users\…`・`E:\…` など）、本名、メールアドレス、パスワード、トークン
- 自分のステージのデータ（`.glb`・`.unity.json`。`.gitignore` で入らないようにしてあります）
- 人からもらった画像・音・モデル（使ってよいか分からないもの）

コミットの作者は GitHub の `noreply` のアドレスにしてあります（メールアドレスは出ません）。GitHub Desktop でも **File → Options → Git** の Email が `…@users.noreply.github.com` になっているか確かめてください。

## おまけ：Web ページにパスワードをかけたいとき

ふだんは不要です。あとで「見られる人を絞りたい」となったときだけ。

1. **Settings → Secrets and variables → Actions → Secrets** タブ → **New repository secret** → Name `PAGE_PASSWORD`、Secret にパスワード
2. **Actions → Webページを公開 → Run workflow**

中身を暗号化して、パスワードを入れた人だけが開けるページになります（AES-GCM＋PBKDF2。「この端末で覚えておく」付き。検索エンジンにも出さない）。
`PAGE_PASSWORD` を消して、もう一度 Run workflow すると、普通の公開に戻ります。
ただし、リポジトリが Public のままだと Releases の `stage-kobo.html` やソースはだれでも取れるので、本当に絞りたいときはリポジトリも Private にしてください（Private で Pages を使うには有料の GitHub Pro が必要）。

## 参考（GitHub の説明）

- [GitHub Pages について](https://docs.github.com/ja/pages/getting-started-with-github-pages/about-github-pages)
- [GitHub Actions で Pages を公開する](https://docs.github.com/ja/pages/getting-started-with-github-pages/using-custom-workflows-with-github-pages)
- [コミットのメールアドレスを設定する](https://docs.github.com/ja/account-and-profile/setting-up-and-managing-personal-account-settings/managing-email-preferences/setting-your-commit-email-address)
