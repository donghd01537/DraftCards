#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using DraftCards.Core;
using DraftCards.Data;
using UnityEditor;
using UnityEngine;

namespace DraftCards.EditorTools
{
    public static class CreateStarterCards
    {
        private const string CardOutputFolder = "Assets/Resources/Cards";
        // Enemies live in their own Resources folder so DeckManager (which loads
        // Resources/Cards) never deals them into the player's draftable deck.
        private const string EnemyOutputFolder = "Assets/Resources/Enemies";
        private const string CardArtFolder = "Assets/Art/Cards";
        private const string CharacterArtFolder = "Assets/Art/Characters";
        private const string EnemyArtFolder = "Assets/Art/Enemies";
        private const string ProjectileArtFolder = "Assets/Art/Projectiles";
        // Stats live in this JSON config so designers can tweak cards without
        // editing/recompiling code. Edit it, then re-run "Create Starter Cards".
        private const string ConfigPath = "Assets/Config/cards.json";

        [MenuItem("DraftCards/Create Starter Cards")]
        public static void Create()
        {
            EnsureFolder(CardOutputFolder);
            EnsureFolder(EnemyOutputFolder);

            if (AssetDatabase.IsValidFolder(CharacterArtFolder))
            {
                AssetDatabase.ImportAsset(CharacterArtFolder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            }
            if (AssetDatabase.IsValidFolder(CardArtFolder))
            {
                AssetDatabase.ImportAsset(CardArtFolder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            }
            if (AssetDatabase.IsValidFolder(EnemyArtFolder))
            {
                AssetDatabase.ImportAsset(EnemyArtFolder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            }
            if (AssetDatabase.IsValidFolder(ProjectileArtFolder))
            {
                AssetDatabase.ImportAsset(ProjectileArtFolder, ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.Refresh();

            // Wipe stale cards so deleted variants don't keep showing up in the deck
            foreach (string guid in AssetDatabase.FindAssets("t:CardData", new[] { CardOutputFolder, EnemyOutputFolder }))
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            }

            CardConfig config = LoadConfig();
            if (config == null)
            {
                return;
            }

            if (config.units != null)
            {
                foreach (UnitCardConfig u in config.units)
                {
                    CreateUnitCard(u.id, u.name, u.characterFolder, u.spriteFile,
                        attack: u.attack, hp: u.hp, count: u.count, line: ParseLine(u.line, u.id),
                        moveSpeed: u.moveSpeed, attackRange: u.attackRange, attackCooldown: u.attackCooldown, attackSpeed: u.attackSpeed,
                        unitType: ParseUnitType(u.unitType, u.id), projectileFile: u.projectileSprite, projectileSpeed: u.projectileSpeed,
                        projectileAoeRadius: u.projectileAoeRadius, shadowScale: u.shadowScale,
                        familyRootId: u.familyRootId, excludeFromInitialDeck: u.excludeFromInitialDeck, evolution: u.evolution);
                }
            }

            if (config.supports != null)
            {
                foreach (SupportCardConfig s in config.supports)
                {
                    CreateSupportCard(s.id, s.name, s.mpCost, ParseEffectType(s.effectType, s.id),
                        s.value, s.value2, s.value3, s.spriteFile, s.projectileSprite,
                        s.area, s.spellType, s.shortDescription, s.description, s.excludeFromInitialDeck);
                }
            }

            // Enemy units. Counts and lanes per round are driven by the wave table in
            // GameManager — the card just defines the per-fighter stats and art.
            if (config.enemies != null)
            {
                foreach (EnemyCardConfig e in config.enemies)
                {
                    CreateEnemyCard(e.id, e.name, e.enemyFolder,
                        attack: e.attack, hp: e.hp, line: ParseLine(e.line, e.id),
                        moveSpeed: e.moveSpeed, attackRange: e.attackRange, attackCooldown: e.attackCooldown, attackSpeed: e.attackSpeed,
                        unitType: ParseUnitType(e.unitType, e.id), projectileFile: e.projectileSprite, projectileSpeed: e.projectileSpeed,
                        projectileAoeRadius: e.projectileAoeRadius, shadowScale: e.shadowScale);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[DraftCards] Starter cards created in " + CardOutputFolder);
        }

        private static CardConfig LoadConfig()
        {
            if (!File.Exists(ConfigPath))
            {
                Debug.LogError($"[DraftCards] Card config not found at {ConfigPath}. No cards created.");
                return null;
            }

            try
            {
                string json = File.ReadAllText(ConfigPath);
                CardConfig config = JsonUtility.FromJson<CardConfig>(json);
                if (config == null)
                {
                    Debug.LogError($"[DraftCards] Failed to parse {ConfigPath} (empty or invalid JSON).");
                }
                return config;
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[DraftCards] Error reading {ConfigPath}: {ex.Message}");
                return null;
            }
        }

        private static FormationLine ParseLine(string value, string id)
        {
            if (System.Enum.TryParse(value, true, out FormationLine line))
            {
                return line;
            }
            Debug.LogWarning($"[DraftCards] Card '{id}': invalid line '{value}'. Defaulting to Front. Valid: Back/Middle/Front.");
            return FormationLine.Front;
        }

        private static UnitType ParseUnitType(string value, string id)
        {
            if (System.Enum.TryParse(value, true, out UnitType type))
            {
                return type;
            }
            Debug.LogWarning($"[DraftCards] Card '{id}': invalid unitType '{value}'. Defaulting to Ground. Valid: Ground/Flying.");
            return UnitType.Ground;
        }

        private static SupportEffectType ParseEffectType(string value, string id)
        {
            if (System.Enum.TryParse(value, true, out SupportEffectType type))
            {
                return type;
            }
            Debug.LogWarning($"[DraftCards] Card '{id}': invalid effectType '{value}'. Defaulting to AddAttackFlat.");
            return SupportEffectType.AddAttackFlat;
        }

        private static void CreateUnitCard(string id, string name, string characterFolder, string spriteFile,
            int attack, int hp, int count, FormationLine line,
            float moveSpeed, float attackRange, float attackCooldown, float attackSpeed,
            UnitType unitType, string projectileFile, float projectileSpeed, float projectileAoeRadius, float shadowScale,
            string familyRootId = null, bool excludeFromInitialDeck = false, EvolutionConfig[] evolution = null)
        {
            CardData card = ScriptableObject.CreateInstance<CardData>();
            card.cardId = id;
            card.cardName = name;
            card.cardType = CardType.Unit;
            card.mpCost = 0;
            card.unitData = new UnitData
            {
                attack = attack,
                hp = hp,
                count = count,
                spawnLine = line,
                moveSpeed = moveSpeed,
                attackRange = attackRange,
                attackCooldown = attackCooldown,
                attackSpeed = attackSpeed,
                projectileSpeed = projectileSpeed,
                projectileAoeRadius = Mathf.Max(0f, projectileAoeRadius),
                unitType = unitType,
                shadowScale = shadowScale > 0f ? shadowScale : 1f
            };
            card.supportEffects = new List<SupportEffectData>();
            card.artwork = LoadCardSprite(spriteFile);
            card.idleSprite = LoadCharacterSprite(characterFolder, $"Unit_{characterFolder}_Idle.png");
            card.attackFrames = LoadFramesFromFolder($"{CharacterArtFolder}/{characterFolder}/Attack");
            card.projectileSprite = LoadProjectileSprite(projectileFile);

            // Unit Upgrade / Evolution data.
            card.familyRootId = familyRootId;
            card.excludeFromInitialDeck = excludeFromInitialDeck;
            card.evolutionLevels = new List<EvolutionLevel>();
            if (evolution != null)
            {
                foreach (EvolutionConfig e in evolution)
                {
                    card.evolutionLevels.Add(new EvolutionLevel
                    {
                        statMultiplier = e.statMultiplier > 0f ? e.statMultiplier : 1f,
                        evolveToId = e.evolveToId
                    });
                }
            }

            SaveCardAsset(card, id, CardOutputFolder);
        }

        private static void CreateEnemyCard(string id, string name, string enemyFolder,
            int attack, int hp, FormationLine line,
            float moveSpeed, float attackRange, float attackCooldown, float attackSpeed,
            UnitType unitType, string projectileFile, float projectileSpeed, float projectileAoeRadius, float shadowScale)
        {
            CardData card = ScriptableObject.CreateInstance<CardData>();
            card.cardId = id;
            card.cardName = name;
            card.cardType = CardType.Unit;
            card.mpCost = 0;
            card.unitData = new UnitData
            {
                attack = attack,
                hp = hp,
                count = 1,
                spawnLine = line,
                moveSpeed = moveSpeed,
                attackRange = attackRange,
                attackCooldown = attackCooldown,
                attackSpeed = attackSpeed,
                projectileSpeed = projectileSpeed,
                projectileAoeRadius = Mathf.Max(0f, projectileAoeRadius),
                unitType = unitType,
                shadowScale = shadowScale > 0f ? shadowScale : 1f
            };
            card.supportEffects = new List<SupportEffectData>();
            // Enemy art ships with attack frames only — reuse the first frame as the
            // idle/standing pose so the unit is visible between attacks. (For the Cyclop
            // this is the rock-overhead pose, which reads as a thrower ready to throw.)
            card.attackFrames = LoadFramesFromFolder($"{EnemyArtFolder}/{enemyFolder}/Attack");
            card.idleSprite = card.attackFrames.Count > 0 ? card.attackFrames[0] : null;
            card.artwork = card.idleSprite;
            card.projectileSprite = LoadProjectileSprite(projectileFile);

            SaveCardAsset(card, id, EnemyOutputFolder);
        }

        private static void CreateSupportCard(string id, string name, int cost, SupportEffectType effectType, float value, float value2, float value3,
            string spriteFile = null, string projectileFile = null, string area = null, string spellType = null,
            string shortDescription = null, string description = null, bool excludeFromInitialDeck = false)
        {
            CardData card = ScriptableObject.CreateInstance<CardData>();
            card.cardId = id;
            card.cardName = name;
            card.cardDescription = shortDescription;
            card.cardArea = area;
            card.cardKind = spellType;
            card.rulesText = description;
            card.cardType = CardType.Support;
            card.mpCost = cost;
            card.excludeFromInitialDeck = excludeFromInitialDeck;
            card.unitData = null;
            card.supportEffects = new List<SupportEffectData>
            {
                new() { effectType = effectType, value = value, value2 = value2, value3 = value3 }
            };
            if (!string.IsNullOrWhiteSpace(spriteFile))
            {
                card.artwork = LoadCardSprite(spriteFile);
            }
            card.projectileSprite = LoadProjectileSprite(projectileFile);

            SaveCardAsset(card, id, CardOutputFolder);
        }

        private static Sprite LoadProjectileSprite(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            string path = Path.Combine(ProjectileArtFolder, fileName).Replace('\\', '/');
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[DraftCards] Projectile sprite not found at {path}.");
            }
            return sprite;
        }

        private static Sprite LoadCardSprite(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
            {
                return null;
            }

            string path = Path.Combine(CardArtFolder, fileName).Replace('\\', '/');
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite != null)
            {
                return sprite;
            }

            string expectedName = Path.GetFileNameWithoutExtension(fileName);
            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { CardArtFolder }))
            {
                string candidatePath = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileNameWithoutExtension(candidatePath), expectedName, System.StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                sprite = AssetDatabase.LoadAssetAtPath<Sprite>(candidatePath);
                if (sprite != null)
                {
                    return sprite;
                }
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                Debug.LogWarning($"[DraftCards] Card sprite not found at {path}.");
            }
            return sprite;
        }

        private static Sprite LoadCharacterSprite(string characterFolder, string fileName)
        {
            string path = $"{CharacterArtFolder}/{characterFolder}/{fileName}";
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
            {
                Debug.LogWarning($"[DraftCards] Character sprite not found at {path}.");
            }
            return sprite;
        }

        private static List<Sprite> LoadFramesFromFolder(string folder)
        {
            List<Sprite> frames = new();
            string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { folder });
            List<string> paths = new();
            foreach (string guid in guids)
            {
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            }
            paths.Sort(System.StringComparer.Ordinal);
            foreach (string path in paths)
            {
                Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite != null) frames.Add(sprite);
            }
            if (frames.Count == 0)
            {
                Debug.LogWarning($"[DraftCards] No attack frames found in {folder}.");
            }
            return frames;
        }

