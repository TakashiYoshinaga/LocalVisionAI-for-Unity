using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

public static class LocalVisionAiFontSetup
{
    private const string SourceFontPath =
        "Assets/Fonts/NotoSansJP/NotoSansJP-VariableFont_wght.ttf";

    private const string JapaneseFontAssetPath =
        "Assets/Fonts/NotoSansJP/NotoSansJP Dynamic SDF.asset";

    private const string MainFontAssetPath =
        "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    private static readonly string[] ScenePaths =
    {
        "Assets/Scenes/0-VisionAI-SystemPromptOnly.unity",
        "Assets/Scenes/1-VisionAI-UserPrompt.unity"
    };
    private const string SampleText = "English / 日本語 / 自動販売機";

    [MenuItem("Tools/Local Vision AI/Setup Japanese Font")]
    public static void SetupJapaneseFont()
    {
        AssetDatabase.ImportAsset(
            SourceFontPath,
            ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

        Font sourceFont = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        if (sourceFont == null)
        {
            throw new InvalidOperationException(
                $"Noto Sans JP was not found at {SourceFontPath}.");
        }

        TMP_FontAsset japaneseFont =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(JapaneseFontAssetPath);

        if (japaneseFont == null)
        {
            japaneseFont = CreateJapaneseFontAsset(sourceFont);
        }

        ConfigureJapaneseFontAsset(japaneseFont);
        ConfigureMainFontFallback(japaneseFont);
        foreach (string scenePath in ScenePaths)
        {
            ConfigureVisionAiScene(scenePath);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Local Vision AI: Japanese TMP fallback setup completed.");
    }

    private static TMP_FontAsset CreateJapaneseFontAsset(Font sourceFont)
    {
        TMP_FontAsset fontAsset = TMP_FontAsset.CreateFontAsset(
            sourceFont,
            48,
            5,
            GlyphRenderMode.SDFAA,
            1024,
            1024,
            AtlasPopulationMode.Dynamic,
            true);

        if (fontAsset == null)
        {
            throw new InvalidOperationException(
                "Failed to create the Noto Sans JP TMP font asset.");
        }

        fontAsset.name = "NotoSansJP Dynamic SDF";
        AssetDatabase.CreateAsset(fontAsset, JapaneseFontAssetPath);

        foreach (Texture2D atlasTexture in fontAsset.atlasTextures)
        {
            if (atlasTexture != null && !AssetDatabase.Contains(atlasTexture))
            {
                AssetDatabase.AddObjectToAsset(atlasTexture, fontAsset);
            }
        }

        if (fontAsset.material != null && !AssetDatabase.Contains(fontAsset.material))
        {
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
        }

        return fontAsset;
    }

    private static void ConfigureJapaneseFontAsset(TMP_FontAsset fontAsset)
    {
        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        fontAsset.isMultiAtlasTexturesEnabled = true;
        fontAsset.getFontFeatures = true;

        FaceInfo faceInfo = fontAsset.faceInfo;
        faceInfo.scale = 1f;
        fontAsset.faceInfo = faceInfo;

        SerializedObject serializedFont = new SerializedObject(fontAsset);
        SerializedProperty clearDynamicData =
            serializedFont.FindProperty("m_ClearDynamicDataOnBuild");

        if (clearDynamicData == null)
        {
            throw new InvalidOperationException(
                "TMP clear-dynamic-data setting was not found.");
        }

        clearDynamicData.boolValue = true;
        serializedFont.ApplyModifiedPropertiesWithoutUndo();

        if (!fontAsset.TryAddCharacters(
                SampleText,
                out string missingCharacters,
                true))
        {
            throw new InvalidOperationException(
                $"Noto Sans JP is missing required sample characters: {missingCharacters}");
        }

        EditorUtility.SetDirty(fontAsset);
    }

    private static void ConfigureMainFontFallback(TMP_FontAsset japaneseFont)
    {
        TMP_FontAsset mainFont =
            AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MainFontAssetPath);

        if (mainFont == null)
        {
            throw new InvalidOperationException(
                $"The main TMP font was not found at {MainFontAssetPath}.");
        }

        List<TMP_FontAsset> fallbackFonts = mainFont.fallbackFontAssetTable;
        if (fallbackFonts == null)
        {
            fallbackFonts = new List<TMP_FontAsset>();
            mainFont.fallbackFontAssetTable = fallbackFonts;
        }

        if (!fallbackFonts.Contains(japaneseFont))
        {
            fallbackFonts.Add(japaneseFont);
        }

        EditorUtility.SetDirty(mainFont);
    }

    private static void ConfigureVisionAiScene(string scenePath)
    {
        Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

        GameObject canvasObject = GameObject.Find("Canvas");
        if (canvasObject == null ||
            canvasObject.GetComponent<UnityEngine.Canvas>() == null)
        {
            throw new InvalidOperationException($"Canvas was not found in {scenePath}.");
        }

        RectTransform canvasTransform =
            canvasObject.GetComponent<RectTransform>();
        canvasTransform.localScale = Vector3.one;

        GameObject resultTextObject = GameObject.Find("Canvas/Text (TMP)");
        TMPro.TextMeshProUGUI resultText =
            resultTextObject != null
                ? resultTextObject.GetComponent<TMPro.TextMeshProUGUI>()
                : null;

        if (resultText == null)
        {
            throw new InvalidOperationException(
                $"Canvas/Text (TMP) was not found in {scenePath}.");
        }

        resultText.enableAutoSizing = false;
        resultText.text = SampleText;
        resultText.rectTransform.sizeDelta = new Vector2(720f, 120f);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException($"Failed to save {scenePath}.");
        }
    }
}
