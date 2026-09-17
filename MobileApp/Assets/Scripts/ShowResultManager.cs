using System;
using R3;
using UnityEngine;

public class ShowResultManager : MonoBehaviour
{
    private IDisposable _progressSubscription;
    private IDisposable _resultSubscription;

    /// <summary>
    /// Formats progress and results into the text to show, and hands it to
    /// <paramref name="onDisplayTextChanged"/>. The caller owns the UI, so this
    /// class never touches a UI type.
    /// </summary>
    public void Initialize(
        VisionAiDataSource visionAiDataSource,
        Action<string> onDisplayTextChanged)
    {
        _progressSubscription?.Dispose();
        _resultSubscription?.Dispose();

        if (onDisplayTextChanged == null)
        {
            Debug.LogError("ShowResultManager was initialized without a text sink.");
            return;
        }

        _progressSubscription = visionAiDataSource.ProgressReports
            .Subscribe(report => onDisplayTextChanged(FormatProgress(report)));
        _resultSubscription = visionAiDataSource.ResultText
            .Subscribe(result => onDisplayTextChanged(result));
    }

    private static string FormatProgress(VisionAiProgressReport report)
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
