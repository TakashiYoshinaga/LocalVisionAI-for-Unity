using System;
using R3;

namespace LiteRtLmUnity
{
    public sealed class VisionAiDataSource : IDisposable
    {
        private readonly Subject<VisionAiRequest> _imageRequestSubject = new();
        private readonly Subject<string> _resultTextSubject = new();
        private readonly Subject<VisionAiProgressReport> _progressSubject = new();

        public Observable<VisionAiRequest> ImageRequests => _imageRequestSubject;
        public Observable<string> ResultText => _resultTextSubject;
        public Observable<VisionAiProgressReport> ProgressReports => _progressSubject;

        public void PublishImageRequest(VisionAiRequest request)
        {
            _imageRequestSubject.OnNext(request);
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
            _imageRequestSubject.Dispose();
            _resultTextSubject.Dispose();
            _progressSubject.Dispose();
        }
    }
}
