using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Owns the device camera, displays its live preview, and provides an upright
/// snapshot when another component needs to send the current frame to the AI.
/// </summary>
public sealed class CameraImageManager : MonoBehaviour
{
    private const int RequestedWidth = 1280;
    private const int RequestedHeight = 720;
    private const int RequestedFps = 30;

    [SerializeField] private UnityEngine.UI.RawImage _previewImage;
    [SerializeField] private bool _preferFrontFacingCamera;

    private WebCamTexture _webCamTexture;
    private RectTransform _previewRect;
    private RectTransform _previewParentRect;
    private bool _isReady;

    public event Action<bool> AvailabilityChanged;

    public bool IsReady => _isReady &&
                           _webCamTexture != null &&
                           _webCamTexture.isPlaying &&
                           _webCamTexture.width > 16;

    private IEnumerator Start()
    {
        Screen.orientation = ScreenOrientation.Portrait;

        yield return RequestCameraPermission();

        if (!HasCameraPermission())
        {
            Debug.LogError("CameraImageManager: camera permission was not granted.");
            yield break;
        }

        WebCamDevice[] devices = WebCamTexture.devices;
        if (devices.Length == 0)
        {
            Debug.LogError("CameraImageManager: no camera device is available.");
            yield break;
        }

        WebCamDevice selectedDevice = devices[0];
        foreach (WebCamDevice device in devices)
        {
            if (device.isFrontFacing == _preferFrontFacingCamera)
            {
                selectedDevice = device;
                break;
            }
        }

        _webCamTexture = new WebCamTexture(
            selectedDevice.name,
            RequestedWidth,
            RequestedHeight,
            RequestedFps);

        if (_previewImage != null)
        {
            _previewImage.texture = _webCamTexture;
            _previewImage.raycastTarget = false;
            _previewRect = _previewImage.rectTransform;
            _previewParentRect = _previewRect.parent as RectTransform;
        }
        else
        {
            Debug.LogWarning("CameraImageManager: preview image is not assigned.");
        }

        _webCamTexture.Play();

        float initializationDeadline = Time.realtimeSinceStartup + 10f;
        while (_webCamTexture.isPlaying &&
               _webCamTexture.width <= 16 &&
               Time.realtimeSinceStartup < initializationDeadline)
        {
            yield return null;
        }

        SetReady(_webCamTexture.isPlaying && _webCamTexture.width > 16);
        if (!_isReady)
        {
            Debug.LogError("CameraImageManager: camera initialization timed out.");
        }
        UpdatePreviewLayout();
    }

    private void LateUpdate()
    {
        if (_webCamTexture == null || !_webCamTexture.isPlaying)
        {
            return;
        }

        UpdatePreviewLayout();

        if (!_isReady && _webCamTexture.didUpdateThisFrame && _webCamTexture.width > 16)
        {
            SetReady(true);
        }
    }

    /// <summary>
    /// Copies the latest camera frame into a readable, portrait-oriented texture.
    /// The caller owns the returned texture and must destroy it.
    /// </summary>
    public bool TryCaptureFrame(out Texture2D frame)
    {
        frame = null;

        if (!IsReady)
        {
            return false;
        }

        int sourceWidth = _webCamTexture.width;
        int sourceHeight = _webCamTexture.height;
        Color32[] sourcePixels = _webCamTexture.GetPixels32();
        int clockwiseRotation = NormalizeRotation(_webCamTexture.videoRotationAngle);
        bool quarterTurn = clockwiseRotation == 90 || clockwiseRotation == 270;
        int targetWidth = quarterTurn ? sourceHeight : sourceWidth;
        int targetHeight = quarterTurn ? sourceWidth : sourceHeight;
        var targetPixels = new Color32[sourcePixels.Length];

        for (int sourceY = 0; sourceY < sourceHeight; sourceY++)
        {
            int sampledY = _webCamTexture.videoVerticallyMirrored
                ? sourceHeight - 1 - sourceY
                : sourceY;

            for (int sourceX = 0; sourceX < sourceWidth; sourceX++)
            {
                int targetX;
                int targetY;

                switch (clockwiseRotation)
                {
                    case 90:
                        targetX = sourceY;
                        targetY = sourceWidth - 1 - sourceX;
                        break;
                    case 180:
                        targetX = sourceWidth - 1 - sourceX;
                        targetY = sourceHeight - 1 - sourceY;
                        break;
                    case 270:
                        targetX = sourceHeight - 1 - sourceY;
                        targetY = sourceX;
                        break;
                    default:
                        targetX = sourceX;
                        targetY = sourceY;
                        break;
                }

                targetPixels[targetY * targetWidth + targetX] =
                    sourcePixels[sampledY * sourceWidth + sourceX];
            }
        }

        frame = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        frame.SetPixels32(targetPixels);
        frame.Apply(false, false);
        return true;
    }

