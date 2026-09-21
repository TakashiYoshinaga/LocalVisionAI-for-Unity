// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

using System;
using R3;
using UnityEngine;

namespace LiteRtLmUnity
{
    public class LlmManager : MonoBehaviour
    {
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
        [Tooltip("How many candidates the next word is picked from. 1 always takes the most likely one, so the same input tends to give the same answer and Top P, Temperature and Seed stop mattering. 0 or less leaves this to LiteRT-LM.")]
        [SerializeField] private int _topK = DefaultTopK;
        [Tooltip("Discards unlikely candidates, keeping only the top ones that together account for this share of the probability. Used only when Top K is 2 or more.")]
        [SerializeField, Range(0f, 1f)] private float _topP = DefaultTopP;
        [Tooltip("Higher values make the wording more varied, lower values more predictable. Used only when Top K is 2 or more.")]
        [SerializeField, Min(0f)] private float _temperature = DefaultTemperature;
        [Tooltip("Starts each request from a new random draw, so asking the same question again can give a different answer. Used only when Top K is 2 or more.")]
        [SerializeField] private bool _randomizeSeed = true;
        [Tooltip("A fixed starting point for those random draws, so the same question comes back with the same answer. Used only when Randomize Seed is off and Top K is 2 or more.")]
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
        private ILlmBackend _backend;
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
            _backend = CreateBackend();
            _imageRequestSubscription?.Dispose();
            _imageRequestSubscription = _dataSource.ImageRequests
                .ObserveOnMainThread()
                .Subscribe(AnalyzeImage);
            RetryModelSetup();
        }

        /// <summary>
        /// Picks how inference runs. The device uses the Kotlin bridge this
        /// package ships; the Editor talks to the LiteRT-LM C API directly so that
        /// prompts can be tried without a Build and Run each time.
        /// </summary>
        private ILlmBackend CreateBackend()
        {
#if UNITY_EDITOR_OSX || UNITY_EDITOR_WIN
            return new EditorLlmBackend(this);
#elif UNITY_EDITOR
            return new UnsupportedLlmBackend(
                this,
                "Running the model in the Editor currently needs the macOS or Windows " +
                "LiteRT-LM library. Build and run on an Android device instead.");
#elif UNITY_ANDROID
            return new AndroidLlmBackend(this);
#else
            return new UnsupportedLlmBackend(this, "This sample requires an Android device.");
#endif
        }

        private void Update()
        {
            // Backends that work on another thread deliver their results here.
            _backend?.Pump();

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
                _dataSource == null ||
                _backend == null)
            {
                return;
            }

