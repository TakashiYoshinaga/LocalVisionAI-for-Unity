using System;
using UnityEngine;

public class VisionAiManager : MonoBehaviour
{
    private const string AndroidBridgeClass =
        "com.takashiyoshinaga.localvisionai.BundledModelBridge";
    private const string ModelAssetPath =
        "Models/gemma-4-E2B-it.litertlm";
    private const string ModelFileName =
        "gemma-4-E2B-it.litertlm";

    private VisionAiDataSource _dataSource;
    private bool _modelSetupInProgress;

    public string PreparedModelPath { get; private set; }

    public void Initialize(VisionAiDataSource visionAiDataSource)
    {
        _dataSource = visionAiDataSource;
        RetryModelSetup();
    }

    public void RetryModelSetup()
    {
        if (_modelSetupInProgress || _dataSource == null)
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
        }
        else if (phase == VisionAiPhase.Error)
        {
            _modelSetupInProgress = false;
        }
    }

    public void Shutdown()
    {
        _modelSetupInProgress = false;
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
