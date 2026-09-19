using System;
using LiteRtLmUnity;
using R3;
using UnityEngine;

/// <summary>
/// Owns the text-only sample's UI and shared data source. Each send is an
/// independent request; conversation history is intentionally not retained.
/// </summary>
public sealed class TextPromptCoordinator : MonoBehaviour
{
    private const string SendLabel = "Send";
    private const string RetryLabel = "Retry Setup";

    [Header("Logic Managers")]
    [SerializeField] private VisionAiManager _visionAiManager;
    [SerializeField] private ShowResultManager _showResultManager;

    [Header("UI Elements")]
    [SerializeField] private TMPro.TMP_Text _resultText;
    [SerializeField] private TMPro.TMP_Text _statusText;
    [SerializeField] private UnityEngine.UI.Button _sendButton;
    [SerializeField] private UnityEngine.UI.Button _closeResultButton;
    [SerializeField] private TMPro.TMP_InputField _userPromptInputField;
    [SerializeField] private UnityEngine.UI.ScrollRect _resultScrollRect;
    [SerializeField] private GameObject _resultPanel;

    private readonly VisionAiDataSource _visionAiDataSource = new();

    private IDisposable _progressSubscription;
    private TMPro.TMP_Text _sendButtonLabel;
    private bool _engineReady;
    private bool _inferenceInProgress;
    private bool _retryAvailable;

    private void Start()
    {
        InitializeUI();

        // Subscribe before the AI manager starts setup so the initial state is
        // reflected in the button as well as in the status label.
        _showResultManager.Initialize(
            _visionAiDataSource,
            SetStatusText,
            SetResultText);
        _progressSubscription = _visionAiDataSource.ProgressReports
            .ObserveOnMainThread()
            .Subscribe(OnProgressReported);
        _visionAiManager.Initialize(
            _visionAiDataSource,
            SetRetryAvailable);
    }

    private void InitializeUI()
    {
        if (_resultText == null)
        {
            Debug.LogError("TextPromptCoordinator: the result text is not assigned.");
        }

        if (_statusText == null)
        {
            Debug.LogError("TextPromptCoordinator: the status text is not assigned.");
        }

        if (_sendButton == null)
        {
            Debug.LogError("TextPromptCoordinator: the Send button is not assigned.");
        }
        else
        {
            _sendButtonLabel = _sendButton.GetComponentInChildren<TMPro.TMP_Text>();
            _sendButton.onClick.AddListener(OnSendButtonClicked);
            _sendButton.interactable = false;
        }

        if (_userPromptInputField == null)
        {
            Debug.LogError("TextPromptCoordinator: the User Prompt input is not assigned.");
        }
        else
        {
            _userPromptInputField.onValueChanged.AddListener(OnUserPromptChanged);
        }

        if (_resultScrollRect == null)
        {
            Debug.LogError("TextPromptCoordinator: the result scroll view is not assigned.");
        }

        if (_resultPanel == null)
        {
            Debug.LogError("TextPromptCoordinator: the result panel is not assigned.");
        }
        else
        {
            _resultPanel.SetActive(false);
        }

        if (_closeResultButton == null)
        {
            Debug.LogError("TextPromptCoordinator: the close result button is not assigned.");
        }
        else
        {
            _closeResultButton.onClick.AddListener(OnCloseResultButtonClicked);
        }
    }

    private void OnSendButtonClicked()
    {
        if (_retryAvailable)
        {
            _visionAiManager.RetryModelSetup();
            return;
        }

        string userPrompt = _userPromptInputField != null
            ? _userPromptInputField.text
            : string.Empty;
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            ApplyButtonState();
            return;
        }

        SetResultText(string.Empty);
        _visionAiManager.AnalyzeText(userPrompt);
    }

    private void OnUserPromptChanged(string _)
    {
        ApplyButtonState();
    }

    private void OnProgressReported(VisionAiProgressReport report)
    {
        switch (report.Phase)
        {
            case VisionAiPhase.Ready:
                _engineReady = true;
                _inferenceInProgress = false;
                break;

            case VisionAiPhase.Inferencing:
                _inferenceInProgress = true;
                break;

            case VisionAiPhase.ExtractingModel:
            case VisionAiPhase.Initializing:
                _engineReady = false;
                _inferenceInProgress = false;
                break;

            case VisionAiPhase.Error:
                _engineReady = _visionAiManager != null && _visionAiManager.IsReady;
                _inferenceInProgress = false;
                break;

            case VisionAiPhase.Capturing:
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        ApplyButtonState();
    }

    private void SetRetryAvailable(bool available)
    {
        _retryAvailable = available;
        ApplyButtonState();
    }

    private void ApplyButtonState()
    {
        if (_sendButton == null)
        {
            return;
        }

        bool hasUserPrompt = _userPromptInputField != null &&
                             !string.IsNullOrWhiteSpace(_userPromptInputField.text);
        _sendButton.interactable = _retryAvailable ||
                                   (_engineReady &&
                                    !_inferenceInProgress &&
                                    hasUserPrompt);

        if (_sendButtonLabel != null)
        {
            _sendButtonLabel.text = _retryAvailable ? RetryLabel : SendLabel;
        }
    }

    private void SetResultText(string text)
    {
        if (_resultText == null)
        {
            return;
        }

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
        if (_sendButton != null)
        {
            _sendButton.onClick.RemoveListener(OnSendButtonClicked);
        }

        if (_closeResultButton != null)
        {
            _closeResultButton.onClick.RemoveListener(OnCloseResultButtonClicked);
        }

        if (_userPromptInputField != null)
        {
            _userPromptInputField.onValueChanged.RemoveListener(OnUserPromptChanged);
        }

        _progressSubscription?.Dispose();
        _visionAiManager?.Shutdown();
        _visionAiDataSource.Dispose();
    }
}
