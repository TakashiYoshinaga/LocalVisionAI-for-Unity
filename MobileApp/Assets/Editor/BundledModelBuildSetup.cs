using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public sealed class BundledModelBuildSetup : IPreprocessBuildWithReport
{
    private const string ModelAssetPath =
        "Assets/StreamingAssets/Models/gemma-4-E2B-it.litertlm";
    private const string MetadataAssetPath = ModelAssetPath + ".sha256";
    private const string KotlinPluginPath =
        "Assets/Android/BundledModelBridge.kt";
    private const string MainGradleTemplatePath =
        "Assets/Plugins/Android/mainTemplate.gradle";

    public int callbackOrder => -1000;

    static BundledModelBuildSetup()
    {
        EditorApplication.delayCall += ApplyProjectSettings;
    }

    [MenuItem("Tools/Local Vision AI/Setup Bundled Model Build")]
    public static void ApplyProjectSettings()
    {
        ConfigureKotlinPlugin();
        EnableCustomMainGradleTemplate();
    }

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.Android)
        {
            return;
        }

        ApplyProjectSettings();
        GenerateModelMetadata();
    }

    private static void ConfigureKotlinPlugin()
    {
        PluginImporter importer =
            AssetImporter.GetAtPath(KotlinPluginPath) as PluginImporter;

        if (importer == null)
        {
            return;
        }

        bool needsUpdate = importer.GetCompatibleWithAnyPlatform() ||
            !importer.GetCompatibleWithPlatform(BuildTarget.Android);

        if (!needsUpdate)
        {
            return;
        }

        importer.SetCompatibleWithAnyPlatform(false);
        importer.SetCompatibleWithEditor(false);
        importer.SetCompatibleWithPlatform(BuildTarget.Android, true);
        importer.SaveAndReimport();
    }

    private static void EnableCustomMainGradleTemplate()
    {
        if (!File.Exists(MainGradleTemplatePath))
        {
            throw new InvalidOperationException(
                $"Gradle template was not found at {MainGradleTemplatePath}.");
        }

        MethodInfo getSerializedObject = typeof(PlayerSettings).GetMethod(
            "GetSerializedObject",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        if (getSerializedObject == null ||
            getSerializedObject.Invoke(null, null) is not SerializedObject settings)
        {
            throw new InvalidOperationException(
                "Could not access serialized Android Player settings.");
        }

        SerializedProperty useCustomTemplate =
            settings.FindProperty("useCustomMainGradleTemplate");

        if (useCustomTemplate == null)
        {
            throw new InvalidOperationException(
                "Custom main Gradle template setting was not found.");
        }

        if (!useCustomTemplate.boolValue)
        {
            useCustomTemplate.boolValue = true;
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
    }

    private static void GenerateModelMetadata()
    {
        if (!File.Exists(ModelAssetPath))
        {
            throw new BuildFailedException(
                "Bundled model is missing. Place it at " + ModelAssetPath + ".");
        }

        FileInfo modelFile = new(ModelAssetPath);
        if (modelFile.Length <= 0L)
        {
            throw new BuildFailedException("Bundled model file is empty.");
        }

        string hash;
        using (FileStream stream = new(
                   ModelAssetPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   1024 * 1024,
                   FileOptions.SequentialScan))
        using (SHA256 sha256 = SHA256.Create())
        {
            hash = BitConverter.ToString(sha256.ComputeHash(stream))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }

        string metadata = $"sha256={hash}\nsize={modelFile.Length}\n";
        File.WriteAllText(MetadataAssetPath, metadata, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(
            MetadataAssetPath,
            ImportAssetOptions.ForceSynchronousImport |
            ImportAssetOptions.ForceUpdate);

        Debug.Log(
            $"Local Vision AI: prepared bundled model metadata " +
            $"({modelFile.Length:N0} bytes, SHA-256 {hash}).");
    }
}
