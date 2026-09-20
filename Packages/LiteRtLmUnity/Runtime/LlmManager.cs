// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

using System;
using R3;
using UnityEngine;

namespace LiteRtLmUnity
{
    public class LlmManager : MonoBehaviour
    {
        private const string AndroidBridgeClass =
            "com.takashiyoshinaga.localvisionai.BundledModelBridge";
        private const string ModelAssetPath = BundledModelPaths.AndroidAssetPath;
        private const string ModelFileName = BundledModelPaths.FileName;
        private const int DefaultAnswerTokenBudget = 512;
        private const int DefaultTopK = 1;
        private const float DefaultTopP = 0.95f;
        private const float DefaultTemperature = 1.0f;

        [Header("Thinking")]
        [Tooltip("Applied at the start of each inference. Changing it never reloads the engine.")]
        [SerializeField] private bool _enableThinking;
        [SerializeField] private int _thinkingTokenBudget = 256;

        [Header("Answer")]
        [Tooltip("Maximum answer tokens. 0 or less means no limit.")]
        [SerializeField] private int _answerTokenBudget = DefaultAnswerTokenBudget;

        [Header("Sampling")]
        // These mirror the LiteRT-LM defaults for this model, so the shipped
        // values reproduce the behavior this sample had before they existed.
        [Tooltip("Number of candidate tokens. 1 is greedy decoding and ignores Top P and Temperature. 0 or less leaves the sampler to LiteRT-LM.")]
        [SerializeField] private int _topK = DefaultTopK;
        [Tooltip("Cumulative probability cutoff. Used only when Top K is 2 or more.")]
        [SerializeField, Range(0f, 1f)] private float _topP = DefaultTopP;
        [Tooltip("Higher values make the answer more varied, lower values more repeatable. Used only when Top K is 2 or more.")]
        [SerializeField, Min(0f)] private float _temperature = DefaultTemperature;
        [Tooltip("Random seed for sampling. Used only when Top K is 2 or more.")]
        [SerializeField] private int _seed;

        [Header("Analysis Monitoring")]
        [Tooltip("Show a long-running warning after this many seconds.")]
        [SerializeField, Min(1)] private int _slowAnalysisWarningSeconds = 30;
        [Tooltip("Show a timeout warning after this many seconds. The native inference remains locked until it returns.")]
        [SerializeField, Min(1)] private int _analysisTimeoutSeconds = 120;

        [Header("Prompt")]
        [SerializeField, TextArea(3, 10)] private string _systemPrompt = "";
        [SerializeField, TextArea(3, 10)] private string _userPrompt = "";
        // Last resort for image analysis only: used when the System Prompt and
        // the User Prompt are BOTH blank. A System Prompt on its own is a valid
        // setup (see 0-VisionAI-SystemPromptOnly), so a blank User Prompt alone
        // never reaches this value.
        [Tooltip("Used only when the System Prompt and the User Prompt are both empty.")]
        [SerializeField, TextArea(2, 5)] private string _fallbackImagePrompt =
            "Describe what is visible in this image clearly and concisely.";

        private LlmDataSource _dataSource;
        private Action<bool> _onRetryAvailabilityChanged;
        private IDisposable _imageRequestSubscription;
        private bool _modelSetupInProgress;
        private bool _engineInitializationInProgress;
        private bool _inferenceInProgress;
        private InferenceKind _activeInferenceKind = InferenceKind.Image;
        private int _lastRequestId;
        private int _activeRequestId;
        private float _analysisStartedAt;
        private int _lastReportedAnalysisSecond = -1;
        private bool _slowAnalysisWarningLogged;
        private bool _analysisTimeoutLogged;

        public string PreparedModelPath { get; private set; }
        public bool IsReady { get; private set; }

        public void SetUserPrompt(string prompt)
        {
            _userPrompt = prompt ?? string.Empty;
        }

        /// <summary>
        /// Reports whether the failed setup can be retried through
        /// <paramref name="onRetryAvailabilityChanged"/>. The caller owns the UI.
        /// </summary>
        public void Initialize(
            LlmDataSource llmDataSource,
            Action<bool> onRetryAvailabilityChanged)
        {
            _dataSource = llmDataSource;
            _onRetryAvailabilityChanged = onRetryAvailabilityChanged;
            _onRetryAvailabilityChanged?.Invoke(false);
            _imageRequestSubscription?.Dispose();
            _imageRequestSubscription = _dataSource.ImageRequests
                .ObserveOnMainThread()
                .Subscribe(AnalyzeImage);
            RetryModelSetup();
        }

