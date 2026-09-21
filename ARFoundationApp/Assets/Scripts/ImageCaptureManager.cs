// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

using System;
using LiteRtLmUnity;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using R3;

public class ImageCaptureManager : MonoBehaviour
{
    private const int MaxImageDimension = 1024;
    private const int JpegQuality = 85;

    [SerializeField] private ARCameraManager _cameraManager;

    private LlmDataSource _dataSource;
    private Action<bool> _onCaptureAvailabilityChanged;
#if UNITY_EDITOR
    // Analyzed in place of an AR frame while playing in the Editor. Resolved
    // once, because finding the settings asset goes through the asset database.
    private Texture2D _editorTestImage;
#endif
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
#if UNITY_EDITOR
        _editorTestImage = LiteRtLmUnity.EditorLlmSettings.LoadOrCreate().TestImage;
#endif

        _onCaptureAvailabilityChanged?.Invoke(false);
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

#if UNITY_EDITOR
        if (_editorTestImage != null)
        {
            CaptureEditorTestImage();
            return;
        }
#endif

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
        _dataSource?.PublishProgress(new LlmProgressReport(
            LlmPhase.Capturing,
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
            var request = new ImageRequest(
                jpegData,
                orientedTexture.width,
                orientedTexture.height);

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

#if UNITY_EDITOR
    /// <summary>
    /// Analyzes the still image assigned on the LiteRT-LM Editor settings asset
    /// instead of an AR frame, so that a prompt can be tried against a fixed
    /// subject without building to a device. The device always uses the camera.
    /// </summary>
    /// <remarks>
    /// The AR camera hands out nothing in the Editor, so this is what makes the
    /// Editor path usable here at all. It skips the orientation pass the camera
    /// frames go through: a picture is already the way up it was authored.
    /// </remarks>
    private void CaptureEditorTestImage()
    {
        _captureInProgress = true;
        _onCaptureAvailabilityChanged?.Invoke(false);
        _dataSource?.PublishProgress(new LlmProgressReport(
            LlmPhase.Capturing,
            "Capturing camera image..."));

        Texture2D frame = null;

        try
        {
            // Copied through a RenderTexture so that the assigned asset works
            // whatever its import settings say about readability or compression.
            frame = ResizeTexture(
                _editorTestImage,
                GetScaledDimensions(_editorTestImage.width, _editorTestImage.height));

            byte[] jpegData = frame.EncodeToJPG(JpegQuality);
            var request = new ImageRequest(jpegData, frame.width, frame.height);

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
            if (frame != null)
            {
                Destroy(frame);
            }

            _captureInProgress = false;
            NotifyCaptureAvailability();
        }
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
#endif

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
        _dataSource?.PublishProgress(new LlmProgressReport(
            LlmPhase.Error,
            message));
    }

    private void OnDestroy()
    {
        _progressSubscription?.Dispose();
    }
}
