# LocalVisionAI

Android端末のカメラで撮った写真を、**端末の中だけで**AIに説明させるUnityアプリです。通信は一切行わず、モデルもAPKに同梱されています。

推論にはGoogleの[LiteRT-LM](https://github.com/google-ai-edge/LiteRT-LM)とGemmaを使い、カメラ画像の取得にはAR Foundationを使っています。

## できること

1. `Analyze Camera`を押すとARカメラから静止画を1枚取得する
2. 画像とプロンプトを端末内のGemmaへ渡す
3. 生成された説明文を画面に表示する

1回あたり数十秒かかります。端末の性能に大きく左右されます。

## 動作環境

| | |
|---|---|
| Unity | 6000.3.7f1 |
| プラットフォーム | Android (ARM64) |
| 最小APIレベル | 29 |
| スクリプティング | IL2CPP |
| 必要な空き容量 | 6〜8GB程度 |

ARCoreに対応したAndroid端末が必要です。モデルの読み込みだけで2.5GB以上のメモリを使うため、RAMに余裕のある端末を推奨します。

主なパッケージはAR Foundation / ARCore 6.3.5、R3 1.3.1、UniTaskです。LiteRT-LMはGradleのMaven依存として自動的に導入されるので、手動の準備は不要です。

## セットアップ

### 1. モデルを用意する

[litert-community/gemma-4-E2B-it-litert-lm](https://huggingface.co/litert-community/gemma-4-E2B-it-litert-lm)から`gemma-4-E2B-it.litertlm`をダウンロードし、次の場所に置きます。

```text
MobileApp/LocalModels/gemma-4-E2B-it.litertlm
```

`LocalModels/`はGit管理外です。ディレクトリが無ければ作ってください。

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

別のモデルを使う場合は、ファイルを`LocalModels/`へ置き、`Assets/Scripts/BundledModelPaths.cs`の`FileName`を書き換えるだけです。

### 2. ビルドする

`MobileApp`をUnityで開き、`Assets/Scenes/VisionAI.unity`を開いてビルドします。コマンドラインからビルドする場合は次のメニューが使えます。

```text
Tools > Local Vision AI > Build Android APK
```

ビルド時に、`LocalModels/`のモデルが自動的に1GiB単位へ分割されて`StreamingAssets`へ配置されます。この分割は、Android Gradle Pluginが単一アセットを2GiB以上扱えないためのものです。分割されたファイルとハッシュはGit管理外です。

APKは2.6GB前後になります。

### 3. 実行する

初回起動時に、APK内の分割ファイルが端末のプライベート領域へ結合・展開され、SHA-256で検証されます。進捗は画面に表示されます。2回目以降はこの処理をスキップします。

展開が終わるとAIエンジンが初期化され、`AI Ready`と表示されたら`Analyze Camera`が押せるようになります。

## 設定

Hierarchyの`VisionAiManager`のInspectorから変更できます。

| 項目 | 既定値 | 説明 |
|---|---|---|
| Enable Thinking | オフ | 思考プロセスを有効にする。推論時間はおよそ倍になる |
| Thinking Token Budget | 256 | 思考に使うトークン数の上限 |
| Answer Token Budget | 512 | 回答の上限トークン数。0以下で無制限 |

思考の内容は画面にもログにも出力されません。設定を変えてもモデルの再読み込みは発生しません。
推論中は経過秒数が表示され、30秒を超えると長時間警告、120秒を超えるとタイムアウト警告へ切り替わります。ネイティブ推論は安全に中断できないため、タイムアウト後も完了しない場合はアプリを再起動してください。

## 構成

```text
MainCoordinator        Scene上のUIを所有し、各Managerを繋ぐ
VisionAiDataSource     R3によるイベントハブ
ImageCaptureManager    ARカメラから静止画を取得しJPEGへ変換する
VisionAiManager        モデル展開、エンジン初期化、推論を管理する
ShowResultManager      進捗と結果を表示用の文字列へ整形する
BundledModelBridge.kt  LiteRT-LMを呼ぶKotlin側の窓口
```

UIの型を持つのは`MainCoordinator`だけです。各Managerはコールバックで値を報告するだけなので、UIの実装から独立しています。

## 制限事項

- 実機はPixel 7 (Android API 37) で確認しています。他機種は未検証です。
- 推論結果はストリーミング表示しません。完了までは経過時間と長時間警告のみ表示します。
- 回答が長いと画面の下で見切れます。
- 撮影した画像は保存も送信もされません。
- 縦向き以外の画面の向きは個別に確認していません。

## ライセンス

このリポジトリのコードはMIT Licenseです。[LICENSE](LICENSE)を参照してください。

同梱するGemmaモデルは[Gemma Terms of Use](https://ai.google.dev/gemma/terms)に従います。モデルファイルはこのリポジトリには含まれていません。

`Assets/Fonts/NotoSansJP`のNoto Sans JPはSIL Open Font License 1.1です。