        private void Update()
        {
            if (!_inferenceInProgress || _dataSource == null)
            {
                return;
            }

            int elapsedSeconds = Mathf.Max(
                0,
                Mathf.FloorToInt(Time.realtimeSinceStartup - _analysisStartedAt));
            if (elapsedSeconds == _lastReportedAnalysisSecond)
            {
                return;
            }

            _lastReportedAnalysisSecond = elapsedSeconds;
            string message;

            if (elapsedSeconds >= _analysisTimeoutSeconds)
            {
                message = _activeInferenceKind == InferenceKind.Text
                    ? $"Response generation exceeded {_analysisTimeoutSeconds}s ({elapsedSeconds}s). Restart the app if it does not finish."
                    : $"Analysis exceeded {_analysisTimeoutSeconds}s ({elapsedSeconds}s). Restart the app if it does not finish.";

                if (!_analysisTimeoutLogged)
                {
                    _analysisTimeoutLogged = true;
                    string operation = _activeInferenceKind == InferenceKind.Text
                        ? "Text generation"
                        : "Image analysis";
                    Debug.LogError(
                        $"{operation} request {_activeRequestId} exceeded the " +
                        $"{_analysisTimeoutSeconds}s time limit. The native call is still running.");
                }
            }
            else if (elapsedSeconds >= _slowAnalysisWarningSeconds)
            {
                message = _activeInferenceKind == InferenceKind.Text
                    ? $"Still generating... {elapsedSeconds}s"
                    : $"Still analyzing... {elapsedSeconds}s";

                if (!_slowAnalysisWarningLogged)
                {
                    _slowAnalysisWarningLogged = true;
                    string operation = _activeInferenceKind == InferenceKind.Text
                        ? "Text generation"
                        : "Image analysis";
                    Debug.LogWarning(
                        $"{operation} request {_activeRequestId} is still running " +
                        $"after {_slowAnalysisWarningSeconds}s.");
                }
            }
            else
            {
                message = _activeInferenceKind == InferenceKind.Text
                    ? $"Generating response... {elapsedSeconds}s"
                    : $"Analyzing image... {elapsedSeconds}s";
            }

            _dataSource.PublishProgress(new LlmProgressReport(
                LlmPhase.Inferencing,
                message));
        }

        public void RetryModelSetup()
        {
            if (_modelSetupInProgress ||
                _engineInitializationInProgress ||
                _dataSource == null)
            {
                return;
            }

    #if UNITY_ANDROID && !UNITY_EDITOR
            _modelSetupInProgress = true;
            _onRetryAvailabilityChanged?.Invoke(false);
            _dataSource.PublishProgress(new LlmProgressReport(
                LlmPhase.ExtractingModel,
                "Preparing bundled AI model...",
                0f));

            try
            {
                using AndroidJavaClass bridge = new(AndroidBridgeClass);
                bridge.CallStatic(
                    "prepareBundledModel",
                    gameObject.name,
                    ModelAssetPath,
                    ModelFileName);
            }
            catch (Exception exception)
            {
                _modelSetupInProgress = false;
                PublishError($"Could not start model setup: {exception.Message}");
            }
    #else
            PublishError("Bundled model setup requires an Android device.");
    #endif
        }

        public void OnBundledModelProgress(string json)
        {
            BundledModelCallback callback;

            try
            {
                callback = JsonUtility.FromJson<BundledModelCallback>(json);
            }
            catch (Exception exception)
            {
                _modelSetupInProgress = false;
                PublishError($"Invalid model setup response: {exception.Message}");
                return;
            }

            if (callback == null ||
                !Enum.TryParse(callback.phase, out LlmPhase phase))
            {
                _modelSetupInProgress = false;
                PublishError("Invalid model setup response.");
                return;
            }

            float? progress = callback.hasProgress
                ? callback.progress01
                : null;

            _dataSource?.PublishProgress(new LlmProgressReport(
                phase,
                callback.message,
                progress));

            if (callback.ready)
            {
                PreparedModelPath = callback.modelPath;
                _modelSetupInProgress = false;
                _onRetryAvailabilityChanged?.Invoke(false);
                StartEngineInitialization();
            }
            else if (phase == LlmPhase.Error)
            {
                _modelSetupInProgress = false;
                _onRetryAvailabilityChanged?.Invoke(callback.retryable);
            }
            else
            {
                _onRetryAvailabilityChanged?.Invoke(false);
            }
        }

