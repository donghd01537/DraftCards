#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace DraftCards.EditorTools
{
    public class CharacterArtPostprocessor : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Contains("/Art/Characters/")
                && !assetPath.Contains("/Art/Cards/")
                && !assetPath.Contains("/Art/Enemies/")
                && !assetPath.Contains("/Art/Effects/")
                && !assetPath.Contains("/Art/Projectiles/"))
            {
                return;
            }

            TextureImporter importer = (TextureImporter)assetImporter;
            if (importer.textureType == TextureImporterType.Sprite)
            {
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 100;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.alphaIsTransparency = true;
        }
    }
}
#endif
