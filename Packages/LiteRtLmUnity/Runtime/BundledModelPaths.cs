namespace LiteRtLmUnity
{
    /// <summary>
    /// Single source of truth for the bundled model's file name and the paths
    /// derived from it. The Editor build step and the runtime bridge must agree on
    /// these, so switching to a different model means editing <see cref="FileName"/>
    /// here and nowhere else.
    /// </summary>
    public static class BundledModelPaths
    {
        /// <summary>
        /// The model to bundle. The master file lives at
        /// <c>MobileApp/LocalModels/&lt;FileName&gt;</c>, outside the asset database.
        /// </summary>
        public const string FileName = "gemma-4-E2B-it.litertlm";

        /// <summary>Folder under StreamingAssets holding the generated parts.</summary>
        public const string StreamingAssetsFolder = "Models";

        /// <summary>Part and sidecar path passed to the Android bridge.</summary>
        public const string AndroidAssetPath = StreamingAssetsFolder + "/" + FileName;

        /// <summary>
        /// This package's name, which is also the root of every asset path inside
        /// it. Renaming the package in <c>package.json</c> means renaming it here.
        /// </summary>
        public const string PackageName = "com.yoshinaga.litertlmunity";

        /// <summary>Asset path of the Kotlin bridge shipped with this package.</summary>
        public const string KotlinPluginAssetPath =
            "Packages/" + PackageName + "/Android/BundledModelBridge.kt";
    }
}
