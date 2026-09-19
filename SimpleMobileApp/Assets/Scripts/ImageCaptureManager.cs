using System;
using LiteRtLmUnity;
using UnityEngine;
using R3;

public class ImageCaptureManager : MonoBehaviour
{
    private const int MaxImageDimension = 1024;
    private const int JpegQuality = 85;

    [SerializeField] private CameraImageManager _cameraImageManager;

    private LlmDataSource _dataSource;
    private Action<bool> _onCaptureAvailabilityChanged;
    private bool _captureInProgress;
    private bool _inferenceInProgress;
    private bool _aiReady;
    private IDisposable _progressSubscription;

    /// <summary>
    /// Reports whether capturing is currently allowed through
    /// <paramref name="onCaptureAvailabilityChanged"/>. The caller owns the UI
    /// and decides how to reflect that, so this class never touches a UI type.
    /// </summary>
    public void Initialize(
        LlmDataSource llmDataSource,
        Action<bool> onCaptureAvailabilityChanged)
    {
        _dataSource = llmDataSource;
        _onCaptureAvailabilityChanged = onCaptureAvailabilityChanged;

        if (_cameraImageManager != null)
        {
            _cameraImageManager.AvailabilityChanged -= OnCameraAvailabilityChanged;
            _cameraImageManager.AvailabilityChanged += OnCameraAvailabilityChanged;
        }

        NotifyCaptureAvailability();
        _progressSubscription?.Dispose();
        _progressSubscription = _dataSource.ProgressReports
            .ObserveOnMainThread()
            .Subscribe(HandleProgressReport);
    }

    /// <summary>Captures one frame. Ignored unless the AI is idle and ready.</summary>
    public void CaptureCameraImage()
    {
        if (!_aiReady || _inferenceInProgress)
        {
            return;
        }

        if (_captureInProgress)
        {
            return;
        }

        if (_cameraImageManager == null)
        {
            PublishError("Camera Image Manager is not configured.");
            return;
        }

        if (!_cameraImageManager.TryCaptureFrame(out Texture2D cameraFrame))
        {
            PublishError("Camera image is not available yet. Try again.");
            return;
        }

        _captureInProgress = true;
        _onCaptureAvailabilityChanged?.Invoke(false);
        _dataSource?.PublishProgress(new LlmProgressReport(
            LlmPhase.Capturing,
            "Capturing camera image..."));

        Texture2D resizedTexture = null;

        try
        {
            Vector2Int outputDimensions = GetScaledDimensions(
                cameraFrame.width,
                cameraFrame.height);
            Texture2D requestTexture = cameraFrame;

            if (outputDimensions.x != cameraFrame.width ||
                outputDimensions.y != cameraFrame.height)
            {
                resizedTexture = ResizeTexture(cameraFrame, outputDimensions);
                requestTexture = resizedTexture;
            }

            byte[] jpegData = requestTexture.EncodeToJPG(JpegQuality);
            var request = new ImageRequest(
                jpegData,
                requestTexture.width,
                requestTexture.height);

            // ImageRequests is marshalled to the next main-thread frame. Lock
            // capture immediately so the button cannot briefly re-enable.
            _inferenceInProgress = true;
            _dataSource?.PublishImageRequest(request);
        }
        catch (Exception exception)
        {
            PublishError($"Could not capture camera image: {exception.Message}");
        }
        finally
        {
            if (resizedTexture != null)
            {
                Destroy(resizedTexture);
            }

            if (cameraFrame != null)
            {
                Destroy(cameraFrame);
            }

            _captureInProgress = false;
            NotifyCaptureAvailability();
        }
    }

    private void HandleProgressReport(LlmProgressReport report)
    {
        switch (report.Phase)
        {
            case LlmPhase.Ready:
                _aiReady = true;
                _inferenceInProgress = false;
                break;
            case LlmPhase.ExtractingModel:
            case LlmPhase.Initializing:
                _aiReady = false;
                _inferenceInProgress = false;
                break;
            case LlmPhase.Inferencing:
                _inferenceInProgress = true;
                break;
            case LlmPhase.Error:
                _inferenceInProgress = false;
                break;
        }

        NotifyCaptureAvailability();
    }

    private void NotifyCaptureAvailability()
    {
        if (_captureInProgress)
        {
            return;
        }

        bool cameraReady = _cameraImageManager != null && _cameraImageManager.IsReady;
        _onCaptureAvailabilityChanged?.Invoke(
            _aiReady && cameraReady && !_inferenceInProgress);
    }

    private static Vector2Int GetScaledDimensions(int width, int height)
    {
        int largestDimension = Mathf.Max(width, height);

        if (largestDimension <= MaxImageDimension)
        {
            return new Vector2Int(width, height);
        }

        float scale = MaxImageDimension / (float)largestDimension;
        return new Vector2Int(
            Mathf.Max(1, Mathf.RoundToInt(width * scale)),
            Mathf.Max(1, Mathf.RoundToInt(height * scale)));
    }

    private static Texture2D ResizeTexture(Texture2D source, Vector2Int dimensions)
    {
        RenderTexture temporary = RenderTexture.GetTemporary(
            dimensions.x,
            dimensions.y,
            0,
            RenderTextureFormat.ARGB32,
            RenderTextureReadWrite.Linear);
        RenderTexture previous = RenderTexture.active;

        try
        {
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            var resized = new Texture2D(
                dimensions.x,
                dimensions.y,
                TextureFormat.RGBA32,
                false);
            resized.ReadPixels(
                new Rect(0, 0, dimensions.x, dimensions.y),
                0,
                0,
                false);
            resized.Apply(false, false);
            return resized;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
        }
    }

    private void OnCameraAvailabilityChanged(bool _)
    {
        NotifyCaptureAvailability();
    }

    private void PublishError(string message)
    {
        _dataSource?.PublishProgress(new LlmProgressReport(
            LlmPhase.Error,
            message));
    }

    private void OnDestroy()
    {
        if (_cameraImageManager != null)
        {
            _cameraImageManager.AvailabilityChanged -= OnCameraAvailabilityChanged;
        }

        _progressSubscription?.Dispose();
    }
}
