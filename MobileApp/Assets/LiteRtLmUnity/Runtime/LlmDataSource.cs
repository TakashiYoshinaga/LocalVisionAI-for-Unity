using System;
using R3;

namespace LiteRtLmUnity
{
    public sealed class LlmDataSource : IDisposable
    {
        private readonly Subject<ImageRequest> _imageRequestSubject = new();
        private readonly Subject<string> _resultTextSubject = new();
        private readonly Subject<LlmProgressReport> _progressSubject = new();

        public Observable<ImageRequest> ImageRequests => _imageRequestSubject;
        public Observable<string> ResultText => _resultTextSubject;
        public Observable<LlmProgressReport> ProgressReports => _progressSubject;

        public void PublishImageRequest(ImageRequest request)
        {
            _imageRequestSubject.OnNext(request);
        }

        public void PublishResultText(string resultText)
        {
            _resultTextSubject.OnNext(resultText);
        }

        public void PublishProgress(LlmProgressReport report)
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
