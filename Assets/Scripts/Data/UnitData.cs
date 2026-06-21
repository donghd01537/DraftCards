using System;
using DraftCards.Core;

namespace DraftCards.Data
{
    [Serializable]
    public class UnitData
    {
        public int attack;
        public int hp;
        public int count;
        public FormationLine spawnLine;
        public UnitType unitType = UnitType.Ground;

        public float moveSpeed = 120f;
        public float attackRange = 40f;
        public float attackCooldown = 1.0f;
        public float attackSpeed = 1f;

        // Travel speed of this unit's thrown projectile, in local units/second. Only used
        // by ranged units (those that throw a projectile). 0 = use the launcher default.
        public float projectileSpeed = 650f;

        // Optional area radius for lobbed projectiles. 0 means a direct single-target hit.
        public float projectileAoeRadius = 0f;

        // Multiplier on the ground shadow size (1 = default). Larger for big units.
        public float shadowScale = 1f;

        // Support healer (Cleric / Shaman): after every `healEveryAttacks` normal attacks,
        // the unit heals every living ally on its own side — itself included — for `healAmount`
        // HP. 0 for either field disables healing. The counter resets each battle.
        public int healEveryAttacks = 0;
        public int healAmount = 0;
    }
}
