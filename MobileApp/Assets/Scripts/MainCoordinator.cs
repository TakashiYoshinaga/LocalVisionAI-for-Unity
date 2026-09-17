using UnityEngine;

/// <summary>
/// Owns the Scene's UI and the shared <see cref="VisionAiDataSource"/>. The
/// managers stay free of UI types: this class forwards button clicks to them
/// and applies the values they report back.
/// </summary>
public class MainCoordinator : MonoBehaviour
{
    [SerializeField] private ImageCaptureManager _imageCaptureManager;
    [SerializeField] private VisionAiManager _visionAiManager;
    [SerializeField] private ShowResultManager _showResultManager;
    [SerializeField] private TMPro.TMP_Text _resultText;
    [SerializeField] private UnityEngine.UI.Button _captureButton;

    private readonly VisionAiDataSource _visionAiDataSource = new();

    private void Start()
    {
        if (_resultText == null)
        {
            Debug.LogError("MainCoordinator: the result text is not assigned.");
        }

        if (_captureButton == null)
        {
            Debug.LogError("MainCoordinator: the Analyze Camera button is not assigned.");
        }
        else
        {
            _captureButton.onClick.AddListener(OnCaptureButtonClicked);
            _captureButton.interactable = false;
        }

        // The display subscribes first so that it also shows whatever the
        // other managers report while they start up.
        _showResultManager.Initialize(_visionAiDataSource, SetResultText);
        _imageCaptureManager.Initialize(_visionAiDataSource, SetCaptureAvailable);
        _visionAiManager.Initialize(_visionAiDataSource);
    }

    private void OnCaptureButtonClicked()
    {
        _imageCaptureManager.CaptureCameraImage();
    }

    private void SetCaptureAvailable(bool available)
    {
        if (_captureButton != null)
        {
            _captureButton.interactable = available;
        }
    }

    private void SetResultText(string text)
    {
        if (_resultText != null)
        {
            _resultText.text = text;
        }
    }

    private void OnDestroy()
    {
        if (_captureButton != null)
        {
            _captureButton.onClick.RemoveListener(OnCaptureButtonClicked);
        }

        _visionAiManager.Shutdown();
        _visionAiDataSource.Dispose();
    }
}
