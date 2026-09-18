using LiteRtLmUnity;
using UnityEngine;

/// <summary>
/// Owns the Scene's UI and the shared <see cref="VisionAiDataSource"/>. The
/// managers stay free of UI types: this class forwards button clicks to them
/// and applies the values they report back.
/// </summary>
public class MainCoordinator : MonoBehaviour
{
    private const string CaptureLabel = "Search";
    private const string RetryLabel = "Retry Setup";

    [Header("Logic Managers")]
    [SerializeField] private ImageCaptureManager _imageCaptureManager;
    [SerializeField] private VisionAiManager _visionAiManager;
    [SerializeField] private ShowResultManager _showResultManager;
    [Header("UI Elements")]
    [SerializeField] private TMPro.TMP_Text _resultText;
    [SerializeField] private TMPro.TMP_Text _statusText;
    [SerializeField] private UnityEngine.UI.Button _captureButton;
    [SerializeField] private UnityEngine.UI.Button _closeResultButton;
    [SerializeField] private TMPro.TMP_InputField _userPromptInputField;
    [SerializeField] private UnityEngine.UI.ScrollRect _resultScrollRect;
    [SerializeField] private GameObject _resultPanel;

    private readonly VisionAiDataSource _visionAiDataSource = new();

    private TMPro.TMP_Text _captureButtonLabel;
    private bool _captureAvailable;
    private bool _retryAvailable;

    private void Start()
    {
        InitializeUI();

        // The display subscribes first so that it also shows whatever the
        // other managers report while they start up.
        _showResultManager.Initialize(_visionAiDataSource, SetStatusText, SetResultText);
        _imageCaptureManager.Initialize(_visionAiDataSource, SetCaptureAvailable);
        _visionAiManager.Initialize(_visionAiDataSource, SetRetryAvailable);
    }

    private void InitializeUI()
    {
        if (_resultText == null)
        {
            Debug.LogError("MainCoordinator: the result text is not assigned.");
        }

        if (_statusText == null)
        {
            Debug.LogError("MainCoordinator: the status text is not assigned.");
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

        if (_userPromptInputField == null)
        {
            Debug.LogError("MainCoordinator: the user prompt input is not assigned.");
        }

        if (_resultScrollRect == null)
        {
            Debug.LogError("MainCoordinator: the result scroll view is not assigned.");
        }

        if (_resultPanel == null)
        {
            Debug.LogError("MainCoordinator: the result panel is not assigned.");
        }
        else
        {
            _resultPanel.SetActive(false);
        }

        if (_closeResultButton == null)
        {
            Debug.LogError("MainCoordinator: the close result button is not assigned.");
        }
        else
        {
            _closeResultButton.onClick.AddListener(OnCloseResultButtonClicked);
        }
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

        // Drop the previous answer before the managers report anything, so the
        // camera view is never left sharing the screen with a stale result.
        SetResultText(string.Empty);

        _visionAiManager.SetUserPrompt(_userPromptInputField != null
            ? _userPromptInputField.text
            : string.Empty);
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

            if (_resultPanel != null)
            {
                _resultPanel.SetActive(!string.IsNullOrEmpty(text));
            }

            if (_resultScrollRect != null && !string.IsNullOrEmpty(text))
            {
                Canvas.ForceUpdateCanvases();
                _resultScrollRect.verticalNormalizedPosition = 1f;
            }
        }
    }

    private void SetStatusText(string text)
    {
        if (_statusText != null)
        {
            _statusText.text = text;
        }
    }

    private void OnCloseResultButtonClicked()
    {
        SetResultText(string.Empty);
    }

    private void OnDestroy()
    {
        if (_captureButton != null)
        {
            _captureButton.onClick.RemoveListener(OnCaptureButtonClicked);
        }

        if (_closeResultButton != null)
        {
            _closeResultButton.onClick.RemoveListener(OnCloseResultButtonClicked);
        }

        _visionAiManager.Shutdown();
        _visionAiDataSource.Dispose();
    }
}
