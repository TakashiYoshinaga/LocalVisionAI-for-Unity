# LocalVisionAI for Unity

*[English README](README.md)*

Android端末のカメラで撮った写真の説明やテキスト入力による質問を、**端末の中だけで**実行するUnityサンプルです。通信は一切行わず、モデルもAPKに同梱されています。

推論にはGoogleの[LiteRT-LM](https://github.com/google-ai-edge/LiteRT-LM)とGemmaを使用します。カメラ画像の取得方法が異なる2つのUnityプロジェクトを収録しています。

- `ARFoundationApp`: AR Foundation / ARCoreを使用するバージョン
- `SimpleMobileApp`: `WebCamTexture`で通常の端末カメラを使用するバージョン

`ARFoundationApp`は、今後AR機能を組み込めるようにAR Foundationでカメラを構成したバージョンです。現時点ではAR空間にオブジェクト、アンカー、平面認識結果などを表示する機能は実装していません。

> **現在はAndroidのみ対応しています。** LiteRT-LMとの連携をAndroid固有のKotlinブリッジとGradle設定で行っているため、iOSやデスクトップには対応していません。

## デモ動画

[![LocalVisionAI OCRデモ](Documents/Materials/YouTubeThumbnail_OfflineVisionAI.png)](https://www.linkedin.com/posts/tks-yoshinaga_localllms-ocr-computervision-activity-7506653758345560064-FxHq)

[YouTubeでデモ動画を見る](https://www.youtube.com/watch?v=gVoTzhzCqSQ)

## できること

- 端末カメラから静止画を1枚取得し、固定または入力したプロンプトと一緒にGemmaへ渡す
- おまけとして、画像を使わず固定System Promptと入力したUser Promptだけで一回質問するテキスト版も収録
- 生成された回答をスクロール可能な画面へ表示

テキストサンプルは質問ごとに新しいConversationを作る一問一答です。会話履歴は保持しません。

1回あたり数秒から数十秒かかります。端末の性能に大きく左右されます。

## 動作環境

| | |
|---|---|
| Unity | 6000.3.7f1 |
| プラットフォーム | Android (ARM64) |
| 最小APIレベル | 29 |
| スクリプティング | IL2CPP |
| 必要な空き容量 | 6〜8GB程度 |

`ARFoundationApp`の画像サンプルにはARCore対応端末が必要です。`SimpleMobileApp`は通常の端末カメラを使用するため、ARCoreには依存しません。テキストサンプルは画像データを推論へ送りません。モデルの読み込みだけで2.5GB以上のメモリを使うため、RAMに余裕のある端末を推奨します。

## 依存関係

両プロジェクトで次のパッケージを使用します。

| パッケージ | バージョン／導入方法 | 用途 |
|---|---|---|
| R3 | 1.3.1 | イベント通知と状態管理 |
| ObservableCollections / ObservableCollections.R3 | 3.3.4 | R3対応コレクション |
| UniTask | Git URL | Unity向け非同期処理 |
| `com.yoshinaga.litertlmunity` | `Packages/LiteRtLmUnity`のローカル参照 | モデル展開とLiteRT-LM連携 |

`ARFoundationApp`だけは、さらにAR Foundation / ARCore 6.3.5を使用します。R3、ObservableCollections、UniTaskの登録とインストールについては、[R3とUniTaskの詳しいインストール手順](Documents/ExternalTools/R3_UniTask_Installation.md)を参照してください。

LiteRT-LMのAndroidライブラリはビルド時にGradleのMaven依存として自動的に追加されるため、手動での導入は不要です。

## セットアップ

### 1. モデルを用意する

[litert-community/gemma-4-E2B-it-litert-lm](https://huggingface.co/litert-community/gemma-4-E2B-it-litert-lm)から`gemma-4-E2B-it.litertlm`をダウンロードし、次の場所に置きます。

使用するプロジェクトの`LocalModels`へ配置します。

```text
ARFoundationApp/LocalModels/gemma-4-E2B-it.litertlm
SimpleMobileApp/LocalModels/gemma-4-E2B-it.litertlm
```

`LocalModels/`ディレクトリは`.gitkeep`とともにリポジトリへ含まれていますが、`.litertlm`モデルファイルはGit管理外です。モデルは各自でダウンロードして配置してください。

> **同じリポジトリの`gemma-4-E2B-it-gpu.litertlm`は使えません。**
> こちらはテキスト専用で画像エンコーダを含まないため、画像を渡すと
> `NOT_FOUND: TF_LITE_VISION_ENCODER not found in the model.`で失敗します。
> 画像に使えるモデルかどうかは次で判別できます。
>
> ```bash
> LC_ALL=C grep -a -o -E "tf_lite_[a-z_]+" <モデル> | sort -u
> ```
>
> `tf_lite_vision_encoder`が出力されれば画像入力に使えます。

Gemma4 E2BではなくE4Bなど異なるモデルを使う場合は、ファイルを`LocalModels/`へ置き、`Packages/LiteRtLmUnity/Runtime/BundledModelPaths.cs`の`FileName`を書き換えるだけです。

### 2. ビルドする

`ARFoundationApp`または`SimpleMobileApp`をUnityで開き、用途に応じて次のサンプルシーンを選びます。

- `Assets/Scenes/0-VisionAI-SystemPromptOnly.unity`: Inspectorで設定した固定System Promptを使用する
- `Assets/Scenes/1-VisionAI-UserPrompt.unity`: 画面からUser Promptを入力する
- `Assets/Scenes/2-VisionAI-TextPrompt.unity`: 画像なしでUser Promptを入力し、一回だけ質問する

使用するシーンをBuild Settingsへ追加するかEditorで開くかしてからビルドします。

ビルド時に、`LocalModels/`のモデルが自動的に1GiB単位へ分割されて`StreamingAssets`へ配置されます。この分割は、Android Gradle Pluginが単一アセットを2GiB以上扱えないためのものです。分割されたファイルとハッシュはGit管理外です。

APKは2.6GB前後になります。

### 3. 実行する

初回起動時に、APK内の分割ファイルが端末のプライベート領域へ結合・展開され、SHA-256で検証されます。進捗は画面に表示されます。2回目以降はこの処理をスキップします。(1分くらいかかります)

展開が終わるとAIエンジンが初期化され、`AI Ready`と表示されたら画像シーンでは`Search`、テキストシーンでは入力後に`Send`が押せるようになります。



## Tips: 指定した対象から文字と数値だけを抽出する

両プロジェクトの`1-VisionAI-UserPrompt`シーンは、OCRのような使い方もできます。Hierarchyの`LLM Manager`を選択し、`LlmManager`のSystem Promptへ次のように設定します。

```text
ユーザーが指定した対象だけを確認し、そこに書かれている文字列と数値のみを抽出してください。内容を意味のまとまりごとに整理し、「- 項目名: 読み取った値」の形式で箇条書きにしてください。対象外の情報は含めず、判読できない文字は推測せず「判読不能」と記載してください。
```

実行時のUser Promptには、[デモ動画](https://www.youtube.com/watch?v=gVoTzhzCqSQ)のように読み取り対象を指定します。

```text
右側のモニターに表示されている内容
```

対象を限定することで、画像全体の説明ではなく、指定した物体に書かれた文字列と数値だけを取得しやすくなります。

## 設定

Hierarchyの`LLM Manager`が持つ`LlmManager`のInspectorから変更できます。

| 項目 | 既定値 | 説明 |
|---|---|---|
| Enable Thinking | オフ | 思考プロセスを有効にする。推論時間はおよそ倍になる |
| Thinking Token Budget | 256 | 思考に使うトークン数の上限 |
| Answer Token Budget | 512 | 回答の上限トークン数。0以下で無制限 |

思考の内容は画面にもログにも出力されません。設定を変えてもモデルの再読み込みは発生しません。
推論中は経過秒数が表示され、30秒を超えると長時間警告、120秒を超えるとタイムアウト警告へ切り替わります。ネイティブ推論は安全に中断できないため、タイムアウト後も完了しない場合はアプリを再起動してください。

## 構成

LLM部分は`Packages/LiteRtLmUnity`のUnityパッケージ（`com.yoshinaga.litertlmunity`）に切り出してあります。両サンプルプロジェクトからローカル参照で読み込んでいるので、他のプロジェクトへはこのフォルダを持っていくだけで再利用できます。

```text
Packages/LiteRtLmUnity/     再利用可能なLLM部分
  Runtime/                  LiteRtLmUnityアセンブリ
    LlmManager              モデル展開、エンジン初期化、推論を管理する
    LlmDataSource           R3によるイベントハブ
    ShowResultManager       進捗と結果を表示用の文字列へ整形する
  Editor/                   LiteRtLmUnity.Editorアセンブリ
    BundledModelBuildSetup  モデルを分割してStreamingAssetsへ配置する
    LiteRtLmAndroidBuildSetup  生成されたGradleへLiteRT-LMの依存を追加する
  Android/
    BundledModelBridge.kt   LiteRT-LMを呼ぶKotlin側の窓口

ARFoundationApp/Assets/Scripts/  AR Foundation版固有の部分
SimpleMobileApp/Assets/Scripts/  通常カメラ版固有の部分
  ImagePromptCoordinator    画像SceneのUIを所有し、各Managerを繋ぐ
  TextPromptCoordinator     テキスト専用SceneのUIと一問一答の送信を管理する
  ImageCaptureManager       カメラ画像をJPEGへ変換して推論要求を作成する
  CameraImageManager        通常カメラ版でプレビューと静止画取得を管理する
```

UIの型を持つのは画像用の`ImagePromptCoordinator`とテキスト用の`TextPromptCoordinator`だけです。各Managerはコールバックで値を報告するだけなので、UIの実装から独立しています。カメラ方式に依存する処理はパッケージ側には入れず、各サンプルプロジェクト側に置いています。

## 制限事項

- Androidのみ対応です。iOSやデスクトップには対応していません。
- 実機はPixel 7 (Android API 37)およびSamsung Galaxy S22で確認しています。他機種は未検証です。
- 推論結果はストリーミング表示しません。完了までは経過時間と長時間警告のみ表示します。
- テキストサンプルは会話履歴を保持しないため、前の質問を前提にした続きの会話はできません。
- 撮影した画像は保存も送信もされません。
- 縦向き以外の画面の向きは個別に確認していません。

## ライセンス

このリポジトリのコードはMIT Licenseです。[LICENSE](LICENSE)を参照してください。

同梱するGemmaモデルは[Gemma Terms of Use](https://ai.google.dev/gemma/terms)に従います。モデルファイルはこのリポジトリには含まれていません。

`Assets/Fonts/NotoSansJP`のNoto Sans JPはSIL Open Font License 1.1です。
