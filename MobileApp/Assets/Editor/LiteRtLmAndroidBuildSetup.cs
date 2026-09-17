using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEngine;

/// <summary>
/// Adds the fixed LiteRT-LM dependency and Android manifest declarations to
/// Unity's generated Gradle project. Keeping this in a post processor avoids
/// maintaining complete custom Gradle and manifest templates for the PoC.
/// </summary>
public sealed class LiteRtLmAndroidBuildSetup : IPostGenerateGradleAndroidProject
{
    private const string LiteRtLmVersion = "0.17.0";
    private const string KotlinVersion = "2.4.0";
    private const string LiteRtLmDependency =
        "com.google.ai.edge.litertlm:litertlm-android:" + LiteRtLmVersion;
    private const string AndroidNamespace =
        "http://schemas.android.com/apk/res/android";

    public int callbackOrder => 100;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        string unityLibraryDirectory = ResolveUnityLibraryDirectory(path);
        string rootDirectory = Directory.GetParent(unityLibraryDirectory)?.FullName;

        if (string.IsNullOrEmpty(rootDirectory))
        {
            throw new BuildFailedException(
                "Could not locate the generated Android Gradle project.");
        }

        PatchRootGradle(Path.Combine(rootDirectory, "build.gradle"));
        PatchUnityLibraryGradle(
            Path.Combine(unityLibraryDirectory, "build.gradle"));
        PatchManifest(
            Path.Combine(
                unityLibraryDirectory,
                "src",
                "main",
                "AndroidManifest.xml"));

        Debug.Log(
            $"Local Vision AI: configured LiteRT-LM {LiteRtLmVersion} " +
            $"and Kotlin {KotlinVersion} in the generated Android project.");
    }

    private static string ResolveUnityLibraryDirectory(string path)
    {
        string nested = Path.Combine(path, "unityLibrary");
        return Directory.Exists(nested) ? nested : path;
    }

    private static void PatchRootGradle(string path)
    {
        string contents = ReadRequiredFile(path);
        const string pattern =
            @"id 'org\.jetbrains\.kotlin\.android' version '[^']+' apply false";
        string replacement =
            $"id 'org.jetbrains.kotlin.android' version '{KotlinVersion}' apply false";
        string updated = Regex.Replace(contents, pattern, replacement);

        if (updated == contents && !contents.Contains(replacement))
        {
            throw new BuildFailedException(
                "Could not update the Kotlin Gradle plugin version.");
        }

        File.WriteAllText(path, updated);
    }

    private static void PatchUnityLibraryGradle(string path)
    {
        string contents = ReadRequiredFile(path);
        string updated = Regex.Replace(
            contents,
            @"implementation 'org\.jetbrains\.kotlin:kotlin-stdlib(?:-jdk7)?:[^']+'",
            $"implementation 'org.jetbrains.kotlin:kotlin-stdlib:{KotlinVersion}'");

        if (!updated.Contains(LiteRtLmDependency))
        {
            const string dependenciesMarker = "dependencies {";
            int markerIndex = updated.IndexOf(
                dependenciesMarker,
                StringComparison.Ordinal);

            if (markerIndex < 0)
            {
                throw new BuildFailedException(
                    "Could not locate the generated Gradle dependencies block.");
            }

            int insertionIndex = markerIndex + dependenciesMarker.Length;
            updated = updated.Insert(
                insertionIndex,
                $"\n    implementation '{LiteRtLmDependency}'");
        }

        File.WriteAllText(path, updated);
    }

    private static void PatchManifest(string path)
    {
        string contents = ReadRequiredFile(path);
        var document = new XmlDocument { PreserveWhitespace = true };
        document.LoadXml(contents);

        XmlElement application = document.DocumentElement?
            .SelectSingleNode("application") as XmlElement;

        if (application == null)
        {
            throw new BuildFailedException(
                "Could not locate the Android application manifest element.");
        }

        AddOptionalNativeLibrary(document, application, "libvndksupport.so");
        AddOptionalNativeLibrary(document, application, "libOpenCL.so");
        document.Save(path);
    }

    private static void AddOptionalNativeLibrary(
        XmlDocument document,
        XmlElement application,
        string libraryName)
    {
        foreach (XmlNode child in application.ChildNodes)
        {
            if (child is XmlElement element &&
                element.LocalName == "uses-native-library" &&
                element.GetAttribute("name", AndroidNamespace) == libraryName)
            {
                return;
            }
        }

        XmlElement library = document.CreateElement("uses-native-library");
        library.SetAttribute("name", AndroidNamespace, libraryName);
        library.SetAttribute("required", AndroidNamespace, "false");
        application.AppendChild(library);
    }

    private static string ReadRequiredFile(string path)
    {
        if (!File.Exists(path))
        {
            throw new BuildFailedException(
                $"Generated Android build file was not found: {path}");
        }

        return File.ReadAllText(path);
    }
}
