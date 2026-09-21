// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
    /// <para>
    /// The library is well over a hundred megabytes, so it is downloaded on
    /// demand rather than tracked by Git, the same reasoning that keeps the model
    /// file out of the repository. It is imported as an Editor-only plugin and
    /// never reaches the APK, which uses the Android bridge and the Gradle
    /// dependency instead.
    /// </para>
    /// <para>
    /// macOS and Windows are fed from different releases. LiteRT-LM attaches the
    /// macOS framework to every release, so that one follows
    /// <see cref="LiteRtLmAndroidBuildSetup.LiteRtLmVersion"/> and stays in step
    /// with the APK. The Windows library has only ever been published once, in
    /// the multi-platform C API bundle attached to v0.16.0, so Windows is pinned
    /// there. The two are interchangeable for this package's purposes: every
    /// entry point <see cref="LiteRtLmNative"/> declares has the same signature
    /// in both.
    /// </para>
    /// </remarks>
    public static class EditorNativeLibrarySetup
    {
        private const string MenuPath = "Tools/LiteRT-LM/Install Editor Native Library";

        // --- macOS ------------------------------------------------------------

        private const string MacArchiveName = "CLiteRTLM_mac.xcframework.zip";
        private const string MacLibraryFileName = "libCLiteRTLM_mac.dylib";

        /// <summary>Where the library is placed, relative to the project folder.</summary>
        private const string MacPluginAssetPath = "Assets/Plugins/macOS/" + MacLibraryFileName;

        /// <summary>The path the archive holds the universal macOS library at.</summary>
        private const string MacArchiveLibraryPath =
            "CLiteRTLM_mac.xcframework/macos-arm64_x86_64/" + MacLibraryFileName;

        private static string MacDownloadUrl =>
            "https://github.com/google-ai-edge/LiteRT-LM/releases/download/" +
            $"v{LiteRtLmAndroidBuildSetup.LiteRtLmVersion}/{MacArchiveName}";

        // --- Windows ----------------------------------------------------------

        /// <summary>
        /// The only LiteRT-LM release that carries a Windows build of the C API.
        /// </summary>
        private const string WindowsBundleTag = "v0.16.0";

        private const string WindowsArchiveName = "litert_lm_c_api-0.1.0.zip";

        private static string WindowsDownloadUrl =>
            "https://github.com/google-ai-edge/LiteRT-LM/releases/download/" +
            $"{WindowsBundleTag}/{WindowsArchiveName}";

        /// <summary>
        /// The DirectX Shader Compiler release the GPU backend needs. Dawn asks
        /// it for shader model 6.8, which the copy Unity ships under
        /// <c>Data/Tools</c> is too old to accept.
        /// </summary>
        private const string DxcArchiveName = "dxc_2026_07_29.zip";

        private static string DxcDownloadUrl =>
            "https://github.com/microsoft/DirectXShaderCompiler/releases/download/" +
            $"v1.9.2607/{DxcArchiveName}";

        private const string WindowsPluginDirectory = "Assets/Plugins/x86_64";

        /// <summary>
        /// What to pull out of each Windows archive, as archive entry to plugin
        /// file name. The C API bundle holds every platform's build, and the DXC
        /// archive holds three architectures, so both are filtered rather than
        /// unpacked whole.
        /// </summary>
        private static readonly (string Archive, string Entry, string FileName)[] WindowsFiles =
        {
            (WindowsArchiveName, "lib/windows_x86_64/bin/litert-lm.dll", "litert-lm.dll"),
            (DxcArchiveName, "bin/x64/dxcompiler.dll", "dxcompiler.dll"),
            (DxcArchiveName, "bin/x64/dxil.dll", "dxil.dll")
        };

        [MenuItem(MenuPath)]
        public static void Install()
        {
            string workingDirectory = Path.Combine(Path.GetTempPath(), "LiteRtLmUnityInstall");

            try
            {
                Directory.CreateDirectory(workingDirectory);

                switch (Application.platform)
                {
                    case RuntimePlatform.OSXEditor:
                        InstallMac(workingDirectory);
                        break;
                    case RuntimePlatform.WindowsEditor:
                        InstallWindows(workingDirectory);
                        break;
                    default:
                        EditorUtility.DisplayDialog(
                            "LiteRT-LM",
                            "LiteRT-LM publishes prebuilt desktop libraries for macOS and " +
                            "Windows, so the Editor can run the model on those two for now. " +
                            "On other systems, build and run on an Android device.",
                            "OK");
                        break;
                }
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

        private static void InstallMac(string workingDirectory)
        {
            string archivePath = Path.Combine(workingDirectory, MacArchiveName);

            if (!Download(MacDownloadUrl, MacArchiveName, archivePath))
            {
                return;
            }

            EditorUtility.DisplayProgressBar("LiteRT-LM", "Extracting the library...", 0.9f);
            UnzipWithSystemTool(archivePath, workingDirectory);

            string extracted = Path.Combine(workingDirectory, MacArchiveLibraryPath);
            if (!File.Exists(extracted))
            {
                throw new FileNotFoundException(
                    $"{MacArchiveName} did not contain {MacArchiveLibraryPath}.");
            }

            string destination = Path.GetFullPath(MacPluginAssetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? string.Empty);
            File.Copy(extracted, destination, overwrite: true);

            AssetDatabase.ImportAsset(MacPluginAssetPath, ImportAssetOptions.ForceUpdate);
            MarkAsEditorOnlyPlugin(MacPluginAssetPath, "OSX", "AnyCPU");

            Debug.Log(
                $"Installed {MacLibraryFileName} " +
                $"(LiteRT-LM v{LiteRtLmAndroidBuildSetup.LiteRtLmVersion}) at {MacPluginAssetPath}. " +
                "Enter Play mode to run the model in the Editor.");
        }

        private static void InstallWindows(string workingDirectory)
        {
            var archives = new Dictionary<string, string>
            {
                [WindowsArchiveName] = WindowsDownloadUrl,
                [DxcArchiveName] = DxcDownloadUrl
            };

            foreach (KeyValuePair<string, string> archive in archives)
            {
                string path = Path.Combine(workingDirectory, archive.Key);
                if (!Download(archive.Value, archive.Key, path))
                {
                    return;
                }
            }

            Directory.CreateDirectory(Path.GetFullPath(WindowsPluginDirectory));

            foreach ((string archive, string entry, string fileName) in WindowsFiles)
            {
                EditorUtility.DisplayProgressBar("LiteRT-LM", $"Extracting {fileName}...", 0.9f);
                string assetPath = $"{WindowsPluginDirectory}/{fileName}";
                ExtractEntry(
                    Path.Combine(workingDirectory, archive),
                    entry,
                    Path.GetFullPath(assetPath));

                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                MarkAsEditorOnlyPlugin(assetPath, "Windows", "x86_64");
            }

            Debug.Log(
                $"Installed litert-lm.dll (LiteRT-LM {WindowsBundleTag}) and the DirectX " +
                $"Shader Compiler it needs for the GPU at {WindowsPluginDirectory}. " +
                "Enter Play mode to run the model in the Editor.");
        }

        private static bool Download(string url, string archiveName, string destination)
        {
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.downloadHandler = new DownloadHandlerFile(destination);
            UnityWebRequestAsyncOperation operation = request.SendWebRequest();

            while (!operation.isDone)
            {
                if (EditorUtility.DisplayCancelableProgressBar(
                        "LiteRT-LM",
                        $"Downloading {archiveName}... ({request.downloadedBytes / (1024 * 1024)} MB)",
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
        /// macOS library has to keep its executable permission bit to be loadable.
        /// </summary>
        private static void UnzipWithSystemTool(string archivePath, string destinationDirectory)
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
        /// Copies one file out of an archive. Windows has no permission bit to
        /// preserve, so the managed reader is enough here, and it saves unpacking
        /// archives that hold every platform's build.
        /// </summary>
        /// <remarks>
        /// Entries are matched with both separators, because the DirectX Shader
        /// Compiler archive names its entries with backslashes.
        /// </remarks>
        private static void ExtractEntry(string archivePath, string entryPath, string destination)
        {
            using var archive = new ZipArchive(File.OpenRead(archivePath), ZipArchiveMode.Read);

            ZipArchiveEntry entry = null;
            foreach (ZipArchiveEntry candidate in archive.Entries)
            {
                if (candidate.FullName.Replace('\\', '/')
                    .Equals(entryPath, StringComparison.OrdinalIgnoreCase))
                {
                    entry = candidate;
                    break;
                }
            }

            if (entry == null)
            {
                throw new FileNotFoundException(
                    $"{Path.GetFileName(archivePath)} did not contain {entryPath}.");
            }

            using Stream source = entry.Open();

            FileStream target;
            try
            {
                target = File.Create(destination);
            }
            catch (IOException exception)
            {
                // Unity never unloads a native plugin, so once the Editor has run
                // the model the file stays locked for the rest of the session.
                // Recompiling scripts does not help; only restarting does.
                throw new IOException(
                    $"{Path.GetFileName(destination)} is in use, which happens once " +
                    "the Editor has loaded it. Restart the Editor and install again. " +
                    $"({exception.Message})",
                    exception);
            }

            using (target)
            {
                source.CopyTo(target);
            }
        }

        /// <summary>
        /// Keeps the library out of every player build. The APK gets LiteRT-LM
        /// from its Gradle dependency, so shipping this too would only add weight.
        /// </summary>
        private static void MarkAsEditorOnlyPlugin(string assetPath, string os, string cpu)
        {
            if (AssetImporter.GetAtPath(assetPath) is not PluginImporter importer)
            {
                Debug.LogWarning(
                    $"{assetPath} was not imported as a plugin. Set it to Editor only by hand.");
                return;
            }

            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(true);
            importer.SetEditorData("OS", os);
            importer.SetEditorData("CPU", cpu);

            // Turning off "any platform" does not clear the individual targets:
            // Unity leaves some of them switched on, so each one this repository
            // can build for is named here.
            foreach (BuildTarget target in new[]
                     {
                         BuildTarget.Android,
                         BuildTarget.iOS,
                         BuildTarget.StandaloneOSX,
                         BuildTarget.StandaloneWindows,
                         BuildTarget.StandaloneWindows64,
                         BuildTarget.StandaloneLinux64
                     })
            {
                importer.SetCompatibleWithPlatform(target, false);
            }

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
