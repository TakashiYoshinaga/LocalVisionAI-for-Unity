# LocalVisionAI for Unity

*[日本語版 README](README_JP.md)*

A Unity sample that describes photos taken with an Android device's camera, and answers typed questions, **entirely on the device**. It performs no network communication, and the model is bundled inside the APK.

Inference uses Google's [LiteRT-LM](https://github.com/google-ai-edge/LiteRT-LM) with Gemma. The repository contains two Unity projects that differ in how they obtain the camera image.

- `ARFoundationApp`: uses AR Foundation / ARCore
- `SimpleMobileApp`: uses the ordinary device camera through `WebCamTexture`

`ARFoundationApp` configures the camera through AR Foundation so that AR features can be added later. It does not currently place objects, anchors, or plane-detection results in AR space.

> **Android is the only build target.** The LiteRT-LM integration relies on an Android-specific Kotlin bridge and Gradle setup, so iOS and desktop players cannot be built.
> While developing, though, the same model runs in the Unity Editor on macOS and Windows. See [4. Running in the Editor (macOS and Windows)](#4-running-in-the-editor-macos-and-windows).

## Demo video

[![LocalVisionAI OCR demo](Documents/Materials/YouTubeThumbnail_OfflineVisionAI_EN.png)](https://www.linkedin.com/posts/tks-yoshinaga_localllms-ocr-computervision-activity-7506653758345560064-FxHq)

[Watch the demo on YouTube](https://www.youtube.com/watch?v=gVoTzhzCqSQ)

## What it does

- Captures a single still image from the device camera and passes it to Gemma along with a fixed or typed prompt
- Also includes, as a bonus, a text-only version that asks a single question using a fixed system prompt and a typed user prompt, with no image
- Displays the generated answer in a scrollable view

The text sample creates a new conversation for every question — one question, one answer. No conversation history is kept.

Each run takes from a few seconds to a few tens of seconds, and depends heavily on the device's performance.

## Requirements

| | |
|---|---|
| Unity | 6000.3.7f1 |
| Platform | Android (ARM64) |
| Minimum API level | 29 |
| Scripting backend | IL2CPP |
| Free storage needed | roughly 6–8 GB |

The image sample in `ARFoundationApp` requires an ARCore-capable device. `SimpleMobileApp` uses the ordinary device camera and does not depend on ARCore. The text sample never sends image data to inference. Loading the model alone uses more than 2.5 GB of memory, so a device with ample RAM is recommended.

## Dependencies

Both projects use the following packages.

| Package | Version / installation | Purpose |
|---|---|---|
| R3 | 1.3.1 | Event notification and state management |
| ObservableCollections / ObservableCollections.R3 | 3.3.4 | R3-aware collections |
| UniTask | Git URL | Async operations for Unity |
| `com.yoshinaga.litertlmunity` | Local reference to `Packages/LiteRtLmUnity` | Model extraction and LiteRT-LM integration |

`ARFoundationApp` additionally uses AR Foundation / ARCore 6.3.5. For registering and installing R3, ObservableCollections, and UniTask, see the [detailed R3 and UniTask installation guide](Documents/ExternalTools/R3_UniTask_Installation.md).

The LiteRT-LM Android library is added automatically as a Gradle Maven dependency at build time, so no manual installation is needed.

## Setup

### 1. Prepare the model

Download `gemma-4-E2B-it.litertlm` from [litert-community/gemma-4-E2B-it-litert-lm](https://huggingface.co/litert-community/gemma-4-E2B-it-litert-lm) and place it in the `LocalModels` folder of the project you intend to use.

```text
ARFoundationApp/LocalModels/gemma-4-E2B-it.litertlm
SimpleMobileApp/LocalModels/gemma-4-E2B-it.litertlm
```

The `LocalModels/` directories are included in the repository along with their `.gitkeep` files, but `.litertlm` model files are not tracked by Git. Download and place the model yourself.

> **`gemma-4-E2B-it-gpu.litertlm` from the same repository will not work.**
> It is text-only and contains no vision encoder, so passing an image fails with
> `NOT_FOUND: TF_LITE_VISION_ENCODER not found in the model.`
> You can check whether a model supports image input with:
>
> ```bash
> LC_ALL=C grep -a -o -E "tf_lite_[a-z_]+" <model> | sort -u
> ```
>
> If `tf_lite_vision_encoder` appears in the output, the model accepts image input.

To use a different model than Gemma 4 E2B — E4B, for example — place the file in `LocalModels/` and change `FileName` in `Packages/LiteRtLmUnity/Runtime/BundledModelPaths.cs`.

### 2. Build

Open `ARFoundationApp` or `SimpleMobileApp` in Unity and pick the sample scene that matches what you want to do.

- `Assets/Scenes/0-VisionAI-SystemPromptOnly.unity`: uses the fixed system prompt configured in the Inspector
- `Assets/Scenes/1-VisionAI-UserPrompt.unity`: lets you type a user prompt on screen
- `Assets/Scenes/2-VisionAI-TextPrompt.unity`: asks a single question from a typed user prompt, with no image

Add the scene you want to Build Settings, or open it in the Editor, and then build.

At build time, the model in `LocalModels/` is automatically split into 1 GiB chunks and placed in `StreamingAssets`. This split exists because the Android Gradle Plugin cannot handle a single asset of 2 GiB or more. The split files and their hashes are not tracked by Git.

The resulting APK is around 2.6 GB.

### 3. Run

On first launch, the split files inside the APK are merged and extracted into the device's private storage and verified with SHA-256. Progress is shown on screen. Later launches skip this step. (It takes about a minute.)

Once extraction finishes, the AI engine is initialized. When `AI Ready` appears, `Search` becomes available in the image scenes, and `Send` becomes available after typing in the text scene.

### 4. Running in the Editor (macOS and Windows)

So that changing a prompt does not mean waiting for another Build and Run, the Unity Editor can run the very same `.litertlm` file from `LocalModels/`. No server and no second model are involved, and the Android build keeps going through the Kotlin bridge exactly as before.

1. Run `Tools > LiteRT-LM > Install Editor Native Library`. It fetches the prebuilt library for your system from the LiteRT-LM releases and imports it as an Editor-only plugin, so it never reaches the APK.
   - On macOS, `libCLiteRTLM_mac.dylib` (about 140 MB) into `Assets/Plugins/macOS/`.
   - On Windows, `litert-lm.dll` (about 48 MB) into `Assets/Plugins/x86_64/`, together with the DirectX Shader Compiler (`dxcompiler.dll` and `dxil.dll`, about 32 MB) that the GPU path needs. Roughly 200 MB is downloaded in total.
2. Press Play once; that creates `Assets/Editor/LiteRtLmEditorSettings.asset`. Select it and assign the texture you want analyzed to `Test Image`, in place of a camera frame.
3. Press Play again. No camera is opened, the assigned image fills the view behind the UI, and `Search` sends that image to the model.

`LiteRtLmEditorSettings` has three fields.

| Setting | What it does |
|---|---|
| Model File Path | The `.litertlm` to open. Leave it empty to search this project's `LocalModels/` and then the `LocalModels/` of the other Unity projects beside it, so that a 2.6 GB model does not have to be copied per project. |
| Prefer Gpu | Runs on the GPU (Metal on macOS, Direct3D 12 on Windows), falling back to the CPU on its own when the model cannot be loaded there. The GPU is around three times faster, so it is worth keeping on. |
| Test Image | The still image sent instead of a camera frame. |

The engine stays loaded for the rest of the Editor session, so pressing Play again does not reload the model. It is released when scripts recompile and when the Editor quits.

The first load writes LiteRT-LM's compiled weight and program caches, close to 2 GB in total, into the system temporary folder. That is what makes later loads take seconds. They are rebuilt on their own if they are removed.

> **How the Editor differs**
>
> - macOS and Windows only, because those are the desktop systems LiteRT-LM publishes a prebuilt library for. On a Linux Editor, building to a device is still the only way.
> - The `Sampling` settings (Top K, Top P, Temperature, Seed) apply on the GPU only. The desktop CPU path has no configurable sampler, so after a fallback to the CPU they are ignored and a warning says so. They always apply on the device.
> - Both projects use the test image, because neither camera returns anything in the Editor. `ARFoundationApp` shows it through a full-screen image created while playing, since its camera view is the AR background rather than a UI element.

## Tips: extracting only text and numbers from a chosen subject

The `1-VisionAI-UserPrompt` scene in either project can also be used like OCR. Select `LLM Manager` in the Hierarchy and set the System Prompt on `LlmManager` to something like this:

```text
Look only at the subject the user specifies and extract only the text and numbers written on it. Organize the content into meaningful groups and list it as "- label: value". Do not include anything outside the specified subject, and do not guess at illegible characters — write "illegible" instead.
```

At run time, name the subject you want read in the user prompt, as in the [demo video](https://www.youtube.com/watch?v=gVoTzhzCqSQ):

```text
The content shown on the monitor on the right
```

Narrowing the subject makes it much easier to get just the text and numbers written on that object, rather than a description of the whole image.

## Settings

These can be changed in the Inspector of `LlmManager` on the `LLM Manager` object in the Hierarchy.

| Setting | Default | Description |
|---|---|---|
| Enable Thinking | Off | Enables the thinking process. Roughly doubles inference time |
| Thinking Token Budget | 256 | Maximum number of tokens spent on thinking |
| Answer Token Budget | 512 | Maximum tokens in the answer. Unlimited when 0 or less |
| Top K | 1 | How many candidates the model picks its next word from. 1 means it always takes the most likely one, so the same input tends to give the same answer and Top P, Temperature and Seed stop mattering. 0 or less leaves this to LiteRT-LM |
| Top P | 0.95 | Discards unlikely candidates, keeping only the top ones that together account for this share of the probability. Used only when Top K is 2 or more |
| Temperature | 1 | Higher values make the wording more varied, lower values more predictable. Used only when Top K is 2 or more |
| Randomize Seed | On | Starts each request from a new random draw, so asking the same question again can give a different answer. Used only when Top K is 2 or more |
| Seed | 0 | A fixed starting point for those random draws, which makes the same question give the same answer again. Used only when Randomize Seed is off and Top K is 2 or more |

The thinking content is written neither to the screen nor to the log. Changing these settings does not reload the model.

The sampling defaults are the ones LiteRT-LM applies when neither the model file nor the app specifies any, so the shipped values keep the previous behavior: with Top K at 1 the model always takes its strongest candidate, so the same input tends to give the same answer. An image is never quite the same twice, though, so a fresh photo of the same subject can still be answered differently. Temperature only begins to matter once Top K is 2 or more.

Each request is a new conversation, so the random draws start over every time. Leave Randomize Seed on when asking again should give a different answer, or turn it off and fix Seed when the same question should come back with the same answer.

While inference is running, elapsed seconds are displayed. Past 30 seconds the display switches to a long-running warning, and past 120 seconds to a timeout warning. Native inference cannot be interrupted safely, so if it still does not finish after the timeout warning, restart the app.

## Structure

The LLM portion is factored out into the Unity package at `Packages/LiteRtLmUnity` (`com.yoshinaga.litertlmunity`). Both sample projects reference it locally, so reusing it in another project is just a matter of copying that folder over.

```text
Packages/LiteRtLmUnity/     Reusable LLM portion
  Runtime/                  LiteRtLmUnity assembly
    LlmManager              Manages model extraction, engine init, and inference
    ILlmBackend             The seam that swaps out how inference actually runs
    AndroidLlmBackend       Calls the Kotlin bridge on the device
    EditorLlmBackend        Calls the LiteRT-LM C API in the Editor (macOS, Windows)
    LiteRtLmNative          P/Invoke declarations for that C API
    EditorLlmSettings       The model and test image used when playing in the Editor
    LlmDataSource           R3-based event hub
    ShowResultManager       Formats progress and results into display strings
  Editor/                   LiteRtLmUnity.Editor assembly
    BundledModelBuildSetup  Splits the model into StreamingAssets
    LiteRtLmAndroidBuildSetup  Adds the LiteRT-LM dependency to the generated Gradle
    EditorNativeLibrarySetup   Installs the LiteRT-LM library used by the Editor
  Android/
    BundledModelBridge.kt   Kotlin-side entry point that calls LiteRT-LM

ARFoundationApp/Assets/Scripts/  AR Foundation specific parts
SimpleMobileApp/Assets/Scripts/  Ordinary camera specific parts
  ImagePromptCoordinator    Owns the image scene UI and wires the managers together
  TextPromptCoordinator     Owns the text-only scene UI and the one-shot send flow
  ImageCaptureManager       Converts the camera image to JPEG and builds the request
  CameraImageManager        Manages preview and still capture in the ordinary camera version
```

Only `ImagePromptCoordinator` (for images) and `TextPromptCoordinator` (for text) hold UI types. The managers merely report values through callbacks, so they are independent of the UI implementation. Anything that depends on the camera approach stays in the sample projects rather than in the package.

## Limitations

- Android is the only build target. iOS and desktop players are not supported.
- Running in the Editor works on macOS and Windows; a Linux Editor cannot run the model.
- Verified on a Pixel 7 (Android API 37) and a Samsung Galaxy S22. Other devices are untested.
- Inference results are not streamed. Until completion, only the elapsed time and the long-running warning are shown.
- The text sample keeps no conversation history, so you cannot ask follow-up questions that build on a previous one.
- Captured images are neither saved nor transmitted.
- Screen orientations other than portrait have not been verified individually.

## License

The code in this repository is MIT licensed. See [LICENSE](LICENSE).

The bundled Gemma model is subject to the [Gemma Terms of Use](https://ai.google.dev/gemma/terms). The model file is not included in this repository.

Noto Sans JP in `Assets/Fonts/NotoSansJP` is licensed under the SIL Open Font License 1.1.
