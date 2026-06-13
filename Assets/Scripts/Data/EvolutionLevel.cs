using System;

namespace DraftCards.Data
{
    // One rung on a unit's Upgrade / Evolution ladder. Levels are applied in order by
    // UpgradeManager; the base card is the un-upgraded state, so the first list entry is
    // the result of upgrading once.
    //
    // Two kinds of rung:
    //  - Stat-only bump: statMultiplier > 1, evolveToId empty. The unit keeps its identity
    //    and art; its stats scale by statMultiplier (e.g. 1.10 = +10% all stats).
    //  - Identity change (evolution): evolveToId names another CardData. The family switches
    //    to that card — its art, name, and stats (which should already bake in any carried
    //    multiplier from earlier rungs, authored in cards.json). From this point DeckManager
    //    deals the evolved card instead of the base, so the base never reappears this run.
    [Serializable]
    public class EvolutionLevel
    {
        // Stat scale applied to the base unit at this level (1 = no change, 1.10 = +10%).
        public float statMultiplier = 1f;

        // Optional cardId to evolve into at this level. Empty/null = stat-only upgrade.
        public string evolveToId;
    }
}
