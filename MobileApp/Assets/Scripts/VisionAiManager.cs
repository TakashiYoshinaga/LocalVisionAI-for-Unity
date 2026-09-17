using System;
using UnityEngine;

public class VisionAiManager : MonoBehaviour
{
    private const string AndroidBridgeClass =
        "com.takashiyoshinaga.localvisionai.BundledModelBridge";
    private const string ModelAssetPath = BundledModelPaths.AndroidAssetPath;
    private const string ModelFileName = BundledModelPaths.FileName;

    private VisionAiDataSource _dataSource;
    private bool _modelSetupInProgress;
    private bool _engineInitializationInProgress;

    public string PreparedModelPath { get; private set; }
    public bool IsReady { get; private set; }

    public void Initialize(VisionAiDataSource visionAiDataSource)
    {
        _dataSource = visionAiDataSource;
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

    public void Shutdown()
    {
        _modelSetupInProgress = false;
        _engineInitializationInProgress = false;
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