        public void OnEngineProgress(string json)
        {
            BundledModelCallback callback;

            try
            {
                callback = JsonUtility.FromJson<BundledModelCallback>(json);
            }
            catch (Exception exception)
            {
                _engineInitializationInProgress = false;
                PublishError($"Invalid engine response: {exception.Message}");
                return;
            }

            if (callback == null ||
                !Enum.TryParse(callback.phase, out LlmPhase phase))
            {
                _engineInitializationInProgress = false;
                PublishError("Invalid engine response.");
                return;
            }

            _dataSource?.PublishProgress(new LlmProgressReport(
                phase,
                callback.message));

            if (phase == LlmPhase.Ready && callback.ready)
            {
                IsReady = true;
                _engineInitializationInProgress = false;
                _onRetryAvailabilityChanged?.Invoke(false);
            }
            else if (phase == LlmPhase.Error)
            {
                IsReady = false;
                _engineInitializationInProgress = false;
                _onRetryAvailabilityChanged?.Invoke(callback.retryable);
            }
        }

        private void AnalyzeImage(ImageRequest request)
        {
            if (request == null || _dataSource == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_systemPrompt) &&
                string.IsNullOrWhiteSpace(_userPrompt) &&
                string.IsNullOrWhiteSpace(_fallbackImagePrompt))
            {
                PublishError("No prompt is configured for image analysis.");
                return;
            }

            if (!IsReady)
            {
                PublishError("The AI engine is not ready yet.");
                return;
            }

            if (_inferenceInProgress)
            {
                return;
            }

    #if UNITY_ANDROID && !UNITY_EDITOR
            _inferenceInProgress = true;
            _activeInferenceKind = InferenceKind.Image;
            _activeRequestId = ++_lastRequestId;
            BeginAnalysisTracking();
            _dataSource.PublishProgress(new LlmProgressReport(
                LlmPhase.Inferencing,
                "Analyzing image... 0s"));

            try
            {
                string effectiveUserPrompt = string.IsNullOrWhiteSpace(_userPrompt) &&
                                             string.IsNullOrWhiteSpace(_systemPrompt)
                    ? _fallbackImagePrompt
                    : _userPrompt;

                using AndroidJavaClass bridge = new(AndroidBridgeClass);

                // AndroidJavaClass.CallStatic cannot express named arguments, so
                // these values must stay in the same order as
                // BundledModelBridge.analyze in BundledModelBridge.kt.
                bridge.CallStatic(
                    // Static Kotlin method to invoke.
                    "analyze",
                    // Name of the GameObject that hosts this LlmManager.
                    // Kotlin uses UnitySendMessage to invoke its callback methods.
                    gameObject.name,
                    // Identifies this request so stale callbacks can be ignored.
                    _activeRequestId,
                    // Captured camera image encoded as JPEG bytes.
                    request.JpegData,
                    // Conversation-level instruction that controls model behavior.
                    _systemPrompt,
                    // Per-request question or instruction entered by the user.
                    effectiveUserPrompt,
                    // Whether the model may generate an internal thinking channel.
                    _enableThinking,
                    // Maximum tokens allocated to the internal thinking channel.
                    _thinkingTokenBudget,
                    // Maximum answer tokens; 0 or less uses the model default.
                    _answerTokenBudget,
                    // Candidate token count; 1 is greedy, 0 or less uses the
                    // LiteRT-LM default.
                    _topK,
                    // Cumulative probability cutoff.
                    _topP,
                    // Logit scaling; higher values vary the answer more.
                    _temperature,
                    // Random seed for sampling.
                    _seed);
            }
            catch (Exception exception)
            {
                _inferenceInProgress = false;
                EndAnalysisTracking();
                PublishError($"Could not start image analysis: {exception.Message}");
            }
    #else
            PublishError("Image analysis requires an Android device.");
    #endif
        }

