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
        public CardType cardType;
        public Sprite artwork;
        public int mpCost;

        public UnitData unitData;
        public List<SupportEffectData> supportEffects;

        public Sprite idleSprite;
        public List<Sprite> attackFrames;

        // Optional ranged projectile. When set, the unit throws this sprite at its
        // target on attack instead of dealing damage instantly (e.g. Cyclop's rock).
        public Sprite projectileSprite;
    }
}
