// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

namespace LiteRtLmUnity
{
    /// <summary>
    /// Everything one inference request needs besides the image itself. The
    /// backends take this as a single argument so that adding a setting does not
    /// mean widening a twelve-parameter call on every platform.
    /// </summary>
    public readonly struct LlmRequestOptions
    {
        public string SystemPrompt { get; }
        public string UserPrompt { get; }
        public bool EnableThinking { get; }
        public int ThinkingTokenBudget { get; }

        /// <summary>Maximum answer tokens. 0 or less means no limit.</summary>
        public int AnswerTokenBudget { get; }

        /// <summary>
        /// Candidate token count. 1 is greedy decoding, and 0 or less leaves the
        /// sampler unset so LiteRT-LM falls back to the model's own parameters.
        /// </summary>
        public int TopK { get; }

        public float TopP { get; }
        public float Temperature { get; }
        public int Seed { get; }

        public LlmRequestOptions(
            string systemPrompt,
            string userPrompt,
            bool enableThinking,
            int thinkingTokenBudget,
            int answerTokenBudget,
            int topK,
            float topP,
            float temperature,
            int seed)
        {
            SystemPrompt = systemPrompt ?? string.Empty;
            UserPrompt = userPrompt ?? string.Empty;
            EnableThinking = enableThinking;
            ThinkingTokenBudget = thinkingTokenBudget;
            AnswerTokenBudget = answerTokenBudget;
            TopK = topK;
            TopP = topP;
            Temperature = temperature;
            Seed = seed;
        }
    }
}
