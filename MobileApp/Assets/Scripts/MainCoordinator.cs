using UnityEngine;

/// <summary>
/// Owns the Scene's UI and the shared <see cref="VisionAiDataSource"/>. The
/// managers stay free of UI types: this class forwards button clicks to them
/// and applies the values they report back.
/// </summary>
public class MainCoordinator : MonoBehaviour
{
    private const string CaptureLabel = "Analyze Camera";
    private const string RetryLabel = "Retry Setup";

    [SerializeField] private ImageCaptureManager _imageCaptureManager;
    [SerializeField] private VisionAiManager _visionAiManager;
    [SerializeField] private ShowResultManager _showResultManager;
    [SerializeField] private TMPro.TMP_Text _resultText;
    [SerializeField] private UnityEngine.UI.Button _captureButton;

    private readonly VisionAiDataSource _visionAiDataSource = new();

    private TMPro.TMP_Text _captureButtonLabel;
    private bool _captureAvailable;
    private bool _retryAvailable;

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
            _captureButtonLabel = _captureButton.GetComponentInChildren<TMPro.TMP_Text>();
            _captureButton.onClick.AddListener(OnCaptureButtonClicked);
            _captureButton.interactable = false;
        }

        // The display subscribes first so that it also shows whatever the
        // other managers report while they start up.
        _showResultManager.Initialize(_visionAiDataSource, SetResultText);
        _imageCaptureManager.Initialize(_visionAiDataSource, SetCaptureAvailable);
        _visionAiManager.Initialize(_visionAiDataSource, SetRetryAvailable);
    }

    /// <summary>
    /// The single button does double duty: it retries a failed setup when one
    /// can be retried, and captures otherwise.
    /// </summary>
    private void OnCaptureButtonClicked()
    {
        if (_retryAvailable)
        {
            _visionAiManager.RetryModelSetup();
            return;
        }

        _imageCaptureManager.CaptureCameraImage();
    }

    private void SetCaptureAvailable(bool available)
    {
        _captureAvailable = available;
        ApplyButtonState();
    }

    private void SetRetryAvailable(bool available)
    {
        _retryAvailable = available;
        ApplyButtonState();
    }

    private void ApplyButtonState()
    {
        if (_captureButton == null)
        {
            return;
        }

        _captureButton.interactable = _retryAvailable || _captureAvailable;

        if (_captureButtonLabel != null)
        {
            _captureButtonLabel.text = _retryAvailable ? RetryLabel : CaptureLabel;
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
