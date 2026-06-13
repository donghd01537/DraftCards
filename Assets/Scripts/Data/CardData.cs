using System.Collections.Generic;
using DraftCards.Core;
using UnityEngine;

namespace DraftCards.Data
{
    [CreateAssetMenu(menuName = "DraftCards/Card", fileName = "NewCard")]
    public class CardData : ScriptableObject
    {
        public string cardId;
        public string cardName;
        public string cardDescription;
        public string cardArea;
        public string cardKind;
        public string rulesText;
        public CardType cardType;
        public Sprite artwork;
        public int mpCost;
        public bool temporary;

        public UnitData unitData;
        public List<SupportEffectData> supportEffects;

        public Sprite idleSprite;
        public List<Sprite> attackFrames;

        // Optional ranged projectile. When set, the unit throws this sprite at its
        // target on attack instead of dealing damage instantly (e.g. Cyclop's rock).
        public Sprite projectileSprite;

        // Unit Upgrade / Evolution ladder for this unit card. Index 0 is the FIRST
        // upgrade (the base card is the un-upgraded state), so evolutionLevels[0] is the
        // result of upgrading once. Empty for non-upgradeable units. See UpgradeManager.
        public List<EvolutionLevel> evolutionLevels;

        // When true, this card never enters the auto-built starting deck (DeckManager)
        // or temporary card rolls. Used for evolved forms (e.g. Spartan) and inactive
        // cards kept in data for possible later reuse.
        public bool excludeFromInitialDeck;

        // The cardId of the base (un-upgraded) unit at the root of this card's evolution
        // family. The base card points to itself; an evolved form (e.g. Spartan) points
        // back to its base (e.g. knight_3). UpgradeManager keys a family's upgrade level by
        // this root, and on-field units carry it as their FamilyId so they keep matching the
        // same family across evolutions. Empty for non-upgradeable units.
        public string familyRootId;
    }
}
