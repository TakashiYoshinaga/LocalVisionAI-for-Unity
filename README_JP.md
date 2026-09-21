# LocalVisionAI for Unity

*[English README](README.md)*

Android端末のカメラ画像をGemmaで解析する、**完全オフライン**のUnityサンプルです。推論にはGoogleの[LiteRT-LM](https://github.com/google-ai-edge/LiteRT-LM)を使用し、モデルはAPKに同梱します。撮影した画像やプロンプトが端末の外へ送信されることはありません。

[![LocalVisionAI OCRデモ](Documents/Materials/YouTubeThumbnail_OfflineVisionAI.png)](https://www.linkedin.com/posts/tks-yoshinaga_localllms-ocr-computervision-activity-7506653758345560064-FxHq)

[YouTubeでデモ動画を見る](https://www.youtube.com/watch?v=gVoTzhzCqSQ)

## 概要

| | 内容 |
|---|---|
| 主な機能 | カメラ画像の説明、対象を指定した文字・数値の抽出、テキストでの一問一答 |
| 動作端末 | Android ARM64（最小APIレベル29） |
| Unity | 6000.3.7f1 / IL2CPP |
| Editor実行 | macOS / Windows |
| モデル | Gemma 4 E2B（約2.6GB、別途ダウンロード） |
| 必要な空き容量 | 6〜8GB程度 |

> Android固有のKotlinブリッジとGradle設定を使用するため、iOSやデスクトップ向けにはビルドできません。macOS / WindowsのUnity Editorでは開発用に直接実行できます。

### 2つのサンプルプロジェクト

| プロジェクト | カメラ | 適した用途 |
|---|---|---|
| `ARFoundationApp` | AR Foundation / ARCore | 将来AR機能を追加したい場合 |
| `SimpleMobileApp` | `WebCamTexture` | 通常のカメラアプリとして使う場合 |

`ARFoundationApp`は、これからARアプリを開発するための最低限のセットアップだけを済ませた構成です。現時点では、オブジェクト配置、アンカー、平面認識などのAR機能は実装していません。画像サンプルの実行にはARCore対応端末が必要です。

### 収録シーン

| シーン | 入力 |
|---|---|
| `0-VisionAI-SystemPromptOnly` | カメラ画像 + Inspectorで設定した固定プロンプト |
| `1-VisionAI-UserPrompt` | カメラ画像 + 画面から入力するプロンプト |
| `2-VisionAI-TextPrompt` | テキストのみ（一問一答、会話履歴なし） |

推論時間は端末性能により数秒〜数十秒です。モデルの読み込みだけで2.5GB以上のメモリを使うため、RAMに余裕のある端末を推奨します。

## このサンプルを作った理由

ARアプリでOCRを使うとき、動いている対象を固定のROI（読み取り範囲）へ収め続けるのは簡単ではありません。対象が水平とは限らず、ユーザーが理想的な角度や位置から撮影できるとも限らないためです。この問題を避けるために撮影条件を厳しくすると、アプリの使いやすさも損なわれます。

そこでこのサンプルでは、固定ROIの代わりに、読み取りたい対象を自然言語で指定します。「右側のモニターに表示された文字と数値だけを抽出する」「スマートフォンを傾けて撮影した画像を読み取る」といったケースでも、多少の取りこぼしはあるものの、多くの場合に正しく動作しました。設定例は[指定した対象の文字と数値を抽出する](#指定した対象の文字と数値を抽出する)を参照してください。

## セットアップ

### 1. モデルを配置する

[litert-community/gemma-4-E2B-it-litert-lm](https://huggingface.co/litert-community/gemma-4-E2B-it-litert-lm)から`gemma-4-E2B-it.litertlm`をダウンロードし、使用するプロジェクトへ配置します。

```text
ARFoundationApp/LocalModels/gemma-4-E2B-it.litertlm
SimpleMobileApp/LocalModels/gemma-4-E2B-it.litertlm
```

`.litertlm`ファイルはGit管理外です。E4Bなど別のモデルを使う場合は、`Packages/LiteRtLmUnity/Runtime/BundledModelPaths.cs`の`FileName`も変更してください。

> **`gemma-4-E2B-it-gpu.litertlm`は画像入力に使えません。**
> このファイルはテキスト専用で、画像を渡すと`TF_LITE_VISION_ENCODER not found`で失敗します。

<details>
<summary>モデルが画像入力に対応しているか確認する</summary>

```bash
LC_ALL=C grep -a -o -E "tf_lite_[a-z_]+" <モデル> | sort -u
```

`tf_lite_vision_encoder`が出力されれば画像入力に対応しています。

</details>

### 2. シーンを選んでビルドする

1. `ARFoundationApp`または`SimpleMobileApp`をUnityで開きます。
2. 上の表から使用するシーンを開き、Build Settingsへ追加します。
3. Android向けにビルドします。

モデルはビルド時に1GiB単位へ分割され、`StreamingAssets`へ自動配置されます。生成されるAPKは約2.6GBです。

### 3. 実行する

初回起動時だけ、モデルの結合・展開とSHA-256検証に約1分かかります。`AI Ready`と表示されたら、画像シーンでは`Search`、テキストシーンでは`Send`を実行できます。2回目以降は展開をスキップします。

## Unity Editorで試す

実機へビルドせず、`LocalModels/`のモデルをmacOS / Windows上で実行できます。

1. `Tools > LiteRT-LM > Install Editor Native Library`を実行します。
2. 一度Playし、生成された`Assets/Editor/LiteRtLmEditorSettings.asset`を選択します。
3. `Test Image`へ解析する画像を割り当て、もう一度Playします。

| 設定 | 内容 |
|---|---|
| Model File Path | 使用するモデル。空欄なら、このプロジェクトと隣接するUnityプロジェクトの`LocalModels/`を検索 |
| Prefer Gpu | Metal / Direct3D 12を使用。失敗時はCPUへフォールバック |
| Test Image | カメラ画像の代わりに推論へ渡す画像 |

初回のみネイティブライブラリ（macOS約140MB、Windows約200MB）を取得します。モデルはEditorセッション中保持され、初回に生成される約2GBの一時キャッシュによって次回以降の読み込みが短縮されます。

<details>
<summary>Editor実行時の制限</summary>

- LinuxにはLiteRT-LMのビルド済みライブラリがないため、Editor実行には対応していません。
- ダウンロードしたライブラリは`Assets/Plugins/macOS/`または`Assets/Plugins/x86_64/`へ配置され、Git管理外になります。
- CPUへフォールバックした場合、サンプリング設定（Top K / Top P / Temperature / Seed）は無視されます。GPUとAndroid実機では反映されます。
- Editorでは基本的に`Test Image`を推論へ渡します。ただし、`SimpleMobileApp`では`Test Image`が未設定の場合にWebカメラの映像を使用できます。

</details>

## 使い方と設定

### 指定した対象の文字と数値を抽出する

`1-VisionAI-UserPrompt`シーンで`LLM Manager`を選び、System Promptに次のような指示を設定します。

```text
ユーザーが指定した対象だけを確認し、そこに書かれている文字列と数値のみを抽出してください。内容を意味のまとまりごとに整理し、「- 項目名: 読み取った値」の形式で箇条書きにしてください。対象外の情報は含めず、判読できない文字は推測せず「判読不能」と記載してください。
```

実行時のUser Promptでは対象を短く指定します。

```text
右側のモニターに表示されている内容
```

固定ROIに対象を収め続ける代わりに、自然言語で対象を限定するのがこのサンプルの狙いです。傾いた画像や動く対象でも使えますが、結果には取りこぼしが生じる場合があります。

### 推論設定

`LLM Manager`の`LlmManager`から変更できます。

| 項目 | 既定値 | 内容 |
|---|---:|---|
| Enable Thinking | オフ | 思考を有効化。推論時間はおよそ倍になる |
| Thinking Token Budget | 256 | 思考に使う最大トークン数 |
| Answer Token Budget | 512 | 回答の最大トークン数。0以下で無制限 |
| Top K | 1 | 次の単語の候補数。1では常に最有力候補を選ぶ |
| Top P | 0.95 | 確率上位の候補を残す割合。Top Kが2以上のとき有効 |
| Temperature | 1 | 高いほど回答が多様になる。Top Kが2以上のとき有効 |
| Randomize Seed | オン | リクエストごとにSeedを変える |
| Seed | 0 | Randomize Seedがオフのときに使う固定値 |

設定変更によるモデルの再読み込みはありません。思考内容は画面やログに出力されません。Top Kが1なら常に最有力候補を選びます。Top Kを2以上にして再現性も保ちたい場合は、Randomize SeedをオフにしてSeedを固定します。

推論中は経過時間が表示され、30秒で長時間警告、120秒でタイムアウト警告に変わります。ネイティブ推論は安全に中断できないため、完了しない場合はアプリを再起動してください。

## パッケージ構成

再利用可能なLLM処理は`Packages/LiteRtLmUnity`（`com.yoshinaga.litertlmunity`）に分離しています。他のプロジェクトでは、このフォルダをコピーして利用できます。

```text
Packages/LiteRtLmUnity/
  Runtime/    モデル展開、推論バックエンド、状態通知
  Editor/     モデル分割、Gradle設定、Editor用ライブラリ導入
  Android/    LiteRT-LMを呼び出すKotlinブリッジ

ARFoundationApp/Assets/Scripts/  AR Foundation版のカメラ・UI処理
SimpleMobileApp/Assets/Scripts/  WebCamTexture版のカメラ・UI処理
```

カメラ固有の処理は各サンプル側、モデル管理と推論処理はパッケージ側に置いています。

### 主な依存関係

| パッケージ | バージョン | 用途 |
|---|---:|---|
| R3 | 1.3.1 | イベント通知と状態管理 |
| ObservableCollections | 3.3.4 | R3対応コレクション |
| UniTask | Git URL | 非同期処理 |
| AR Foundation / ARCore | 6.3.5 | `ARFoundationApp`のみ |

導入方法は[R3とUniTaskのインストール手順](Documents/ExternalTools/R3_UniTask_Installation.md)を参照してください。LiteRT-LMのAndroidライブラリはGradle依存として自動追加されます。

## 制限事項

- 対応するビルド先はAndroidのみです。
- 実機動作はPixel 7（Android API 37）とSamsung Galaxy S22で確認しています。
- 回答はストリーミング表示されません。
- テキストサンプルは会話履歴を保持しません。
- 撮影画像は保存も送信もされません。
- 縦向き以外の画面方向は未検証です。

## ライセンス

コードは[MIT License](LICENSE)です。Gemmaモデルには[Gemma Terms of Use](https://ai.google.dev/gemma/terms)が適用されます。モデルファイルはこのリポジトリに含まれません。

`Assets/Fonts/NotoSansJP`のNoto Sans JPはSIL Open Font License 1.1です。