    private void UpdatePreviewLayout()
    {
        if (_previewRect == null ||
            _previewParentRect == null ||
            _webCamTexture.width <= 16)
        {
            return;
        }

        int clockwiseRotation = NormalizeRotation(_webCamTexture.videoRotationAngle);
        bool quarterTurn = clockwiseRotation == 90 || clockwiseRotation == 270;
        float rotatedWidth = quarterTurn ? _webCamTexture.height : _webCamTexture.width;
        float rotatedHeight = quarterTurn ? _webCamTexture.width : _webCamTexture.height;
        Rect parentRect = _previewParentRect.rect;
        float coverScale = Mathf.Max(
            parentRect.width / rotatedWidth,
            parentRect.height / rotatedHeight);

        _previewRect.anchorMin = new Vector2(0.5f, 0.5f);
        _previewRect.anchorMax = new Vector2(0.5f, 0.5f);
        _previewRect.pivot = new Vector2(0.5f, 0.5f);
        _previewRect.anchoredPosition = Vector2.zero;
        _previewRect.sizeDelta = new Vector2(
            _webCamTexture.width * coverScale,
            _webCamTexture.height * coverScale);
        _previewRect.localEulerAngles = new Vector3(0f, 0f, -clockwiseRotation);
        _previewImage.uvRect = _webCamTexture.videoVerticallyMirrored
            ? new Rect(0f, 1f, 1f, -1f)
            : new Rect(0f, 0f, 1f, 1f);
    }

    private static int NormalizeRotation(int degrees)
    {
        return ((degrees % 360) + 360) % 360;
    }

    private static bool HasCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return UnityEngine.Android.Permission.HasUserAuthorizedPermission(
            UnityEngine.Android.Permission.Camera);
#elif UNITY_IOS && !UNITY_EDITOR
        return Application.HasUserAuthorization(UserAuthorization.WebCam);
#else
        return true;
#endif
    }

    private static IEnumerator RequestCameraPermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Camera))
        {
            bool requestCompleted = false;
            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += _ => requestCompleted = true;
            callbacks.PermissionDenied += _ => requestCompleted = true;
            callbacks.PermissionDeniedAndDontAskAgain += _ => requestCompleted = true;
            UnityEngine.Android.Permission.RequestUserPermission(
                UnityEngine.Android.Permission.Camera,
                callbacks);
            while (!requestCompleted)
            {
                yield return null;
            }
        }
#elif UNITY_IOS && !UNITY_EDITOR
        if (!Application.HasUserAuthorization(UserAuthorization.WebCam))
        {
            yield return Application.RequestUserAuthorization(UserAuthorization.WebCam);
        }
#else
        yield return null;
#endif
    }

    private void SetReady(bool ready)
    {
        if (_isReady == ready)
        {
            return;
        }

        _isReady = ready;
        AvailabilityChanged?.Invoke(ready);
    }

    private void OnDestroy()
    {
        SetReady(false);

        if (_webCamTexture != null)
        {
            if (_webCamTexture.isPlaying)
            {
                _webCamTexture.Stop();
            }

            Destroy(_webCamTexture);
        }
    }
}
