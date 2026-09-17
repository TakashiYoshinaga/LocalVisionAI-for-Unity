using System;
using R3;
using UnityEngine;

public class VisionAiManager : MonoBehaviour
{
    private const string AndroidBridgeClass =
        "com.takashiyoshinaga.localvisionai.BundledModelBridge";
    private const string ModelAssetPath = BundledModelPaths.AndroidAssetPath;
    private const string ModelFileName = BundledModelPaths.FileName;

    private VisionAiDataSource _dataSource;
    private IDisposable _imageRequestSubscription;
    private bool _modelSetupInProgress;
    private bool _engineInitializationInProgress;
    private bool _inferenceInProgress;
    private int _lastRequestId;
    private int _activeRequestId;

    public string PreparedModelPath { get; private set; }
    public bool IsReady { get; private set; }

    public void Initialize(VisionAiDataSource visionAiDataSource)
    {
        _dataSource = visionAiDataSource;
        _imageRequestSubscription?.Dispose();
        _imageRequestSubscription = _dataSource.ImageRequests
            .Subscribe(AnalyzeImage);
        RetryModelSetup();
    }

    public void RetryModelSetup()
    {
        if (_modelSetupInProgress ||
            _engineInitializationInProgress ||
            _dataSource == null)
        {
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        _modelSetupInProgress = true;
        _dataSource.PublishProgress(new VisionAiProgressReport(
            VisionAiPhase.ExtractingModel,
            "Preparing bundled AI model...",
            0f));

        try
        {
            using AndroidJavaClass bridge = new(AndroidBridgeClass);
            bridge.CallStatic(
                "prepareBundledModel",
                gameObject.name,
                ModelAssetPath,
                ModelFileName);
        }
        catch (Exception exception)
        {
            _modelSetupInProgress = false;
            PublishError($"Could not start model setup: {exception.Message}");
        }
#else
        PublishError("Bundled model setup requires an Android device.");
#endif
    }

    public void OnBundledModelProgress(string json)
    {
        BundledModelCallback callback;

        try
        {
            callback = JsonUtility.FromJson<BundledModelCallback>(json);
        }
        catch (Exception exception)
        {
            _modelSetupInProgress = false;
            PublishError($"Invalid model setup response: {exception.Message}");
            return;
        }

        if (callback == null ||
            !Enum.TryParse(callback.phase, out VisionAiPhase phase))
        {
            _modelSetupInProgress = false;
            PublishError("Invalid model setup response.");
            return;
        }

        float? progress = callback.hasProgress
            ? callback.progress01
            : null;

        _dataSource?.PublishProgress(new VisionAiProgressReport(
            phase,
            callback.message,
            progress));

        if (callback.ready)
        {
            PreparedModelPath = callback.modelPath;
            _modelSetupInProgress = false;
            StartEngineInitialization();
        }
        else if (phase == VisionAiPhase.Error)
        {
            _modelSetupInProgress = false;
        }
    }

    public void OnEngineProgress(string json)
    {
        BundledModelCallback callback;

        try
        {
            callback = JsonUtility.FromJson<BundledModelCallback>(json);
        }
        catch (Exception exception)
        {
            _engineInitializationInProgress = false;
            PublishError($"Invalid engine response: {exception.Message}");
            return;
        }

        if (callback == null ||
            !Enum.TryParse(callback.phase, out VisionAiPhase phase))
        {
            _engineInitializationInProgress = false;
            PublishError("Invalid engine response.");
            return;
        }

        _dataSource?.PublishProgress(new VisionAiProgressReport(
            phase,
            callback.message));

        if (phase == VisionAiPhase.Ready && callback.ready)
        {
            IsReady = true;
            _engineInitializationInProgress = false;
        }
        else if (phase == VisionAiPhase.Error)
        {
            IsReady = false;
            _engineInitializationInProgress = false;
        }
    }

    private void AnalyzeImage(VisionAiRequest request)
    {
        if (request == null || _dataSource == null)
        {
            return;
        }

        if (!IsReady)
        {
            PublishError("The AI engine is not ready yet.");
            return;
        }

        if (_inferenceInProgress)
        {
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        _inferenceInProgress = true;
        _activeRequestId = ++_lastRequestId;
        _dataSource.PublishProgress(new VisionAiProgressReport(
            VisionAiPhase.Inferencing,
            "Analyzing image..."));

        try
        {
            using AndroidJavaClass bridge = new(AndroidBridgeClass);
            bridge.CallStatic(
                "analyze",
                gameObject.name,
                _activeRequestId,
                request.JpegData,
                request.Prompt);
        }
        catch (Exception exception)
        {
            _inferenceInProgress = false;
            PublishError($"Could not start image analysis: {exception.Message}");
        }
#else
        PublishError("Image analysis requires an Android device.");
#endif
    }

    public void OnAnalysisProgress(string json)
    {
        AnalysisCallback callback;

        try
        {
            callback = JsonUtility.FromJson<AnalysisCallback>(json);
        }
        catch (Exception exception)
        {
            _inferenceInProgress = false;
            PublishError($"Invalid analysis response: {exception.Message}");
            return;
        }

        if (callback == null ||
            !Enum.TryParse(callback.phase, out VisionAiPhase phase))
        {
            _inferenceInProgress = false;
            PublishError("Invalid analysis response.");
            return;
        }

        if (callback.requestId != _activeRequestId)
        {
            return;
        }

        if (phase == VisionAiPhase.Inferencing)
        {
            _dataSource?.PublishProgress(new VisionAiProgressReport(
                phase,
                callback.message));
            return;
        }

        _inferenceInProgress = false;

        if (phase == VisionAiPhase.Error)
        {
            PublishError(callback.message);
            return;
        }

        // Report Ready before the answer so the UI re-enables its capture
        // button first and the answer stays as the last text on screen.
        _dataSource?.PublishProgress(new VisionAiProgressReport(
            VisionAiPhase.Ready,
            callback.message));
        _dataSource?.PublishResultText(callback.resultText);
    }

    public void Shutdown()
    {
        _imageRequestSubscription?.Dispose();
        _imageRequestSubscription = null;
        _modelSetupInProgress = false;
        _engineInitializationInProgress = false;
        _inferenceInProgress = false;
        IsReady = false;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using AndroidJavaClass bridge = new(AndroidBridgeClass);
            bridge.CallStatic("shutdown");
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"Could not shut down the AI engine cleanly: {exception.Message}");
        }
#endif
    }

    private void StartEngineInitialization()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(PreparedModelPath))
        {
            PublishError("Prepared AI model path is missing.");
            return;
        }

        _engineInitializationInProgress = true;
        IsReady = false;
        _dataSource?.PublishProgress(new VisionAiProgressReport(
            VisionAiPhase.Initializing,
            "Initializing AI engine..."));

        try
        {
            using AndroidJavaClass bridge = new(AndroidBridgeClass);
            bridge.CallStatic(
                "initialize",
                gameObject.name,
                PreparedModelPath);
        }
        catch (Exception exception)
        {
            _engineInitializationInProgress = false;
            PublishError($"Could not start AI initialization: {exception.Message}");
        }
#endif
    }

    private void PublishError(string message)
    {
        _dataSource?.PublishProgress(new VisionAiProgressReport(
            VisionAiPhase.Error,
            message));
    }

    [Serializable]
    private sealed class AnalysisCallback
    {
        public string phase;
        public string message;
        public int requestId;
        public string resultText;
    }

    [Serializable]
    private sealed class BundledModelCallback
    {
        public string phase;
        public string message;
        public bool hasProgress;
        public float progress01;
        public bool ready;
        public bool retryable;
        public string modelPath;
    }
}
