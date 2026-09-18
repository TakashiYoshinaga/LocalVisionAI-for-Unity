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
    private const float KeyboardMargin = 16f;
    private const float KeyboardMoveDuration = 0.18f;

    [Header("Logic Managers")]
    [SerializeField] private ImageCaptureManager _imageCaptureManager;
    [SerializeField] private VisionAiManager _visionAiManager;
    [SerializeField] private ShowResultManager _showResultManager;
    [Header("UI Elements")]
    [SerializeField] private TMPro.TMP_Text _resultText;
    [SerializeField] private UnityEngine.UI.Button _captureButton;
    [SerializeField] private TMPro.TMP_InputField _userPromptInputField;
    [SerializeField] private UnityEngine.UI.ScrollRect _resultScrollRect;

    private readonly VisionAiDataSource _visionAiDataSource = new();

    private TMPro.TMP_Text _captureButtonLabel;
    private RectTransform _inputPanelRectTransform;
    private RectTransform _inputPanelParentRectTransform;
    private Canvas _inputPanelCanvas;
    private Vector2 _inputPanelRestingPosition;
    private float _inputPanelMoveVelocity;
    private bool _inputPanelPositionInitialized;
    private bool _captureAvailable;
    private bool _retryAvailable;

    private readonly Vector3[] _inputPanelWorldCorners = new Vector3[4];

    private void Start()
    {
        InitializeUI();

        // The display subscribes first so that it also shows whatever the
        // other managers report while they start up.
        _showResultManager.Initialize(_visionAiDataSource, SetResultText);
        _imageCaptureManager.Initialize(_visionAiDataSource, SetCaptureAvailable);
        _visionAiManager.Initialize(_visionAiDataSource, SetRetryAvailable);
    }

    private void InitializeUI()
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

        if (_userPromptInputField == null)
        {
            Debug.LogError("MainCoordinator: the user prompt input is not assigned.");
        }
        else
        {
            InitializeInputPanelPosition();
        }

        if (_resultScrollRect == null)
        {
            Debug.LogError("MainCoordinator: the result scroll view is not assigned.");
        }
    }

    private void InitializeInputPanelPosition()
    {
        _inputPanelRectTransform = _userPromptInputField.transform.parent as RectTransform;

        if (_inputPanelRectTransform == null)
        {
            Debug.LogError("MainCoordinator: the user prompt input must be inside an InputPanel RectTransform.");
            return;
        }

        _inputPanelParentRectTransform = _inputPanelRectTransform.parent as RectTransform;
        _inputPanelCanvas = _inputPanelRectTransform.GetComponentInParent<Canvas>();

        if (_inputPanelParentRectTransform == null || _inputPanelCanvas == null)
        {
            Debug.LogError("MainCoordinator: the InputPanel must be inside a Canvas RectTransform.");
            return;
        }

        _inputPanelRestingPosition = _inputPanelRectTransform.anchoredPosition;
        _inputPanelPositionInitialized = true;
    }

    private void LateUpdate()
    {
        if (!_inputPanelPositionInitialized)
        {
            return;
        }

        float targetY = _inputPanelRestingPosition.y;

        if (_userPromptInputField.isFocused)
        {
            targetY = GetKeyboardAvoidingPanelY();
        }

        Vector2 currentPosition = _inputPanelRectTransform.anchoredPosition;
        currentPosition.y = Mathf.SmoothDamp(
            currentPosition.y,
            targetY,
            ref _inputPanelMoveVelocity,
            KeyboardMoveDuration,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
        _inputPanelRectTransform.anchoredPosition = currentPosition;
    }

    private float GetKeyboardAvoidingPanelY()
    {
        float keyboardTopScreenY = AndroidKeyboardInsetProvider.GetBottomInset();

        if (keyboardTopScreenY <= 0f)
        {
            return _inputPanelRestingPosition.y;
        }

        Camera canvasCamera = _inputPanelCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _inputPanelCanvas.worldCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _inputPanelParentRectTransform,
                new Vector2(Screen.width * 0.5f, keyboardTopScreenY),
                canvasCamera,
                out Vector2 keyboardTopInParent))
        {
            return _inputPanelRestingPosition.y;
        }

        _inputPanelRectTransform.GetWorldCorners(_inputPanelWorldCorners);
        float panelBottomInParent = _inputPanelParentRectTransform
            .InverseTransformPoint(_inputPanelWorldCorners[0]).y;
        float requiredMove = keyboardTopInParent.y + KeyboardMargin - panelBottomInParent;

        return Mathf.Max(
            _inputPanelRestingPosition.y,
            _inputPanelRectTransform.anchoredPosition.y + requiredMove);
    }

    private void OnDisable()
    {
        if (!_inputPanelPositionInitialized)
        {
            return;
        }

        _inputPanelRectTransform.anchoredPosition = _inputPanelRestingPosition;
        _inputPanelMoveVelocity = 0f;
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

            if (_resultScrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                _resultScrollRect.verticalNormalizedPosition = 1f;
            }
        }
    }

    private void OnDestroy()
    {
        AndroidKeyboardInsetProvider.Dispose();

        if (_captureButton != null)
        {
            _captureButton.onClick.RemoveListener(OnCaptureButtonClicked);
        }

        _visionAiManager.Shutdown();
        _visionAiDataSource.Dispose();
    }
}
