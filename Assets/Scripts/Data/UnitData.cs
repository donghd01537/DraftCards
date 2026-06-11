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

        // Multiplier on the ground shadow size (1 = default). Larger for big units.
        public float shadowScale = 1f;
    }
}
