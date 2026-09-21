// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

namespace LiteRtLmUnity
{
    /// <summary>
    /// One way of running the model. <see cref="LlmManager"/> owns the UI-facing
    /// state machine and delegates every native call here, so the Android device
    /// path and the in-Editor desktop path never have to know about each other.
    /// </summary>
    /// <remarks>
    /// Every method returns immediately. Backends report what happened by calling
    /// the <c>Report*</c> methods on the manager they were constructed with, always
    /// from the main thread.
    /// </remarks>
    internal interface ILlmBackend
    {
        /// <summary>
        /// Makes the model file available and reports its path. On Android this
        /// extracts the bundled parts out of the APK; in the Editor the file is
        /// already on disk, so this only locates and verifies it.
        /// </summary>
        void PrepareModel();

        /// <summary>Loads the prepared model into an inference engine.</summary>
        void InitializeEngine(string preparedModelPath);

        void Analyze(int requestId, byte[] jpegData, LlmRequestOptions options);

        void AnalyzeText(int requestId, LlmRequestOptions options);

        /// <summary>
        /// Called once per frame from <see cref="LlmManager"/>. Backends that do
        /// their work on another thread use this to deliver results on the main
        /// thread; the Android backend, which is called back by UnitySendMessage,
        /// does nothing here.
        /// </summary>
        void Pump();

        void Shutdown();
    }
}
