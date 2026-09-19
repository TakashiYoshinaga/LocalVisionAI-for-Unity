using UnityEngine;

/// <summary>
/// Reads the height occupied by Android's docked software keyboard without
/// changing the keyboard or the Activity's soft-input configuration.
/// </summary>
internal static class AndroidKeyboardInsetProvider
{
    private const float MinimumKeyboardHeightRatio = 0.15f;

#if UNITY_ANDROID
    private static AndroidJavaObject _decorView;
    private static AndroidJavaObject _visibleDisplayFrame;
    private static AndroidJavaClass _windowInsetsType;
    private static int _apiLevel;
    private static bool _initializationAttempted;
    private static bool _failureReported;
#endif

    /// <summary>
    /// Returns the distance from the bottom of the Unity screen to the top of
    /// the keyboard in Unity screen pixels. Returns zero when no docked
    /// software keyboard is visible.
    /// </summary>
    public static float GetBottomInset()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        Rect unityKeyboardArea = TouchScreenKeyboard.area;

        if (unityKeyboardArea.height > 0f)
        {
            // This UI only avoids a keyboard docked to the bottom edge. Unity
            // can report the Rect position using a platform coordinate origin,
            // so yMax may resolve to the full screen height on Android.
            return unityKeyboardArea.height;
        }

        return GetAndroidBottomInset();
#else
        return 0f;
#endif
    }

    public static void Dispose()
    {
#if UNITY_ANDROID
        _windowInsetsType?.Dispose();
        _windowInsetsType = null;
        _visibleDisplayFrame?.Dispose();
        _visibleDisplayFrame = null;
        _decorView?.Dispose();
        _decorView = null;
        _initializationAttempted = false;
        _failureReported = false;
        _apiLevel = 0;
#endif
    }

#if UNITY_ANDROID
    private static float GetAndroidBottomInset()
    {
        try
        {
            EnsureInitialized();

            if (_decorView == null)
            {
                return 0f;
            }

            int decorViewHeight = _decorView.Call<int>("getHeight");

            if (decorViewHeight <= 0)
            {
                return 0f;
            }

            int insetPixels = _apiLevel >= 30
                ? GetImeInsetFromWindowInsets()
                : -1;

            if (insetPixels < 0)
            {
                insetPixels = GetInsetFromVisibleDisplayFrame(decorViewHeight);
            }

            float screenScale = Screen.height / (float)decorViewHeight;
            return Mathf.Max(0f, insetPixels * screenScale);
        }
        catch (AndroidJavaException exception)
        {
            if (!_failureReported)
            {
                Debug.LogWarning($"Could not read the Android keyboard inset: {exception.Message}");
                _failureReported = true;
            }

            return 0f;
        }
    }

    private static void EnsureInitialized()
    {
        if (_initializationAttempted)
        {
            return;
        }

        _initializationAttempted = true;

        using AndroidJavaClass unityPlayer = new("com.unity3d.player.UnityPlayer");
        using AndroidJavaObject activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
        using AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow");
        using AndroidJavaClass buildVersion = new("android.os.Build$VERSION");

        _decorView = window.Call<AndroidJavaObject>("getDecorView");
        _visibleDisplayFrame = new AndroidJavaObject("android.graphics.Rect");
        _apiLevel = buildVersion.GetStatic<int>("SDK_INT");

        if (_apiLevel >= 30)
        {
            _windowInsetsType = new AndroidJavaClass("android.view.WindowInsets$Type");
        }
    }

    private static int GetImeInsetFromWindowInsets()
    {
        using AndroidJavaObject windowInsets = _decorView.Call<AndroidJavaObject>("getRootWindowInsets");

        if (windowInsets == null)
        {
            return -1;
        }

        int imeType = _windowInsetsType.CallStatic<int>("ime");

        if (!windowInsets.Call<bool>("isVisible", imeType))
        {
            return 0;
        }

        using AndroidJavaObject imeInsets = windowInsets.Call<AndroidJavaObject>("getInsets", imeType);
        return imeInsets.Get<int>("bottom");
    }

    private static int GetInsetFromVisibleDisplayFrame(int decorViewHeight)
    {
        _decorView.Call("getWindowVisibleDisplayFrame", _visibleDisplayFrame);
        int visibleBottom = _visibleDisplayFrame.Get<int>("bottom");
        int bottomInset = Mathf.Max(0, decorViewHeight - visibleBottom);

        // The visible frame also excludes a small navigation-bar area on some
        // Android versions. Treat only a keyboard-sized occlusion as an IME.
        return bottomInset >= decorViewHeight * MinimumKeyboardHeightRatio
            ? bottomInset
            : 0;
    }
#endif
}
