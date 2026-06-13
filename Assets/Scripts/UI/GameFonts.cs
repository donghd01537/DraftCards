using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;

namespace DraftCards.UI
{
    public enum GameFontRole
    {
        Normal,
        Title
    }

    public static class GameFonts
    {
        private const string LatoResourcePath = "Fonts/Lato-Regular";
        private const string PatrickHandResourcePath = "Fonts/PatrickHandSC-Regular";
        private const string LatoFontAssetResourcePath = "Fonts/Lato SDF";
        private const string PatrickHandFontAssetResourcePath = "Fonts/Patrick Hand SC SDF";
        private const string TmpFallbackFontAssetResourcePath = "Fonts & Materials/LiberationSans SDF";

        private static TMP_FontAsset _normalFont;
        private static TMP_FontAsset _titleFont;
        private static TMP_FontAsset _originalDefaultFont;
        private static bool _initialized;
        private static bool _warnedMissingFont;

        public static TMP_FontAsset NormalFont => GetOrCreateFont(GameFontRole.Normal);
        public static TMP_FontAsset TitleFont => GetOrCreateFont(GameFontRole.Title);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeOnLoad()
        {
            InitializeDefaults();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            ApplyToLoadedText();
        }

        public static void Apply(TMP_Text text, GameFontRole role)
        {
            if (text == null)
            {
                return;
            }

            TMP_FontAsset font = GetUsableFont(role);
            if (font != null)
            {
                text.font = font;
                text.enabled = true;
                return;
            }

            text.enabled = false;
            if (!_warnedMissingFont)
            {
                Debug.LogWarning("[DraftCards] No usable TextMeshPro font asset found. Text rendering disabled for affected labels.");
                _warnedMissingFont = true;
            }
        }

        public static void ApplyToLoadedText()
        {
            InitializeDefaults();

            foreach (TMP_Text text in FindLoadedText())
            {
                Apply(text, InferRole(text));
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            ApplyToLoadedText();
        }

        private static void InitializeDefaults()
        {
            if (_initialized)
            {
                return;
            }

            _originalDefaultFont = TMP_Settings.defaultFontAsset;
            TMP_FontAsset normal = GetUsableFont(GameFontRole.Normal);
            AddFallback(normal, _originalDefaultFont);
            AddFallback(_titleFont, _originalDefaultFont);
            if (normal != null)
            {
                TMP_Settings.defaultFontAsset = normal;
            }

            _initialized = true;
        }

        private static TMP_FontAsset GetOrCreateFont(GameFontRole role)
        {
            if (role == GameFontRole.Title)
            {
                if (_titleFont == null)
                {
                    _titleFont = LoadOrCreateFontAsset(
                        PatrickHandResourcePath,
                        PatrickHandFontAssetResourcePath,
                        "Patrick Hand SC SDF");
                }

                return _titleFont;
            }

            if (_normalFont == null)
            {
                _normalFont = LoadOrCreateFontAsset(LatoResourcePath, LatoFontAssetResourcePath, "Lato SDF");
            }

            return _normalFont;
        }

        private static TMP_FontAsset LoadOrCreateFontAsset(
            string sourceResourcePath,
            string fontAssetResourcePath,
            string assetName)
        {
            TMP_FontAsset savedFontAsset = Resources.Load<TMP_FontAsset>(fontAssetResourcePath);
            if (IsUsable(savedFontAsset))
            {
                AddFallback(savedFontAsset, _originalDefaultFont);
                return savedFontAsset;
            }

            if (!Application.isPlaying)
            {
                return null;
            }

            return CreateRuntimeFontAsset(sourceResourcePath, assetName);
        }

        private static TMP_FontAsset GetUsableFont(GameFontRole role)
        {
            TMP_FontAsset requested = GetOrCreateFont(role);
            if (IsUsable(requested)) return requested;

            TMP_FontAsset tmpDefault = TMP_Settings.defaultFontAsset;
            if (IsUsable(tmpDefault)) return tmpDefault;

            TMP_FontAsset fallback = Resources.Load<TMP_FontAsset>(TmpFallbackFontAssetResourcePath);
            return IsUsable(fallback) ? fallback : null;
        }

        private static bool IsUsable(TMP_FontAsset font)
        {
            try
            {
                return font != null && font.material != null;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private static TMP_FontAsset CreateRuntimeFontAsset(string resourcePath, string assetName)
        {
            Font sourceFont = Resources.Load<Font>(resourcePath);
            if (sourceFont == null)
            {
                Debug.LogWarning($"[DraftCards] Font not found at Resources/{resourcePath}.");
                return null;
            }

            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(
                sourceFont,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);

            if (font == null)
            {
                return null;
            }

            font.name = assetName;
            AddFallback(font, _originalDefaultFont);
            return font;
        }

        private static GameFontRole InferRole(TMP_Text text)
        {
            string objectName = text.gameObject.name;
            if (Contains(objectName, "Title") ||
                Contains(objectName, "Header") ||
                Contains(objectName, "Label") ||
                Contains(objectName, "NameText"))
            {
                return GameFontRole.Title;
            }

            return GameFontRole.Normal;
        }

        private static bool Contains(string value, string fragment)
        {
            return value != null && value.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static TMP_Text[] FindLoadedText()
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<TMP_Text>(true);
#endif
        }

        private static void AddFallback(TMP_FontAsset font, TMP_FontAsset fallback)
        {
            if (font == null || fallback == null || font == fallback)
            {
                return;
            }

            if (font.fallbackFontAssetTable == null)
            {
                font.fallbackFontAssetTable = new List<TMP_FontAsset>();
            }
            if (!font.fallbackFontAssetTable.Contains(fallback))
            {
                font.fallbackFontAssetTable.Add(fallback);
            }
        }
    }
}