        private static void SaveCardAsset(CardData card, string id, string folder)
        {
            string path = $"{folder}/Card_{id}.asset";
            string assetName = $"Card_{id}";
            card.name = assetName;
            CardData existing = AssetDatabase.LoadAssetAtPath<CardData>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(card, existing);
                existing.name = assetName;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(card);
            }
            else
            {
                AssetDatabase.CreateAsset(card, path);
            }
        }

        private static void EnsureFolder(string assetPath)
        {
            if (AssetDatabase.IsValidFolder(assetPath))
            {
                return;
            }

            string parent = Path.GetDirectoryName(assetPath).Replace('\\', '/');
            string leaf = Path.GetFileName(assetPath);

            if (!AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolder(parent);
            }

            AssetDatabase.CreateFolder(parent, leaf);
        }

        // ---- JSON config shapes (mirror Assets/Config/cards.json) ----
        // Enums are stored as strings in JSON for readability, so they are typed
        // as string here and converted via the Parse* helpers above.

        [System.Serializable]
        private class CardConfig
        {
            public UnitCardConfig[] units;
            public EnemyCardConfig[] enemies;
            public SupportCardConfig[] supports;
        }

        [System.Serializable]
        private class UnitCardConfig
        {
            public string id;
            public string name;
            public string characterFolder;
            public string spriteFile;
            public int attack;
            public int hp;
            public int count;
            public string line;
            public float moveSpeed;
            public float attackRange;
            public float attackCooldown;
            public float attackSpeed;
            public string unitType;
            public string projectileSprite;
            public float projectileSpeed;
            public float projectileAoeRadius;
            public float shadowScale;
            // Unit Upgrade / Evolution fields (optional).
            public string familyRootId;
            public bool excludeFromInitialDeck;
            public EvolutionConfig[] evolution;
        }

        [System.Serializable]
        private class EvolutionConfig
        {
            public float statMultiplier;
            public string evolveToId;
        }

        [System.Serializable]
        private class EnemyCardConfig
        {
            public string id;
            public string name;
            public string enemyFolder;
            public int attack;
            public int hp;
            public string line;
            public float moveSpeed;
            public float attackRange;
            public float attackCooldown;
            public float attackSpeed;
            public string unitType;
            public string projectileSprite;
            public float projectileSpeed;
            public float projectileAoeRadius;
            public float shadowScale;
        }

        [System.Serializable]
        private class SupportCardConfig
        {
            public string id;
            public string name;
            public int mpCost;
            public string effectType;
            public float value;
            public float value2;
            public float value3;
            public string spriteFile;
            public string projectileSprite;
            public string area;
            public string spellType;
            public string shortDescription;
            public string description;
            public bool excludeFromInitialDeck;
        }
    }
}
#endif
