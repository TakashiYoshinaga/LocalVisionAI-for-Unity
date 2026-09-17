using UnityEngine;

public class MainCoordinator : MonoBehaviour
{
    [SerializeField] private ImageCaptureManager _imageCaptureManager;
    [SerializeField] private VisionAiManager _visionAiManager;
    [SerializeField] private ShowResultManager _showResultManager;
    [SerializeField] private TMPro.TMP_Text _resultText;

    private readonly VisionAiDataSource _visionAiDataSource = new();

    private void Start()
    {
        _imageCaptureManager.Initialize(_visionAiDataSource);
        _showResultManager.Initialize(_visionAiDataSource, _resultText);
        _visionAiManager.Initialize(_visionAiDataSource);
    }

    private void OnDestroy()
    {
        _visionAiManager.Shutdown();
        _visionAiDataSource.Dispose();
    }
}
