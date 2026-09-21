// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace LiteRtLmUnity
{
    /// <summary>
    /// Installs the prebuilt LiteRT-LM library that lets the Editor run the model
    /// itself, instead of every prompt change needing a Build and Run.
    /// </summary>
    /// <remarks>
    /// The library is 140 MB, so it is downloaded on demand rather than tracked by
    /// Git, the same reasoning that keeps the model file out of the repository.
    /// It is imported as an Editor-only plugin and never reaches the APK, which
    /// uses the Android bridge and the Gradle dependency instead.
    /// </remarks>
    public static class EditorNativeLibrarySetup
    {
        private const string MenuPath = "Tools/LiteRT-LM/Install Editor Native Library";
        private const string ArchiveName = "CLiteRTLM_mac.xcframework.zip";
        private const string LibraryFileName = "libCLiteRTLM_mac.dylib";

        /// <summary>Where the library is placed, relative to the project folder.</summary>
        private const string PluginAssetPath = "Assets/Plugins/macOS/" + LibraryFileName;

        /// <summary>The path the archive holds the universal macOS library at.</summary>
        private const string ArchiveLibraryPath =
            "CLiteRTLM_mac.xcframework/macos-arm64_x86_64/" + LibraryFileName;

        private static string DownloadUrl =>
            "https://github.com/google-ai-edge/LiteRT-LM/releases/download/" +
            $"v{LiteRtLmAndroidBuildSetup.LiteRtLmVersion}/{ArchiveName}";

        [MenuItem(MenuPath)]
        public static void Install()
        {
            if (Application.platform != RuntimePlatform.OSXEditor)
            {
                EditorUtility.DisplayDialog(
                    "LiteRT-LM",
                    "LiteRT-LM publishes a prebuilt desktop library for macOS only, so " +
                    "the Editor can run the model on macOS alone for now. On other " +
                    "systems, build and run on an Android device.",
                    "OK");
                return;
            }

            string workingDirectory = Path.Combine(Path.GetTempPath(), "LiteRtLmUnityInstall");

            try
            {
                Directory.CreateDirectory(workingDirectory);
                string archivePath = Path.Combine(workingDirectory, ArchiveName);

                if (!Download(DownloadUrl, archivePath))
                {
                    return;
                }

                EditorUtility.DisplayProgressBar("LiteRT-LM", "Extracting the library...", 0.9f);
                Unzip(archivePath, workingDirectory);

                string extracted = Path.Combine(workingDirectory, ArchiveLibraryPath);
                if (!File.Exists(extracted))
                {
                    throw new FileNotFoundException(
                        $"{ArchiveName} did not contain {ArchiveLibraryPath}.");
                }

                string destination = Path.GetFullPath(PluginAssetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
                File.Copy(extracted, destination, overwrite: true);

                AssetDatabase.ImportAsset(PluginAssetPath, ImportAssetOptions.ForceUpdate);
                MarkAsEditorOnlyPlugin();

                Debug.Log(
                    $"Installed {LibraryFileName} " +
                    $"(LiteRT-LM v{LiteRtLmAndroidBuildSetup.LiteRtLmVersion}) at {PluginAssetPath}. " +
                    "Enter Play mode to run the model in the Editor.");
            }
            catch (Exception exception)
            {
                Debug.LogError($"Could not install the LiteRT-LM Editor library: {exception.Message}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                DeleteQuietly(workingDirectory);
            }
        }

        [MenuItem(MenuPath, isValidateFunction: true)]
        private static bool CanInstall()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        private static bool Download(string url, string destination)
        {
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerFile(destination);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "LiteRT-LM",
                        $"Downloading {ArchiveName}... ({request.downloadedBytes / (1024 * 1024)} MB)",
                        request.downloadProgress))
                {
                    request.Abort();
                    Debug.Log("The LiteRT-LM Editor library download was cancelled.");
                    return false;
                }
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new IOException($"Downloading {url} failed: {request.error}");
            }

            return true;
        }

        /// <summary>
        /// Extracts with the system tool rather than a managed one, because the
        /// library has to keep its executable permission bit to be loadable.
        /// </summary>
        private static void Unzip(string archivePath, string destinationDirectory)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/unzip",
                Arguments = $"-o -q \"{archivePath}\" -d \"{destinationDirectory}\"",
                UseShellExecute = false,
                RedirectStandardError = true
            };

            using Process process = Process.Start(startInfo);
            if (process == null)
            {
                throw new IOException("Could not start unzip.");
            }

            string errors = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                throw new IOException($"unzip failed: {errors}");
            }
        }

        /// <summary>
        /// Keeps the library out of every player build. The APK gets LiteRT-LM
        /// from its Gradle dependency, so shipping this too would only add 140 MB.
        /// </summary>
        private static void MarkAsEditorOnlyPlugin()
        {
            if (AssetImporter.GetAtPath(PluginAssetPath) is not PluginImporter importer)
            {
                Debug.LogWarning(
                    $"{PluginAssetPath} was not imported as a plugin. Set it to Editor only by hand.");
                return;
            }

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetEditorData("OS", "OSX");
            importer.SetEditorData("CPU", "AnyCPU");
            importer.SetCompatibleWithPlatform(BuildTarget.Android, false);
            importer.SetCompatibleWithPlatform(BuildTarget.StandaloneOSX, false);
            importer.SaveAndReimport();
        }

        private static void DeleteQuietly(string directory)
        {
            try
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not clean up {directory}: {exception.Message}");
            }
        }
    }
}
