using System;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using R3;

public class ImageCaptureManager : MonoBehaviour
{
    private const int MaxImageDimension = 1024;
    private const int JpegQuality = 85;
    private const string DefaultPrompt =
        "Describe what is visible in this image clearly and concisely.";

    [SerializeField] private ARCameraManager _cameraManager;

    private VisionAiDataSource _dataSource;
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
        VisionAiDataSource visionAiDataSource,
        Action<bool> onCaptureAvailabilityChanged)
    {
        _dataSource = visionAiDataSource;
        _onCaptureAvailabilityChanged = onCaptureAvailabilityChanged;

        _onCaptureAvailabilityChanged?.Invoke(false);
        _progressSubscription = _dataSource.ProgressReports
            .Subscribe(HandleProgressReport);
    }

    /// <summary>Captures one frame. Ignored unless the AI is idle and ready.</summary>
    public void CaptureCameraImage()
    {
        if (!_aiReady || _inferenceInProgress)
        {
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                UnityEngine.Android.Permission.Camera))
        {
            PublishError("Camera permission is required.");
            return;
        }
#endif

        if (_captureInProgress)
        {
            return;
        }

        if (_cameraManager == null)
        {
            PublishError("AR Camera Manager is not configured.");
            return;
        }

        if (!_cameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
        {
            PublishError("Camera image is not available yet. Try again.");
            return;
        }

        _captureInProgress = true;
        _onCaptureAvailabilityChanged?.Invoke(false);
        _dataSource?.PublishProgress(new VisionAiProgressReport(
            VisionAiPhase.Capturing,
            "Capturing camera image..."));

        NativeArray<byte> convertedData = default;
        Texture2D convertedTexture = null;
        Texture2D orientedTexture = null;

        try
        {
            Vector2Int outputDimensions = GetScaledDimensions(
                cpuImage.width,
                cpuImage.height);
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = outputDimensions,
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            convertedData = new NativeArray<byte>(
                cpuImage.GetConvertedDataSize(conversionParams),
                Allocator.Temp);
            cpuImage.Convert(conversionParams, convertedData);

            convertedTexture = new Texture2D(
                outputDimensions.x,
                outputDimensions.y,
                TextureFormat.RGBA32,
                false);
            convertedTexture.LoadRawTextureData(convertedData);
            convertedTexture.Apply(false, false);

            orientedTexture = ApplyScreenOrientation(convertedTexture);
            byte[] jpegData = orientedTexture.EncodeToJPG(JpegQuality);
            var request = new VisionAiRequest(
                jpegData,
                orientedTexture.width,
                orientedTexture.height,
                DefaultPrompt);

            _dataSource?.PublishResultText(
                $"Captured {request.Width} x {request.Height} image " +
                $"({request.JpegData.Length:N0} bytes JPEG).");
            _dataSource?.PublishImageRequest(request);
        }
        catch (Exception exception)
        {
            PublishError($"Could not capture camera image: {exception.Message}");
        }
        finally
        {
            cpuImage.Dispose();

            if (convertedData.IsCreated)
            {
                convertedData.Dispose();
            }

            if (orientedTexture != null && orientedTexture != convertedTexture)
            {
                Destroy(orientedTexture);
            }

            if (convertedTexture != null)
            {
                Destroy(convertedTexture);
            }

            _captureInProgress = false;
            NotifyCaptureAvailability();
        }
    }

    private void HandleProgressReport(VisionAiProgressReport report)
    {
        switch (report.Phase)
        {
            case VisionAiPhase.Ready:
                _aiReady = true;
                _inferenceInProgress = false;
                break;
            case VisionAiPhase.ExtractingModel:
            case VisionAiPhase.Initializing:
                _aiReady = false;
                _inferenceInProgress = false;
                break;
            case VisionAiPhase.Inferencing:
                _inferenceInProgress = true;
                break;
            case VisionAiPhase.Error:
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

        _onCaptureAvailabilityChanged?.Invoke(_aiReady && !_inferenceInProgress);
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

    private static Texture2D ApplyScreenOrientation(Texture2D source)
    {
        return Screen.orientation switch
        {
            ScreenOrientation.Portrait => RotateTexture(source, 90),
            ScreenOrientation.PortraitUpsideDown => RotateTexture(source, -90),
            ScreenOrientation.LandscapeRight => RotateTexture(source, 180),
            _ => source
        };
    }

    private static Texture2D RotateTexture(Texture2D source, int clockwiseDegrees)
    {
        Color32[] sourcePixels = source.GetPixels32();
        int sourceWidth = source.width;
        int sourceHeight = source.height;
        bool quarterTurn = clockwiseDegrees == 90 || clockwiseDegrees == -90;
        int targetWidth = quarterTurn ? sourceHeight : sourceWidth;
        int targetHeight = quarterTurn ? sourceWidth : sourceHeight;
        var targetPixels = new Color32[sourcePixels.Length];

        for (int y = 0; y < sourceHeight; y++)
        {
            for (int x = 0; x < sourceWidth; x++)
            {
                int targetX;
                int targetY;

                if (clockwiseDegrees == 90)
                {
                    targetX = sourceHeight - 1 - y;
                    targetY = x;
                }
                else if (clockwiseDegrees == -90)
                {
                    targetX = y;
                    targetY = sourceWidth - 1 - x;
                }
                else
                {
                    targetX = sourceWidth - 1 - x;
                    targetY = sourceHeight - 1 - y;
                }

                targetPixels[targetY * targetWidth + targetX] =
                    sourcePixels[y * sourceWidth + x];
            }
        }

        var target = new Texture2D(
            targetWidth,
            targetHeight,
            TextureFormat.RGBA32,
            false);
        target.SetPixels32(targetPixels);
        target.Apply(false, false);
        return target;
    }

    private void PublishError(string message)
    {
        _dataSource?.PublishProgress(new VisionAiProgressReport(
            VisionAiPhase.Error,
            message));
    }

    private void OnDestroy()
    {
        _progressSubscription?.Dispose();
    }
}