        /// <summary>
        /// Runs one text-only request. The System Prompt belongs to this manager;
        /// callers provide only the per-request User Prompt.
        /// </summary>
        public void AnalyzeText(string userPrompt)
        {
            if (_dataSource == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_systemPrompt))
            {
                PublishError("The System Prompt is not configured.");
                return;
            }

            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                PublishError("Enter a User Prompt before sending.");
                return;
            }

            if (!IsReady)
            {
                PublishError("The AI engine is not ready yet.");
                return;
            }

            if (_inferenceInProgress)
            {
                return;
            }

    #if UNITY_ANDROID && !UNITY_EDITOR
            _inferenceInProgress = true;
            _activeInferenceKind = InferenceKind.Text;
            _activeRequestId = ++_lastRequestId;
            BeginAnalysisTracking();
            _dataSource.PublishProgress(new LlmProgressReport(
                LlmPhase.Inferencing,
                "Generating response... 0s"));

            try
            {
                using AndroidJavaClass bridge = new(AndroidBridgeClass);

                // Keep these values in the same order as
                // BundledModelBridge.analyzeText in BundledModelBridge.kt.
                bridge.CallStatic(
                    "analyzeText",
                    gameObject.name,
                    _activeRequestId,
                    _systemPrompt,
                    userPrompt,
                    _enableThinking,
                    _thinkingTokenBudget,
                    _answerTokenBudget,
                    _topK,
                    _topP,
                    _temperature,
                    _seed);
            }
            catch (Exception exception)
            {
                _inferenceInProgress = false;
                EndAnalysisTracking();
                PublishError($"Could not start text generation: {exception.Message}");
            }
    #else
            PublishError("Text generation requires an Android device.");
    #endif
        }

        public void OnAnalysisProgress(string json)
        {
            AnalysisCallback callback;

            try
            {
                callback = JsonUtility.FromJson<AnalysisCallback>(json);
            }
            catch (Exception exception)
            {
                _inferenceInProgress = false;
                EndAnalysisTracking();
                PublishError($"Invalid analysis response: {exception.Message}");
                return;
            }

            if (callback == null ||
                !Enum.TryParse(callback.phase, out LlmPhase phase))
            {
                _inferenceInProgress = false;
                EndAnalysisTracking();
                PublishError("Invalid analysis response.");
                return;
            }

            if (callback.requestId != _activeRequestId)
            {
                return;
            }

            if (phase == LlmPhase.Inferencing)
            {
                _dataSource?.PublishProgress(new LlmProgressReport(
                    phase,
                    callback.message));
                return;
            }

            _inferenceInProgress = false;
            EndAnalysisTracking();

            if (phase == LlmPhase.Error)
            {
                PublishError(callback.message);
                return;
            }

            // Report Ready before the answer so the UI re-enables its capture
            // button first and the answer stays as the last text on screen.
            _dataSource?.PublishProgress(new LlmProgressReport(
                LlmPhase.Ready,
                callback.message));
            _dataSource?.PublishResultText(callback.resultText);
        }

        public void Shutdown()
        {
            _imageRequestSubscription?.Dispose();
            _imageRequestSubscription = null;
            _modelSetupInProgress = false;
            _engineInitializationInProgress = false;
            _inferenceInProgress = false;
            EndAnalysisTracking();
            IsReady = false;

    #if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using AndroidJavaClass bridge = new(AndroidBridgeClass);
                bridge.CallStatic("shutdown");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not shut down the AI engine cleanly: {exception.Message}");
            }
    #endif
        }

        private void StartEngineInitialization()
        {
    #if UNITY_ANDROID && !UNITY_EDITOR
            if (string.IsNullOrEmpty(PreparedModelPath))
            {
                PublishError("Prepared AI model path is missing.");
                return;
            }

            _engineInitializationInProgress = true;
            IsReady = false;
            _dataSource?.PublishProgress(new LlmProgressReport(
                LlmPhase.Initializing,
                "Initializing AI engine..."));

            try
            {
                using AndroidJavaClass bridge = new(AndroidBridgeClass);
                bridge.CallStatic(
                    "initialize",
                    gameObject.name,
                    PreparedModelPath);
            }
            catch (Exception exception)
            {
                _engineInitializationInProgress = false;
                PublishError($"Could not start AI initialization: {exception.Message}");
            }
    #endif
        }

        private void PublishError(string message)
        {
            _dataSource?.PublishProgress(new LlmProgressReport(
                LlmPhase.Error,
                message));
        }

        private void BeginAnalysisTracking()
        {
            _analysisStartedAt = Time.realtimeSinceStartup;
            _lastReportedAnalysisSecond = -1;
            _slowAnalysisWarningLogged = false;
            _analysisTimeoutLogged = false;

            string operation = _activeInferenceKind == InferenceKind.Text
                ? "Text generation"
                : "Image analysis";
            Debug.Log(
                $"{operation} request {_activeRequestId} started " +
                $"(thinking={_enableThinking}, thinkingTokens={_thinkingTokenBudget}, " +
                $"answerTokens={_answerTokenBudget}).");
        }

        private void EndAnalysisTracking()
        {
            _lastReportedAnalysisSecond = -1;
            _slowAnalysisWarningLogged = false;
            _analysisTimeoutLogged = false;
        }

        [Serializable]
        private sealed class AnalysisCallback
        {
            public string phase;
            public string message;
            public int requestId;
            public string resultText;
        }

        [Serializable]
        private sealed class BundledModelCallback
        {
            public string phase;
            public string message;
            public bool hasProgress;
            public float progress01;
            public bool ready;
            public bool retryable;
            public string modelPath;
        }

        private enum InferenceKind
        {
            Image,
            Text
        }
    }
}
