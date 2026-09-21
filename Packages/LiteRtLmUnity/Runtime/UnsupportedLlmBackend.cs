// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

namespace LiteRtLmUnity
{
    /// <summary>
    /// Stands in on platforms that have neither the Android bridge nor an Editor
    /// library, so that the manager's state machine is the same everywhere and a
    /// missing backend shows up as a message on screen rather than as silence.
    /// </summary>
    internal sealed class UnsupportedLlmBackend : ILlmBackend
    {
        private readonly LlmManager _manager;
        private readonly string _reason;

        public UnsupportedLlmBackend(LlmManager manager, string reason)
        {
            _manager = manager;
            _reason = reason;
        }

        public void PrepareModel() => _manager.ReportError(_reason);

        public void InitializeEngine(string preparedModelPath) => _manager.ReportError(_reason);

        public void Analyze(int requestId, byte[] jpegData, LlmRequestOptions options) =>
            _manager.ReportError(_reason);

        public void AnalyzeText(int requestId, LlmRequestOptions options) =>
            _manager.ReportError(_reason);

        public void Pump()
        {
        }

        public void Shutdown()
        {
        }
    }
}
