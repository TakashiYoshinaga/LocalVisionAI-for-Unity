using UnityEngine;

/// <summary>
/// Keeps the panel this component is attached to above Android's docked
/// software keyboard while the prompt field has focus. The behaviour belongs
/// to this piece of UI, so it lives here instead of in
/// <see cref="MainCoordinator"/>.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class InputFieldPositionOptimizer : MonoBehaviour
{
    private const float KeyboardMargin = 16f;
    private const float KeyboardMoveDuration = 0.18f;

    [SerializeField] private TMPro.TMP_InputField _userPromptInputField;

    private RectTransform _panelRectTransform;
    private RectTransform _parentRectTransform;
    private Canvas _canvas;
    private Vector2 _restingPosition;
    private float _moveVelocity;
    private bool _initialized;

    private readonly Vector3[] _panelWorldCorners = new Vector3[4];

    private void Awake()
    {
        if (_userPromptInputField == null)
        {
            _userPromptInputField = GetComponentInChildren<TMPro.TMP_InputField>();
        }

        if (_userPromptInputField == null)
        {
            Debug.LogError("InputFieldPositionOptimizer: no user prompt input was found.");
            return;
        }

        _panelRectTransform = (RectTransform)transform;
        _parentRectTransform = _panelRectTransform.parent as RectTransform;
        _canvas = GetComponentInParent<Canvas>();

        if (_parentRectTransform == null || _canvas == null)
        {
            Debug.LogError("InputFieldPositionOptimizer: this panel must sit inside a Canvas RectTransform.");
            return;
        }

        _restingPosition = _panelRectTransform.anchoredPosition;
        _initialized = true;
    }

    private void LateUpdate()
    {
        if (!_initialized)
        {
            return;
        }

        float targetY = _userPromptInputField.isFocused
            ? GetKeyboardAvoidingPanelY()
            : _restingPosition.y;

        Vector2 currentPosition = _panelRectTransform.anchoredPosition;
        currentPosition.y = Mathf.SmoothDamp(
            currentPosition.y,
            targetY,
            ref _moveVelocity,
            KeyboardMoveDuration,
            Mathf.Infinity,
            Time.unscaledDeltaTime);
        _panelRectTransform.anchoredPosition = currentPosition;
    }

    private float GetKeyboardAvoidingPanelY()
    {
        float keyboardTopScreenY = AndroidKeyboardInsetProvider.GetBottomInset();

        if (keyboardTopScreenY <= 0f)
        {
            return _restingPosition.y;
        }

        Camera canvasCamera = _canvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : _canvas.worldCamera;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _parentRectTransform,
                new Vector2(Screen.width * 0.5f, keyboardTopScreenY),
                canvasCamera,
                out Vector2 keyboardTopInParent))
        {
            return _restingPosition.y;
        }

        _panelRectTransform.GetWorldCorners(_panelWorldCorners);
        float panelBottomInParent = _parentRectTransform
            .InverseTransformPoint(_panelWorldCorners[0]).y;
        float requiredMove = keyboardTopInParent.y + KeyboardMargin - panelBottomInParent;

        return Mathf.Max(
            _restingPosition.y,
            _panelRectTransform.anchoredPosition.y + requiredMove);
    }

    private void OnDisable()
    {
        if (!_initialized)
        {
            return;
        }

        _panelRectTransform.anchoredPosition = _restingPosition;
        _moveVelocity = 0f;
    }

    private void OnDestroy()
    {
        AndroidKeyboardInsetProvider.Dispose();
    }
}
