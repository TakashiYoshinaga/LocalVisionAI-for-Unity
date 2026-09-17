using System;
using R3;
using UnityEngine;

public sealed class VisionAiDataSource : IDisposable
{
    private readonly Subject<Texture2D> _imageDataSubject = new();
    private readonly Subject<string> _resultTextSubject = new();
    private readonly Subject<VisionAiProgressReport> _progressSubject = new();

    public Observable<Texture2D> ImageData => _imageDataSubject;
    public Observable<string> ResultText => _resultTextSubject;
    public Observable<VisionAiProgressReport> ProgressReports => _progressSubject;

    public void PublishImageData(Texture2D imageData)
    {
        _imageDataSubject.OnNext(imageData);
    }

    public void PublishResultText(string resultText)
    {
        _resultTextSubject.OnNext(resultText);
    }

    public void PublishProgress(VisionAiProgressReport report)
    {
        _progressSubject.OnNext(report);
    }

    public void Dispose()
    {
        _imageDataSubject.Dispose();
        _resultTextSubject.Dispose();
        _progressSubject.Dispose();
    }
}
