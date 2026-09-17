using System;
using R3;
using TMPro;
using UnityEngine;

public class ShowResultManager : MonoBehaviour
{
    private IDisposable _progressSubscription;
    private IDisposable _resultSubscription;

    public void Initialize(
        VisionAiDataSource visionAiDataSource,
        TMP_Text resultText)
    {
        _progressSubscription?.Dispose();
        _resultSubscription?.Dispose();

        _progressSubscription = visionAiDataSource.ProgressReports
            .Subscribe(report => resultText.text = FormatProgress(report));
        _resultSubscription = visionAiDataSource.ResultText
            .Subscribe(result => resultText.text = result);
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
