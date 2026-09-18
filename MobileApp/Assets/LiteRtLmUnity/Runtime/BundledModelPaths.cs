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
    }
}
