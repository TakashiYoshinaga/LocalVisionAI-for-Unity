// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

using LiteRtLmUnity;
using UnityEngine;

/// <summary>
/// Owns the Scene's UI and the shared <see cref="LlmDataSource"/>. The
/// managers stay free of UI types: this class forwards button clicks to them
/// and applies the values they report back.
/// </summary>
public class ImagePromptCoordinator : MonoBehaviour
{
    private const string CaptureLabel = "Search";
    private const string RetryLabel = "Retry Setup";

    [Header("Logic Managers")]
    [SerializeField] private ImageCaptureManager _imageCaptureManager;
    [SerializeField] private LlmManager _llmManager;
    [SerializeField] private ShowResultManager _showResultManager;
    [Header("UI Elements")]
    [SerializeField] private TMPro.TMP_Text _resultText;
    [SerializeField] private TMPro.TMP_Text _statusText;
    [SerializeField] private UnityEngine.UI.Button _captureButton;
    [SerializeField] private UnityEngine.UI.Button _closeResultButton;
    [SerializeField] private TMPro.TMP_InputField _userPromptInputField;
    [SerializeField] private UnityEngine.UI.ScrollRect _resultScrollRect;
    [SerializeField] private GameObject _resultPanel;

    private readonly LlmDataSource _llmDataSource = new();

    private TMPro.TMP_Text _captureButtonLabel;
    private bool _captureAvailable;
    private bool _retryAvailable;

    private void Start()
    {
        InitializeUI();
#if UNITY_EDITOR
        ShowEditorTestImageBackground();
#endif

        // The display subscribes first so that it also shows whatever the
        // other managers report while they start up.
        _showResultManager.Initialize(_llmDataSource, SetStatusText, SetResultText);
        _imageCaptureManager.Initialize(_llmDataSource, SetCaptureAvailable);
        _llmManager.Initialize(_llmDataSource, SetRetryAvailable);
    }

#if UNITY_EDITOR
    /// <summary>
    /// Puts the still image that stands in for the camera behind the UI while
    /// playing in the Editor, so that what gets analyzed is also what is on
    /// screen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On a device the view behind the UI is <c>ARCameraBackground</c>, which
    /// draws nothing in the Editor because there is no session. The Scenes have
    /// no <c>RawImage</c> to fill instead, and adding one would mean editing
    /// three Scene files by hand, so the image is put into a child of the
    /// Canvas created here and thrown away when play stops.
    /// </para>
    /// <para>
    /// It is inserted as the first child so that every existing element still
    /// draws over it, and it is cropped to cover the way the camera preview in
    /// <c>SimpleMobileApp</c> is.
    /// </para>
    /// </remarks>
    private void ShowEditorTestImageBackground()
    {
        Texture2D testImage = EditorLlmSettings.LoadOrCreate().TestImage;
        if (testImage == null)
        {
            return;
        }

        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            return;
        }

        var holder = new GameObject(
            "Editor Test Image Background",
            typeof(RectTransform),
            typeof(UnityEngine.UI.RawImage));

        var rawImage = holder.GetComponent<UnityEngine.UI.RawImage>();
        rawImage.texture = testImage;
        rawImage.raycastTarget = false;

        var rect = holder.GetComponent<RectTransform>();
        rect.SetParent(canvas.transform, worldPositionStays: false);
        rect.SetAsFirstSibling();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;

        Rect canvasRect = ((RectTransform)canvas.transform).rect;
        float coverScale = Mathf.Max(
            canvasRect.width / testImage.width,
            canvasRect.height / testImage.height);
        rect.sizeDelta = new Vector2(
            testImage.width * coverScale,
            testImage.height * coverScale);
    }
#endif

    private void InitializeUI()
    {
        if (_resultText == null)
        {
            Debug.LogError("ImagePromptCoordinator: the result text is not assigned.");
        }

        if (_statusText == null)
        {
            Debug.LogError("ImagePromptCoordinator: the status text is not assigned.");
        }

        if (_captureButton == null)
        {
            Debug.LogError("ImagePromptCoordinator: the Analyze Camera button is not assigned.");
        }
        else
        {
            _captureButtonLabel = _captureButton.GetComponentInChildren<TMPro.TMP_Text>();
            _captureButton.onClick.AddListener(OnCaptureButtonClicked);
            _captureButton.interactable = false;
        }

        if (_userPromptInputField == null)
        {
            Debug.LogError("ImagePromptCoordinator: the user prompt input is not assigned.");
        }

        if (_resultScrollRect == null)
        {
            Debug.LogError("ImagePromptCoordinator: the result scroll view is not assigned.");
        }

        if (_resultPanel == null)
        {
            Debug.LogError("ImagePromptCoordinator: the result panel is not assigned.");
        }
        else
        {
            _resultPanel.SetActive(false);
        }

        if (_closeResultButton == null)
        {
            Debug.LogError("ImagePromptCoordinator: the close result button is not assigned.");
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
            _llmManager.RetryModelSetup();
            return;
        }

        // Drop the previous answer before the managers report anything, so the
        // camera view is never left sharing the screen with a stale result.
        SetResultText(string.Empty);

        _llmManager.SetUserPrompt(_userPromptInputField != null
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

        _llmManager.Shutdown();
        _llmDataSource.Dispose();
    }
}
