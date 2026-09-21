// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

#if UNITY_EDITOR_OSX || UNITY_EDITOR_WIN

using System;
using System.Collections.Concurrent;
using System.IO;
#if UNITY_EDITOR_WIN
using System.Runtime.InteropServices;
#endif
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace LiteRtLmUnity
{
    /// <summary>
    /// Runs the same <c>.litertlm</c> model the APK ships, inside the Editor,
    /// through the LiteRT-LM C API. It exists so that changing a prompt does not
    /// mean waiting for another Build and Run.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The native calls block for seconds at a time, so they run on the thread
    /// pool and their results reach the main thread through <see cref="Pump"/>.
    /// </para>
    /// <para>
    /// The conversation is kept deliberately identical to
    /// <c>BundledModelBridge.kt</c> — one conversation per request, the system
    /// prompt as the preface, the image first and the user prompt after it — so
    /// that a prompt tuned here behaves the same way on the device.
    /// </para>
    /// </remarks>
    internal sealed class EditorLlmBackend : ILlmBackend
    {
        /// <summary>
        /// Gemma 4 wraps its thinking text in these markers. The answer is
        /// stripped of anything between them, mirroring the Kotlin bridge, so that
        /// thinking text never reaches the UI.
        /// </summary>
        private const string ThinkingOpen = "<|channel>";
        private const string ThinkingClose = "<channel|>";

        private const string MissingLibraryMessage =
            "The LiteRT-LM Editor library is not installed. Run " +
            "Tools > LiteRT-LM > Install Editor Native Library.";

        // The engine outlives a single Play session on purpose: loading the model
        // takes seconds, and reloading it every time Play is pressed would give
        // back exactly the delay this backend exists to remove. It is released on
        // domain reload and when the Editor quits, both wired up below.
        private static IntPtr s_engine;
        private static string s_engineModelPath;
        private static bool s_engineUsesGpu;
        private static bool s_cleanupRegistered;

        /// <summary>
        /// How many native calls are in flight. <see cref="ReleaseEngine"/> waits
        /// for this to reach zero, because freeing the engine underneath a running
        /// call would take the Editor down with it.
        /// </summary>
        private static int s_activeNativeCalls;

        private readonly LlmManager _manager;
        private readonly ConcurrentQueue<Action> _mainThreadWork = new();
        private bool _shutdownRequested;

        public EditorLlmBackend(LlmManager manager)
        {
            _manager = manager;
            RegisterCleanup();
        }

        public void PrepareModel()
        {
            _shutdownRequested = false;

            string modelPath = EditorLlmSettings.LoadOrCreate().ResolveModelFilePath();

            if (string.IsNullOrEmpty(modelPath) || !File.Exists(modelPath))
            {
                _manager.ReportModelProgress(
                    LlmPhase.Error,
                    $"Could not find {BundledModelPaths.FileName}. Put it in the " +
                    "project's LocalModels folder, or set a Model File Path on " +
                    $"{EditorLlmSettings.DefaultAssetPath}.",
                    progress: null,
                    ready: false,
                    // Retrying is worth offering: the file can be dropped into
                    // LocalModels while the Editor is still playing.
                    retryable: true,
                    modelPath: null);
                return;
            }

            // Nothing has to be extracted in the Editor, so this phase only reports
            // that the file is where the engine expects it.
            _manager.ReportModelProgress(
                LlmPhase.ExtractingModel,
                "Using the model in LocalModels...",
                progress: 1f,
                ready: true,
                retryable: false,
                modelPath: modelPath);
        }

        public void InitializeEngine(string preparedModelPath)
        {
            if (s_engine != IntPtr.Zero && s_engineModelPath == preparedModelPath)
            {
                // Still loaded from an earlier Play session.
                _manager.ReportEngineProgress(LlmPhase.Ready, "AI Ready", ready: true, retryable: false);
                return;
            }

            bool preferGpu = EditorLlmSettings.LoadOrCreate().PreferGpu;

            _manager.ReportEngineProgress(
                LlmPhase.Initializing,
                "Initializing AI engine...",
                ready: false,
                retryable: false);

            RunInBackground(() =>
            {
                ReleaseEngineCore();

                LiteRtLmNative.SetMinLogLevel(LiteRtLmNative.LogSeverityWarning);

                string failure = null;
                string gpuFailure = null;

                if (preferGpu)
                {
                    failure = gpuFailure = TryCreateEngine(preparedModelPath, "GPU");
                }

                if (s_engine == IntPtr.Zero)
                {
                    failure = TryCreateEngine(preparedModelPath, "CPU");
                }

                // Only worth mentioning once the CPU has actually taken over. When
                // neither backend starts — most often because the library was
                // never installed, which is the normal state for someone who only
                // builds to a device — the error below says all there is to say,
                // and a warning about the GPU on top of it points at the wrong
                // thing.
                if (gpuFailure != null && s_engine != IntPtr.Zero)
                {
                    string reported = gpuFailure;
                    Post(() => Debug.LogWarning(
                        $"The GPU backend could not load the model ({reported}). " +
                        "Falling back to the CPU."));
                }

                if (s_engine == IntPtr.Zero)
                {
                    string reason = failure;
                    PostReport(() => _manager.ReportEngineProgress(
                        LlmPhase.Error,
                        $"Could not initialize the AI engine: {reason}",
                        ready: false,
                        retryable: true));
                    return;
                }

                s_engineModelPath = preparedModelPath;
                bool usedGpu = s_engineUsesGpu;
                Post(() => Debug.Log(
                    $"LiteRT-LM engine ready in the Editor on the {(usedGpu ? "GPU" : "CPU")} " +
                    $"using {preparedModelPath}."));
                PostReport(() => _manager.ReportEngineProgress(
                    LlmPhase.Ready,
                    "AI Ready",
                    ready: true,
                    retryable: false));
            });
        }

        public void Analyze(int requestId, byte[] jpegData, LlmRequestOptions options)
        {
            if (jpegData == null || jpegData.Length == 0)
            {
                _manager.ReportAnalysis(requestId, LlmPhase.Error, "The captured image was empty.", null);
                return;
            }

            SendMessage(
                requestId,
                BuildMessageJson(Convert.ToBase64String(jpegData), options.UserPrompt),
                options);
        }

        public void AnalyzeText(int requestId, LlmRequestOptions options)
        {
            SendMessage(requestId, BuildMessageJson(null, options.UserPrompt), options);
        }

        public void Pump()
        {
            while (_mainThreadWork.TryDequeue(out Action work))
            {
                work();
            }
        }

        public void Shutdown()
        {
            // The engine is intentionally left loaded for the next Play session;
            // only the reporting stops. ReleaseEngine runs on domain reload and
            // when the Editor quits instead.
            _shutdownRequested = true;
        }

        private void SendMessage(int requestId, string messageJson, LlmRequestOptions options)
        {
            if (s_engine == IntPtr.Zero)
            {
                _manager.ReportAnalysis(requestId, LlmPhase.Error, "The AI engine is not ready yet.", null);
                return;
            }

            bool applySampler = options.TopK > 0 && s_engineUsesGpu;

            if (options.TopK > 0 && !s_engineUsesGpu)
            {
                // The desktop CPU executor answers an explicit sampler with
                // "Sampler type not implemented yet", so passing one would fail the
                // request outright rather than merely be ignored.
                Debug.LogWarning(
                    "The sampling settings are ignored while the Editor runs on the CPU, " +
                    "because the desktop CPU path has no configurable sampler. They still " +
                    "apply on the device.");
            }

            RunInBackground(() =>
            {
                IntPtr sampler = IntPtr.Zero;
                IntPtr sessionConfig = IntPtr.Zero;
                IntPtr thinking = IntPtr.Zero;
                IntPtr conversationConfig = IntPtr.Zero;
                IntPtr conversation = IntPtr.Zero;
                IntPtr response = IntPtr.Zero;

                try
                {
                    sessionConfig = LiteRtLmNative.SessionConfigCreate();

                    if (options.AnswerTokenBudget > 0)
                    {
                        LiteRtLmNative.SessionConfigSetMaxOutputTokens(sessionConfig, options.AnswerTokenBudget);
                    }

                    if (applySampler)
                    {
                        sampler = LiteRtLmNative.SamplerParamsCreate(LiteRtLmNative.SamplerTypeTopK);
                        LiteRtLmNative.SamplerParamsSetTopK(sampler, options.TopK);
                        LiteRtLmNative.SamplerParamsSetTopP(sampler, options.TopP);
                        LiteRtLmNative.SamplerParamsSetTemperature(sampler, options.Temperature);
                        LiteRtLmNative.SamplerParamsSetSeed(sampler, options.Seed);
                        LiteRtLmNative.SessionConfigSetSamplerParams(sessionConfig, sampler);
                    }

                    conversationConfig = LiteRtLmNative.ConversationConfigCreate();
                    LiteRtLmNative.ConversationConfigSetSessionConfig(conversationConfig, sessionConfig);

                    if (!string.IsNullOrWhiteSpace(options.SystemPrompt))
                    {
                        LiteRtLmNative.ConversationConfigSetSystemMessage(conversationConfig, options.SystemPrompt);
                    }

                    thinking = LiteRtLmNative.ThinkingConfigCreate();
                    LiteRtLmNative.ThinkingConfigSetEnableThinking(thinking, options.EnableThinking);
                    LiteRtLmNative.ThinkingConfigSetTokenBudget(thinking, options.ThinkingTokenBudget);
                    LiteRtLmNative.ConversationConfigSetThinkingConfig(conversationConfig, thinking);

                    // One conversation per request, as on the device: this sample
                    // answers one question about one image, and keeping history
                    // would only grow the context with unused image tokens.
                    conversation = LiteRtLmNative.ConversationCreate(s_engine, conversationConfig);
                    if (conversation == IntPtr.Zero)
                    {
                        ReportFailure(requestId, "Could not start the conversation");
                        return;
                    }

                    response = LiteRtLmNative.ConversationSendMessage(conversation, messageJson);
                    if (response == IntPtr.Zero)
                    {
                        ReportFailure(requestId, "Inference failed");
                        return;
                    }

                    // Only the native read happens here. JsonUtility is a Unity API,
                    // so the response is parsed back on the main thread.
                    string responseJson = LiteRtLmNative.JsonResponseGetString(response);
                    PostReport(() => CompleteAnalysis(requestId, responseJson));
                }
                catch (DllNotFoundException)
                {
                    PostReport(() => _manager.ReportAnalysis(
                        requestId,
                        LlmPhase.Error,
                        MissingLibraryMessage,
                        null));
                }
                finally
                {
                    if (response != IntPtr.Zero) LiteRtLmNative.JsonResponseDelete(response);
                    if (conversation != IntPtr.Zero) LiteRtLmNative.ConversationDelete(conversation);
                    if (conversationConfig != IntPtr.Zero) LiteRtLmNative.ConversationConfigDelete(conversationConfig);
                    if (thinking != IntPtr.Zero) LiteRtLmNative.ThinkingConfigDelete(thinking);
                    if (sessionConfig != IntPtr.Zero) LiteRtLmNative.SessionConfigDelete(sessionConfig);
                    if (sampler != IntPtr.Zero) LiteRtLmNative.SamplerParamsDelete(sampler);
                }
            });
        }

        private void CompleteAnalysis(int requestId, string responseJson)
        {
            string answer = ExtractText(responseJson);

            if (string.IsNullOrWhiteSpace(answer))
            {
                _manager.ReportAnalysis(
                    requestId,
                    LlmPhase.Error,
                    "The AI model returned an empty answer.",
                    null);
                return;
            }

            _manager.ReportAnalysis(requestId, LlmPhase.Ready, "AI Ready", answer);
        }

        /// <summary>
        /// Creates the engine on one backend. Returns null on success, or the
        /// reason it failed.
        /// </summary>
        private static string TryCreateEngine(string modelPath, string backend)
        {
#if UNITY_EDITOR_WIN
            if (backend == "GPU")
            {
                string missing = LoadDirectXShaderCompiler();
                if (missing != null)
                {
                    return missing;
                }
            }
#endif

            IntPtr settings = IntPtr.Zero;

            try
            {
                settings = LiteRtLmNative.EngineSettingsCreate(modelPath, backend);
                if (settings == IntPtr.Zero)
                {
                    return $"the {backend} settings were rejected{SeeConsole}";
                }

                // The image scenes send exactly one picture per request.
                LiteRtLmNative.EngineSettingsSetMaxNumImages(settings, 1);

                // The GPU backend writes its compiled weight cache here and will
                // not create the folder itself, so loading fails outright when it
                // is missing. The cache is what makes later loads fast.
                string cacheDirectory = Path.Combine(Path.GetTempPath(), "LiteRtLmUnity");
                Directory.CreateDirectory(cacheDirectory);
                LiteRtLmNative.EngineSettingsSetCacheDir(settings, cacheDirectory);

                IntPtr engine = LiteRtLmNative.EngineCreate(settings);
                if (engine == IntPtr.Zero)
                {
                    return $"the model could not be loaded on the {backend}{SeeConsole}";
                }

                s_engine = engine;
                s_engineUsesGpu = backend == "GPU";
                return null;
            }
            catch (DllNotFoundException)
            {
                return MissingLibraryMessage;
            }
            finally
            {
                if (settings != IntPtr.Zero)
                {
                    LiteRtLmNative.EngineSettingsDelete(settings);
                }
            }
        }

        /// <summary>
        /// LiteRT-LM reports nothing through the C API when a call fails; it
        /// writes the reason to its own log, which Unity captures.
        /// </summary>
        private const string SeeConsole = ". See the Console for the LiteRT-LM message.";

#if UNITY_EDITOR_WIN
        private static bool s_shaderCompilerLoaded;

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryW(string fileName);

        /// <summary>
        /// Brings the DirectX Shader Compiler into the process so that the GPU
        /// backend can start. Returns null once both libraries are loaded, or the
        /// reason they are not.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The GPU path runs through Dawn, which compiles its shaders at load
        /// time and reaches for <c>dxcompiler.dll</c> and <c>dxil.dll</c> by bare
        /// name. Windows resolves a bare name against the directory of the
        /// running executable, which here is the Unity installation rather than
        /// this project, so the two are loaded by full path up front instead.
        /// Once a module is loaded under a name, the later bare-name lookup finds
        /// it without searching.
        /// </para>
        /// <para>
        /// Unity ships its own copy of both under <c>Data/Tools</c>, but that
        /// build is older than the shader model Dawn asks for and answers with
        /// <c>invalid profile cs_6_8</c>, so the installer fetches them from the
        /// DirectXShaderCompiler releases instead.
        /// </para>
        /// </remarks>
        private static string LoadDirectXShaderCompiler()
        {
            if (s_shaderCompilerLoaded)
            {
                return null;
            }

            // Order matters: dxcompiler.dll pulls in dxil.dll to sign what it
            // compiles, and an unsigned shader is rejected by the driver.
            foreach (string fileName in new[] { "dxcompiler.dll", "dxil.dll" })
            {
                // Kept in step with EditorNativeLibrarySetup, which puts them
                // here. The two cannot share a constant: that class lives in the
                // Editor assembly, which this one must not reference.
                // Resolved against the working directory, which the Editor keeps
                // at the project root, rather than through Application.dataPath,
                // because this runs on the thread pool and that is a Unity API.
                string path = Path.GetFullPath(
                    Path.Combine("Assets", "Plugins", "x86_64", fileName));

                if (!File.Exists(path) || LoadLibraryW(path) == IntPtr.Zero)
                {
                    return
                        $"the GPU needs {fileName}, which is not installed. Run " +
                        "Tools > LiteRT-LM > Install Editor Native Library";
                }
            }

            s_shaderCompilerLoaded = true;
            return null;
        }
#endif

        private void ReportFailure(int requestId, string message)
        {
            PostReport(() => _manager.ReportAnalysis(
                requestId,
                LlmPhase.Error,
                message + SeeConsole,
                null));
        }

        /// <summary>
        /// Builds the message the C API expects. The shape matches
        /// <c>Content.ImageBytes</c> and <c>Content.Text</c> in the LiteRT-LM
        /// Kotlin API, which is what the Android bridge sends.
        /// </summary>
        private static string BuildMessageJson(string base64Image, string userPrompt)
        {
            var json = new StringBuilder("{\"role\":\"user\",\"content\":[");
            bool needsComma = false;

            if (!string.IsNullOrEmpty(base64Image))
            {
                json.Append("{\"type\":\"image\",\"blob\":\"").Append(base64Image).Append("\"}");
                needsComma = true;
            }

            if (!string.IsNullOrWhiteSpace(userPrompt))
            {
                if (needsComma)
                {
                    json.Append(',');
                }

                json.Append("{\"type\":\"text\",\"text\":");
                AppendJsonString(json, userPrompt);
                json.Append('}');
            }

            return json.Append("]}").ToString();
        }

        private static void AppendJsonString(StringBuilder builder, string value)
        {
            builder.Append('"');

            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        // Everything else, Japanese included, is valid UTF-8 as it
                        // stands; only the C0 controls have to be escaped.
                        if (character < ' ')
                        {
                            builder.Append("\\u").Append(((int)character).ToString("x4"));
                        }
                        else
                        {
                            builder.Append(character);
                        }

                        break;
                }
            }

            builder.Append('"');
        }

        /// <summary>
        /// Reads the answer out of the response message, dropping any thinking
        /// text that survived the thinking configuration.
        /// </summary>
        private static string ExtractText(string responseJson)
        {
            if (string.IsNullOrWhiteSpace(responseJson))
            {
                return string.Empty;
            }

            ResponseMessage message;

            try
            {
                message = JsonUtility.FromJson<ResponseMessage>(responseJson);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not read the model's response: {exception.Message}");
                return string.Empty;
            }

            if (message?.content == null)
            {
                return string.Empty;
            }

            var answer = new StringBuilder();

            foreach (ResponseContent part in message.content)
            {
                if (part != null && part.type == "text")
                {
                    answer.Append(part.text);
                }
            }

            return StripThinking(answer.ToString()).Trim();
        }

        private static string StripThinking(string text)
        {
            if (!text.Contains(ThinkingOpen))
            {
                return text;
            }

            var stripped = new StringBuilder();

            foreach (string part in text.Split(new[] { ThinkingClose }, StringSplitOptions.None))
            {
                int open = part.IndexOf(ThinkingOpen, StringComparison.Ordinal);
                stripped.Append(open >= 0 ? part.Substring(0, open) : part);
            }

            return stripped.ToString();
        }

        private void RunInBackground(Action work)
        {
            Interlocked.Increment(ref s_activeNativeCalls);

            Task.Run(() =>
            {
                try
                {
                    work();
                }
                catch (Exception exception)
                {
                    Post(() => Debug.LogException(exception));
                }
                finally
                {
                    Interlocked.Decrement(ref s_activeNativeCalls);
                }
            });
        }

        /// <summary>Queues work that runs even after the manager has shut down.</summary>
        private void Post(Action work)
        {
            _mainThreadWork.Enqueue(work);
        }

        /// <summary>
        /// Queues a report for the manager. Reports that arrive after the manager
        /// has shut down are dropped, because the request they belong to is gone.
        /// </summary>
        private void PostReport(Action report)
        {
            _mainThreadWork.Enqueue(() =>
            {
                if (!_shutdownRequested)
                {
                    report();
                }
            });
        }

        private static void RegisterCleanup()
        {
            if (s_cleanupRegistered)
            {
                return;
            }

            s_cleanupRegistered = true;
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseEngine;
            EditorApplication.quitting += ReleaseEngine;
        }

        /// <summary>
        /// Frees the loaded model. The Editor never unloads a native plugin, so
        /// without this the memory the engine holds would stay held until the
        /// Editor is quit.
        /// </summary>
        private static void ReleaseEngine()
        {
            // A reload that freed the engine underneath a running native call
            // would crash the Editor. Inference is seconds long, so the wait is
            // short and bounded.
            while (Volatile.Read(ref s_activeNativeCalls) > 0)
            {
                Thread.Sleep(50);
            }

            ReleaseEngineCore();
        }

        private static void ReleaseEngineCore()
        {
            if (s_engine == IntPtr.Zero)
            {
                return;
            }

            try
            {
                LiteRtLmNative.EngineDelete(s_engine);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not release the AI engine cleanly: {exception.Message}");
            }

            s_engine = IntPtr.Zero;
            s_engineModelPath = null;
            s_engineUsesGpu = false;
        }

        [Serializable]
        private sealed class ResponseMessage
        {
            public ResponseContent[] content;
        }

        [Serializable]
        private sealed class ResponseContent
        {
            public string type;
            public string text;
        }
    }
}

#endif
