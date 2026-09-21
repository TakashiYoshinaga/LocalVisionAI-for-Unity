// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Takashi Yoshinaga

#if UNITY_EDITOR

using System.IO;
using UnityEditor;
using UnityEngine;

namespace LiteRtLmUnity
{
    /// <summary>
    /// What the in-Editor backend needs that a built player never does: which
    /// model file to open, and which still image to analyze in place of the
    /// camera.
    /// </summary>
    /// <remarks>
    /// This lives in a settings asset rather than on the Scene's components so
    /// that every sample Scene picks it up without being modified. The asset is
    /// created under <c>Assets/Editor/</c>, which Unity leaves out of builds.
    /// </remarks>
    public sealed class EditorLlmSettings : ScriptableObject
    {
        /// <summary>Where <see cref="LoadOrCreate"/> puts a newly created asset.</summary>
        public const string DefaultAssetPath = "Assets/Editor/LiteRtLmEditorSettings.asset";

        [Header("Model")]
        [Tooltip("Absolute path of the .litertlm file, or a path relative to the " +
                 "Unity project folder. Leave this empty to search the LocalModels " +
                 "folder of this project and of the other Unity projects beside it.")]
        [SerializeField] private string _modelFilePath = "";

        [Tooltip("Use the GPU (Metal on macOS), which is several times faster than " +
                 "the CPU. The backend falls back to the CPU on its own when the GPU " +
                 "cannot load the model.")]
        [SerializeField] private bool _preferGpu = true;

        [Header("Camera Replacement")]
        [Tooltip("Sent to the model instead of a camera frame while playing in the " +
                 "Editor. The texture must have Read/Write enabled in its import " +
                 "settings, because the image is encoded to JPEG on the CPU.")]
        [SerializeField] private Texture2D _testImage;

        public bool PreferGpu => _preferGpu;

        public Texture2D TestImage => _testImage;

        /// <summary>
        /// The configured model file, or the first one found beside the project
        /// when no path is set. Returns an empty string when nothing was found.
        /// </summary>
        public string ResolveModelFilePath()
        {
            if (!string.IsNullOrWhiteSpace(_modelFilePath))
            {
                return Path.IsPathRooted(_modelFilePath)
                    ? _modelFilePath
                    : Path.GetFullPath(Path.Combine(ProjectRoot, _modelFilePath));
            }

            string ownCopy = Path.Combine(ProjectRoot, "LocalModels", BundledModelPaths.FileName);
            if (File.Exists(ownCopy))
            {
                return ownCopy;
            }

            // The repository holds more than one Unity project and the model is
            // 2.6 GB, so a second copy is a real cost. Reuse a sibling project's
            // copy rather than asking for one per project.
            string siblingRoot = Directory.GetParent(ProjectRoot)?.FullName;
            if (siblingRoot == null)
            {
                return string.Empty;
            }

            foreach (string sibling in Directory.GetDirectories(siblingRoot))
            {
                string candidate = Path.Combine(sibling, "LocalModels", BundledModelPaths.FileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Finds the settings asset, creating one at
        /// <see cref="DefaultAssetPath"/> the first time it is needed.
        /// </summary>
        public static EditorLlmSettings LoadOrCreate()
        {
            string[] existing = AssetDatabase.FindAssets($"t:{nameof(EditorLlmSettings)}");
            if (existing.Length > 0)
            {
                return AssetDatabase.LoadAssetAtPath<EditorLlmSettings>(
                    AssetDatabase.GUIDToAssetPath(existing[0]));
            }

            string directory = Path.GetDirectoryName(DefaultAssetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                AssetDatabase.Refresh();
            }

            var created = CreateInstance<EditorLlmSettings>();
            AssetDatabase.CreateAsset(created, DefaultAssetPath);
            AssetDatabase.SaveAssets();
            Debug.Log(
                $"Created {DefaultAssetPath}. Assign a Test Image on it to analyze " +
                "a picture instead of the camera while playing in the Editor.");
            return created;
        }

        /// <summary>The folder that holds <c>Assets/</c>, without a trailing slash.</summary>
        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }
}

#endif
