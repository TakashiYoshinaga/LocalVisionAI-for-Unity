using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Command line entry point used to build the Android player without opening
/// the Editor UI. Invoked as
/// <c>unity build --target Android --execute-method LocalVisionAiBuilder.BuildAndroidApk -o &lt;path&gt;</c>.
/// </summary>
public static class LocalVisionAiBuilder
{
    private const string DefaultOutputPath = "Builds/Android/LocalVisionAI.apk";

    [MenuItem("Tools/Local Vision AI/Build Android APK")]
    public static void BuildAndroidApk()
    {
        string outputPath = ResolveOutputPath();
        string outputDirectory = Path.GetDirectoryName(outputPath);

        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
        {
            Fail("No enabled scenes are present in the build settings.");
            return;
        }

        EditorUserBuildSettings.buildAppBundle = false;

        BuildPlayerOptions options = new()
        {
            scenes = scenes,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            locationPathName = outputPath,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        Debug.Log(
            $"Local Vision AI: Android build {summary.result} " +
            $"({summary.totalSize:N0} bytes, {summary.totalErrors} errors, " +
            $"{summary.totalWarnings} warnings) -> {outputPath}");

        if (summary.result != BuildResult.Succeeded)
        {
            Fail($"Android build did not succeed: {summary.result}.");
            return;
        }

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(0);
        }
    }

    private static string ResolveOutputPath()
    {
        string[] arguments = Environment.GetCommandLineArgs();

        for (int index = 0; index < arguments.Length - 1; index++)
        {
            if (arguments[index] is "-buildOutput" or "-outputPath")
            {
                return Path.GetFullPath(arguments[index + 1]);
            }
        }

        return Path.GetFullPath(DefaultOutputPath);
    }

    private static void Fail(string message)
    {
        Debug.LogError($"Local Vision AI: {message}");

        if (Application.isBatchMode)
        {
            EditorApplication.Exit(1);
        }
    }
}
