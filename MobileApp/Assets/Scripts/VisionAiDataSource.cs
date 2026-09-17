using UnityEngine;
using R3;
public class VisionAiDataSource
{
    Subject<Texture2D> _imageDtataSubject = new Subject<Texture2D>();
    Subject<string> _rexultTextSubject = new Subject<string>();

    Observable<Texture2D> _imageDataObservable;
    Observable<string> _resultTextObservable;

    public VisionAiDataSource()
    {
        _imageDataObservable = _imageDtataSubject.AsObservable();
        _resultTextObservable = _rexultTextSubject.AsObservable();
    }

    public void SetImageData(Texture2D imageData)
    {
        _imageDtataSubject.OnNext(imageData);
    }

    public void SetResultText(string resultText)
    {
        _rexultTextSubject.OnNext(resultText);
    }
}
