// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

using System;
using System.Text;
using R3;
using UnityEngine;

namespace LiteRtLmUnity
{
    public class ShowResultManager : MonoBehaviour
    {
        private IDisposable _progressSubscription;
        private IDisposable _resultSubscription;

        /// <summary>
        /// Formats progress and results for their separate text sinks. The caller
        /// owns the UI, so this class never touches a UI type.
        /// </summary>
        public void Initialize(
            LlmDataSource llmDataSource,
            Action<string> onStatusTextChanged,
            Action<string> onResultTextChanged)
        {
            _progressSubscription?.Dispose();
            _resultSubscription?.Dispose();

            if (onStatusTextChanged == null || onResultTextChanged == null)
            {
                Debug.LogError("ShowResultManager was initialized without all text sinks.");
                return;
            }

            _progressSubscription = llmDataSource.ProgressReports
                .ObserveOnMainThread()
                .Subscribe(report => onStatusTextChanged(FormatProgress(report)));
            _resultSubscription = llmDataSource.ResultText
                .ObserveOnMainThread()
                .Subscribe(result => onResultTextChanged(ToDisplayText(result)));
        }

        /// <summary>
        /// The model answers in Markdown, which TextMesh Pro shows verbatim. Emphasis
        /// markers are dropped and list markers become bullets. Nothing is converted
        /// to rich text tags, so model output can never be read as TMP markup.
        /// </summary>
        private static string ToDisplayText(string result)
        {
            if (string.IsNullOrEmpty(result))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(result.Length);

            foreach (string line in result.Split('\n'))
            {
                string trimmed = line.TrimStart();
                string indent = line.Substring(0, line.Length - trimmed.Length);

                if (trimmed.StartsWith("* ") || trimmed.StartsWith("- "))
                {
                    builder.Append(indent).Append("\u2022 ").Append(trimmed, 2, trimmed.Length - 2);
                }
                else if (trimmed.StartsWith("#"))
                {
                    builder.Append(indent).Append(trimmed.TrimStart('#').TrimStart());
                }
                else
                {
                    builder.Append(line);
                }

                builder.Append('\n');
            }

            return builder
                .Replace("**", string.Empty)
                .ToString()
                .TrimEnd('\n');
        }

        private static string FormatProgress(LlmProgressReport report)
        {
            return report.Progress01.HasValue
                ? $"{report.Message} {report.Progress01.Value:P0}"
                : report.Message;
        }

        private void OnDestroy()
        {
            _progressSubscription?.Dispose();
            _resultSubscription?.Dispose();
        }
    }
}
