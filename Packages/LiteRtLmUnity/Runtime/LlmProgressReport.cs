// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

using UnityEngine;

namespace LiteRtLmUnity
{
    public readonly struct LlmProgressReport
    {
        public LlmPhase Phase { get; }
        public string Message { get; }
        public float? Progress01 { get; }

        public LlmProgressReport(
            LlmPhase phase,
            string message,
            float? progress01 = null)
        {
            Phase = phase;
            Message = message ?? string.Empty;
            Progress01 = progress01.HasValue
                ? Mathf.Clamp01(progress01.Value)
                : null;
        }
    }
}
