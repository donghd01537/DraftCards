using System.Collections.Generic;
using DraftCards.Core;
using DraftCards.Data;
using UnityEngine;

namespace DraftCards.Cards
{
    public class PendingUnitBuild
    {
        public int attack;
        public int hp;
        public int count;
        public FormationLine line;
        public Sprite artwork;
        public Sprite idleSprite;
        public List<Sprite> attackFrames;
        public Sprite projectileSprite;
        public string displayName;

        // Identifies which upgrade family this unit belongs to (the base unit card's
        // cardId). The Upgrade spell uses it to match on-field units to a family and to
        // re-skin/scale them when the family evolves. Empty for clones/enemies.
        public string familyId;

        public float moveSpeed;
        public float attackRange;
        public float attackCooldown;
        public float attackSpeed;
        public float projectileSpeed;
        public float projectileAoeRadius;
        public UnitType unitType;
        public float shadowScale = 1f;

        // Support healer cadence/amount (Cleric / Shaman). After every `healEveryAttacks`
        // normal attacks the unit heals all living allies on its side for `healAmount` HP.
        // 0 for either disables it. See BattleUnit.PerformAttack / IBattleSpatial.HealAllies.
        public int healEveryAttacks;
        public int healAmount;

        // Fortify shield window (seconds) to grant the summoned unit, if a Fortify spell
        // was cast on a pending front-line build. 0 means no shield.
        public float shieldDuration;

        // Rally speed buff to grant the summoned unit, if a Rally spell was cast this turn.
        // rallyBonus is the speed fraction (0.4 = +40%); rallyDuration is the window in
        // seconds. 0 duration means no rally.
        public float rallyBonus;
        public float rallyDuration;
        public float damageReduction;
        public bool temporaryBattleOnly;

        public List<CardData> appliedCards = new();

        public PendingUnitBuild() { }

        public PendingUnitBuild(CardData unitCard)
        {
            attack = unitCard.unitData.attack;
            hp = unitCard.unitData.hp;
            count = unitCard.unitData.count;
            line = unitCard.unitData.spawnLine;
            artwork = unitCard.artwork;
            idleSprite = unitCard.idleSprite != null ? unitCard.idleSprite : unitCard.artwork;
            attackFrames = unitCard.attackFrames;
            projectileSprite = unitCard.projectileSprite;
            displayName = unitCard.cardName;
            familyId = !string.IsNullOrEmpty(unitCard.familyRootId) ? unitCard.familyRootId : unitCard.cardId;
            moveSpeed = unitCard.unitData.moveSpeed;
            attackRange = unitCard.unitData.attackRange;
            attackCooldown = unitCard.unitData.attackCooldown;
            attackSpeed = unitCard.unitData.attackSpeed;
            projectileSpeed = unitCard.unitData.projectileSpeed;
            projectileAoeRadius = unitCard.unitData.projectileAoeRadius;
            unitType = unitCard.unitData.unitType;
            shadowScale = unitCard.unitData.shadowScale > 0f ? unitCard.unitData.shadowScale : 1f;
            healEveryAttacks = unitCard.unitData.healEveryAttacks;
            healAmount = unitCard.unitData.healAmount;
            appliedCards.Add(unitCard);
        }
    }
}
