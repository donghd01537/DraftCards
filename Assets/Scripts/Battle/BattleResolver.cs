using DraftCards.Units;

namespace DraftCards.Battle
{
    public class BattleResolver
    {
        public BattleResult ResolveAttack(UnitGroup attacker, UnitGroup defender)
        {
            int damage = attacker.TotalDamage;
            int countBefore = defender.Count;
            defender.TakeDamage(damage);

            return new BattleResult
            {
                Attacker = attacker,
                Defender = defender,
                DamageDealt = damage,
                DefenderCountLost = countBefore - defender.Count,
                DefenderKilled = defender.IsDead
            };
        }
    }

    public class BattleResult
    {
        public UnitGroup Attacker;
        public UnitGroup Defender;
        public int DamageDealt;
        public int DefenderCountLost;
        public bool DefenderKilled;
    }
}
