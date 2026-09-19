using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LiteRtLmUnity
{
    /// <summary>
    /// Prepares the bundled AI model for an Android build.
    ///
    /// The master model lives outside <c>Assets/</c> so that Unity never imports it
    /// and never packages it as a single asset. Android Gradle Plugin reads every
    /// asset into a byte array while packaging, so a single asset of 2 GiB or more
    /// fails the build with "Required array size too large". The master model is
    /// therefore split into parts that stay below that limit, and the Kotlin bridge
    /// joins them back together on the device.
    /// </summary>
    [InitializeOnLoad]
    public sealed class BundledModelBuildSetup : IPreprocessBuildWithReport
    {
        private const string ModelFileName = BundledModelPaths.FileName;

        /// <summary>Master model, outside the Unity asset database.</summary>
        private const string MasterModelPath = "LocalModels/" + ModelFileName;

        private const string StreamingModelsDirectory =
            "Assets/StreamingAssets/" + BundledModelPaths.StreamingAssetsFolder;
        private const string MetadataAssetPath =
            StreamingModelsDirectory + "/" + ModelFileName + ".sha256";

        /// <summary>
        /// Maximum bytes per part. Must stay below <c>int.MaxValue</c> bytes so the
        /// Android Gradle Plugin can package each part.
        /// </summary>
        private const long MaxPartBytes = 1024L * 1024L * 1024L;

        private const int CopyBufferSize = 1024 * 1024;

        private const string KotlinPluginPath =
            BundledModelPaths.KotlinPluginAssetPath;

        public int callbackOrder => -1000;

        static BundledModelBuildSetup()
        {
            EditorApplication.delayCall += ApplyProjectSettings;
        }

        [MenuItem("Tools/Local Vision AI/Setup Bundled Model Build")]
        public static void ApplyProjectSettings()
        {
            ConfigureKotlinPlugin();
        }

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
            {
                return;
            }

            ApplyProjectSettings();
            PrepareBundledModelAssets();
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

        [MenuItem("Tools/Local Vision AI/Prepare Bundled Model Assets")]
        public static void PrepareBundledModelAssets()
        {
            RejectOversizedStreamingModel();

            if (!File.Exists(MasterModelPath))
            {
                throw new BuildFailedException(
                    $"Bundled model is missing. Place it at {MasterModelPath} " +
                    "(this path is outside Assets/ on purpose and is not committed).");
            }

            FileInfo masterFile = new(MasterModelPath);
            if (masterFile.Length <= 0L)
            {
                throw new BuildFailedException("Bundled model file is empty.");
            }

            string hash = ComputeSha256(MasterModelPath);
            int partCount = (int)((masterFile.Length + MaxPartBytes - 1) / MaxPartBytes);

            Directory.CreateDirectory(StreamingModelsDirectory);

            if (PartsAreUpToDate(hash, masterFile.Length, partCount))
            {
                Debug.Log(
                    $"Local Vision AI: bundled model parts are up to date " +
                    $"({masterFile.Length:N0} bytes, {partCount} part(s)).");
                return;
            }

            DeleteExistingParts();
            WriteParts(masterFile.Length, partCount);
            WriteMetadata(hash, masterFile.Length, partCount);

            AssetDatabase.Refresh(
                ImportAssetOptions.ForceSynchronousImport |
                ImportAssetOptions.ForceUpdate);

            Debug.Log(
                $"Local Vision AI: prepared bundled model " +
                $"({masterFile.Length:N0} bytes, {partCount} part(s), SHA-256 {hash}).");
        }

        /// <summary>
        /// A whole model left inside StreamingAssets would be packaged as one asset
        /// and break the Android build once it reaches 2 GiB, so refuse it early
        /// with an actionable message instead of deleting the file.
        /// </summary>
        private static void RejectOversizedStreamingModel()
        {
            string strayModel = StreamingModelsDirectory + "/" + ModelFileName;

            if (File.Exists(strayModel))
            {
                throw new BuildFailedException(
                    $"{strayModel} must not be packaged directly. " +
                    $"Move the model to {MasterModelPath}; the build splits it into " +
                    "parts automatically.");
            }
        }

        private static bool PartsAreUpToDate(string hash, long size, int partCount)
        {
            if (!File.Exists(MetadataAssetPath))
            {
                return false;
            }

            string expected = FormatMetadata(hash, size, partCount);
            if (File.ReadAllText(MetadataAssetPath) != expected)
            {
                return false;
            }

            long total = 0L;
            for (int index = 0; index < partCount; index++)
            {
                string path = GetPartPath(index);
                if (!File.Exists(path))
                {
                    return false;
                }

                total += new FileInfo(path).Length;
            }

            return total == size;
        }

        private static void DeleteExistingParts()
        {
            foreach (string path in
                     Directory.EnumerateFiles(
                         StreamingModelsDirectory, ModelFileName + ".part*"))
            {
                File.Delete(path);
            }
        }

        private static void WriteParts(long totalSize, int partCount)
        {
            byte[] buffer = new byte[CopyBufferSize];

            using FileStream source = new(
                MasterModelPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.SequentialScan);

            try
            {
                for (int index = 0; index < partCount; index++)
                {
                    long remaining = Math.Min(MaxPartBytes, totalSize - index * MaxPartBytes);

                    EditorUtility.DisplayProgressBar(
                        "Local Vision AI",
                        $"Writing bundled model part {index + 1} of {partCount}...",
                        (float)index / partCount);

                    using FileStream destination = new(
                        GetPartPath(index),
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        CopyBufferSize);

                    while (remaining > 0L)
                    {
                        int wanted = (int)Math.Min(buffer.Length, remaining);
                        int read = source.Read(buffer, 0, wanted);

                        if (read <= 0)
                        {
                            throw new BuildFailedException(
                                "Bundled model ended earlier than its reported size.");
                        }

                        destination.Write(buffer, 0, read);
                        remaining -= read;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void WriteMetadata(string hash, long size, int partCount)
        {
            File.WriteAllText(
                MetadataAssetPath,
                FormatMetadata(hash, size, partCount),
                new UTF8Encoding(false));
        }

        private static string FormatMetadata(string hash, long size, int partCount)
        {
            return $"sha256={hash}\nsize={size}\nparts={partCount}\n";
        }

        private static string GetPartPath(int index)
        {
            return $"{StreamingModelsDirectory}/{ModelFileName}.part{index:D3}";
        }

        private static string ComputeSha256(string path)
        {
            using FileStream stream = new(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                CopyBufferSize,
                FileOptions.SequentialScan);
            using SHA256 sha256 = SHA256.Create();

            return BitConverter.ToString(sha256.ComputeHash(stream))
                .Replace("-", string.Empty)
                .ToLowerInvariant();
        }
    }
}
