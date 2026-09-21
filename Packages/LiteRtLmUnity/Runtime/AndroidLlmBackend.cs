// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

#if UNITY_ANDROID && !UNITY_EDITOR

using System;
using UnityEngine;

namespace LiteRtLmUnity
{
    /// <summary>
    /// Runs inference on the device through the Kotlin bridge shipped with this
    /// package. The Kotlin side answers by calling
    /// <c>UnitySendMessage(callbackGameObject, ...)</c>, which lands on the public
    /// <c>On*Progress</c> methods of <see cref="LlmManager"/>, so this class only
    /// ever sends.
    /// </summary>
    internal sealed class AndroidLlmBackend : ILlmBackend
    {
        private const string BridgeClass =
            "com.takashiyoshinaga.localvisionai.BundledModelBridge";

        private readonly LlmManager _manager;
        private readonly string _callbackGameObject;

        public AndroidLlmBackend(LlmManager manager)
        {
            _manager = manager;
            _callbackGameObject = manager.gameObject.name;
        }

        public void PrepareModel()
        {
            _manager.ReportModelProgress(
                LlmPhase.ExtractingModel,
                "Preparing bundled AI model...",
                0f,
                ready: false,
                retryable: false,
                modelPath: null);

            try
            {
                using AndroidJavaClass bridge = new(BridgeClass);
                bridge.CallStatic(
                    "prepareBundledModel",
                    _callbackGameObject,
                    BundledModelPaths.AndroidAssetPath,
                    BundledModelPaths.FileName);
            }
            catch (Exception exception)
            {
                _manager.ReportError($"Could not start model setup: {exception.Message}");
            }
        }

        public void InitializeEngine(string preparedModelPath)
        {
            if (string.IsNullOrEmpty(preparedModelPath))
            {
                _manager.ReportError("Prepared AI model path is missing.");
                return;
            }

            _manager.ReportEngineProgress(
                LlmPhase.Initializing,
                "Initializing AI engine...",
                ready: false,
                retryable: false);

            try
            {
                using AndroidJavaClass bridge = new(BridgeClass);
                bridge.CallStatic("initialize", _callbackGameObject, preparedModelPath);
            }
            catch (Exception exception)
            {
                _manager.ReportError($"Could not start AI initialization: {exception.Message}");
            }
        }

        public void Analyze(int requestId, byte[] jpegData, LlmRequestOptions options)
        {
            try
            {
                using AndroidJavaClass bridge = new(BridgeClass);

                // AndroidJavaClass.CallStatic cannot express named arguments, so
                // these values must stay in the same order as
                // BundledModelBridge.analyze in BundledModelBridge.kt.
                bridge.CallStatic(
                    // Static Kotlin method to invoke.
                    "analyze",
                    // Name of the GameObject that hosts the LlmManager.
                    // Kotlin uses UnitySendMessage to invoke its callback methods.
                    _callbackGameObject,
                    // Identifies this request so stale callbacks can be ignored.
                    requestId,
                    // Captured camera image encoded as JPEG bytes.
                    jpegData,
                    // Conversation-level instruction that controls model behavior.
                    options.SystemPrompt,
                    // Per-request question or instruction entered by the user.
                    options.UserPrompt,
                    // Whether the model may generate an internal thinking channel.
                    options.EnableThinking,
                    // Maximum tokens allocated to the internal thinking channel.
                    options.ThinkingTokenBudget,
                    // Maximum answer tokens; 0 or less uses the model default.
                    options.AnswerTokenBudget,
                    // Candidate token count; 1 is greedy, 0 or less uses the
                    // LiteRT-LM default.
                    options.TopK,
                    // Cumulative probability cutoff.
                    options.TopP,
                    // Logit scaling; higher values vary the answer more.
                    options.Temperature,
                    // Random seed for sampling.
                    options.Seed);
            }
            catch (Exception exception)
            {
                _manager.ReportError($"Could not start image analysis: {exception.Message}");
            }
        }

        public void AnalyzeText(int requestId, LlmRequestOptions options)
        {
            try
            {
                using AndroidJavaClass bridge = new(BridgeClass);

                // Keep these values in the same order as
                // BundledModelBridge.analyzeText in BundledModelBridge.kt.
                bridge.CallStatic(
                    "analyzeText",
                    _callbackGameObject,
                    requestId,
                    options.SystemPrompt,
                    options.UserPrompt,
                    options.EnableThinking,
                    options.ThinkingTokenBudget,
                    options.AnswerTokenBudget,
                    options.TopK,
                    options.TopP,
                    options.Temperature,
                    options.Seed);
            }
            catch (Exception exception)
            {
                _manager.ReportError($"Could not start text generation: {exception.Message}");
            }
        }

        public void Pump()
        {
            // UnitySendMessage already arrives on the main thread.
        }

        public void Shutdown()
        {
            try
            {
                using AndroidJavaClass bridge = new(BridgeClass);
                bridge.CallStatic("shutdown");
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not shut down the AI engine cleanly: {exception.Message}");
            }
        }
    }
}

#endif
