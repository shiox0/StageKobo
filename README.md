# すてーじ工房（StageKobo）

ブラウザでアイドルのライブステージを組み立てて、そのまま **VRChat のワールド（Unity）** に持っていけるツールです。

- **Web ページ（ブラウザ版）**：ステージ・照明・LED モニター・VJ・カメラワークを作って、その場で演出を試せます
- **Unity パッケージ（インポーター）**：ブラウザ版で書き出したファイルから、Unity 上で同じステージを組み立て直します。照明の動き・リモコン（全員に同期）・VJ・曲の音への反応（AudioLink / YamaPlayer）まで

> [!NOTE]
> 開発中です。VRChat の実機での確認はこれからなので、うまく動かないところがあるかもしれません。

## 使う

| | どこ |
|---|---|
| **Web ページ** | https://shiox0.github.io/StageKobo/ |
| **Unity パッケージ** | このページ右の **Releases** → いちばん新しい版 → `StageKobo_Importer_v〇.〇.〇.unitypackage` |

Releases にある `stage-kobo.html` をダウンロードして、ブラウザにドラッグしても同じものが使えます（インターネットにつながっている必要があります）。

## 流れ

1. **Web ページでステージを作る**
   - 左のタブでステージ・背景・アセット・ライトを決めて、右のリモコンで演出を試します
   - 作ったものは自動で保存されます（同じブラウザだけ）。ほかの PC に持っていくときは「保存・書き出し」タブの「JSONで保存」
2. **書き出す**：「保存・書き出し」タブで **GLB** と **Unity用JSON** の 2 つ
3. **Unity にパッケージを入れる**：Releases の `.unitypackage` を Unity のプロジェクトにドラッグ →「Import」
4. **組み立てる**：GLB と `.unity.json` を Assets にドラッグ → メニュー **和室 → すてーじ工房 → インポーター** → 2 つを欄に入れて **組み立てる**
   - はじめての時だけ「UdonSharp 版を有効にする」を押して、再コンパイルを待ちます（リモコンが全員に同期するようになります）
5. **VRChat にアップロード**：いつも通りワールドとしてアップロード

くわしい使い方・できること・困ったときは → [Unity 側の説明（README）](unity/Assets/和室/Tools/StageKobo/README.md)

## 必要なもの

- Unity 2022.3（VRChat の Creator Companion で作ったワールドのプロジェクト。UdonSharp は最初から入っています）
- GLB を読むパッケージ：**glTFast**（インポーターの「glTFast を入れる」ボタンで入ります）
- 曲の音に反応させるとき（任意）：**AudioLink**（VCC で追加）・**YamaPlayer**

## 更新するとき

Releases の新しい `.unitypackage` を、前の版の上からそのまま入れてください（シーンや設定はそのまま）。
新しい機能を使うには、インポーターで **組み立て直し** てください。変わったことは [更新履歴](CHANGELOG.md) にあります。

## 困ったとき・要望

- このリポジトリの **Issues** に書いてください（すぐには返事できないこともあります）
- うまく動かないときは、Unity の Console に出た赤いエラーと、インポーターのログ（組み立てたときの文字）をいっしょに書いてもらえると早いです

## ライセンス

[CC0 1.0](LICENSE)（パブリックドメイン）です。ワールドに使う・改造する・配る、どれも自由で、クレジットの表記もいりません。
そのかわり、無保証です（使って起きたことの責任は持てません）。

---

<details>
<summary>このリポジトリの中身（作る人向け）</summary>

| フォルダ | 中身 |
|---|---|
| `web/stage-kobo.html` | ブラウザ版（1 ファイル）。Web ページと Releases にはこれが出ます |
| `web/src/` ・ `web/build.sh` | ブラウザ版の元のファイル。`bash web/build.sh` で `stage-kobo.html` を作り直します |
| `web/page/` | Web ページにパスワードをかけたいとき用（ふだんは使いません。[SETUP.md](SETUP.md)） |
| `unity/Assets/和室/Tools/StageKobo/` | Unity パッケージの中身（`.meta` の GUID は変えないこと） |
| `tools/` | `.unitypackage` を作る・Release の説明を作るスクリプト |
| `.github/workflows/` | 「Release を作る」「Webページを公開」（GitHub Actions） |

公開の設定と、新しい版の出し方は [SETUP.md](SETUP.md) にあります。

</details>