            _modelSetupInProgress = true;
            _onRetryAvailabilityChanged?.Invoke(false);
            _backend.PrepareModel();
        }

        /// <summary>
        /// Entry point for the Android bridge, which answers through
        /// UnitySendMessage and therefore can only pass a string.
        /// </summary>
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

            ReportModelProgress(
                phase,
                callback.message,
                callback.hasProgress ? callback.progress01 : null,
                callback.ready,
                callback.retryable,
                callback.modelPath);
        }

        /// <summary>Entry point for the Android bridge. See <see cref="OnBundledModelProgress"/>.</summary>
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

            ReportEngineProgress(phase, callback.message, callback.ready, callback.retryable);
        }

        /// <summary>Entry point for the Android bridge. See <see cref="OnBundledModelProgress"/>.</summary>
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

            ReportAnalysis(callback.requestId, phase, callback.message, callback.resultText);
        }

        /// <summary>
        /// How a backend reports on making the model file available. Both the
        /// device's extraction step and the Editor's file lookup end here.
        /// </summary>
        internal void ReportModelProgress(
            LlmPhase phase,
            string message,
            float? progress,
            bool ready,
            bool retryable,
            string modelPath)
        {
            _dataSource?.PublishProgress(new LlmProgressReport(phase, message, progress));

            if (ready)
            {
                PreparedModelPath = modelPath;
                _modelSetupInProgress = false;
                _onRetryAvailabilityChanged?.Invoke(false);
                StartEngineInitialization();
            }
            else if (phase == LlmPhase.Error)
            {
                _modelSetupInProgress = false;
                _onRetryAvailabilityChanged?.Invoke(retryable);
            }
            else
            {
                _onRetryAvailabilityChanged?.Invoke(false);
            }
        }

        /// <summary>How a backend reports on loading the model into an engine.</summary>
        internal void ReportEngineProgress(
            LlmPhase phase,
            string message,
            bool ready,
            bool retryable)
        {
            _dataSource?.PublishProgress(new LlmProgressReport(phase, message));

            if (phase == LlmPhase.Ready && ready)
            {
                IsReady = true;
                _engineInitializationInProgress = false;
                _onRetryAvailabilityChanged?.Invoke(false);
            }
            else if (phase == LlmPhase.Error)
            {
                IsReady = false;
                _engineInitializationInProgress = false;
                _onRetryAvailabilityChanged?.Invoke(retryable);
            }
        }

        /// <summary>How a backend reports the outcome of one inference request.</summary>
        internal void ReportAnalysis(
            int requestId,
            LlmPhase phase,
            string message,
            string resultText)
        {
            if (requestId != _activeRequestId)
            {
                return;
            }

            if (phase == LlmPhase.Inferencing)
            {
                _dataSource?.PublishProgress(new LlmProgressReport(phase, message));
                return;
            }

            _inferenceInProgress = false;
            EndAnalysisTracking();

            if (phase == LlmPhase.Error)
            {
                PublishError(message);
                return;
            }

            // Report Ready before the answer so the UI re-enables its capture
            // button first and the answer stays as the last text on screen.
            _dataSource?.PublishProgress(new LlmProgressReport(LlmPhase.Ready, message));
            _dataSource?.PublishResultText(resultText);
        }

        /// <summary>
        /// How a backend reports that a call could not even be started. Whatever
        /// was in progress is cancelled, because nothing will report on it.
        /// </summary>
        internal void ReportError(string message)
        {
            _modelSetupInProgress = false;
            _engineInitializationInProgress = false;

            if (_inferenceInProgress)
            {
                _inferenceInProgress = false;
                EndAnalysisTracking();
            }

            PublishError(message);
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

            string effectiveUserPrompt = string.IsNullOrWhiteSpace(_userPrompt) &&
                                         string.IsNullOrWhiteSpace(_systemPrompt)
                ? _fallbackImagePrompt
                : _userPrompt;

            _inferenceInProgress = true;
            _activeInferenceKind = InferenceKind.Image;
            _activeRequestId = ++_lastRequestId;
            BeginAnalysisTracking();
            _dataSource.PublishProgress(new LlmProgressReport(
                LlmPhase.Inferencing,
                "Analyzing image... 0s"));

            _backend.Analyze(
                _activeRequestId,
                request.JpegData,
                BuildRequestOptions(effectiveUserPrompt));
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

            _inferenceInProgress = true;
            _activeInferenceKind = InferenceKind.Text;
            _activeRequestId = ++_lastRequestId;
            BeginAnalysisTracking();
            _dataSource.PublishProgress(new LlmProgressReport(
                LlmPhase.Inferencing,
                "Generating response... 0s"));

            _backend.AnalyzeText(_activeRequestId, BuildRequestOptions(userPrompt));
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
            _backend?.Shutdown();
        }

        private void StartEngineInitialization()
        {
            if (_backend == null)
            {
                return;
            }

            _engineInitializationInProgress = true;
            IsReady = false;
            _backend.InitializeEngine(PreparedModelPath);
        }

        private LlmRequestOptions BuildRequestOptions(string userPrompt)
        {
            return new LlmRequestOptions(
                _systemPrompt,
                userPrompt,
                _enableThinking,
                _thinkingTokenBudget,
                _answerTokenBudget,
                _topK,
                _topP,
                _temperature,
                ResolveSeed());
        }

        /// <summary>
        /// Picks the seed for one request. A fresh seed per request is what makes
        /// the same question answer differently, because every request starts a new
        /// conversation and therefore restarts the sampler's random sequence.
        /// The seed never matters while Top K is 1, which is greedy decoding.
        /// </summary>
        private int ResolveSeed()
        {
            return _randomizeSeed
                ? UnityEngine.Random.Range(1, int.MaxValue)
                : _seed;
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
