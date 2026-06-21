using System;
using System.Collections.Generic;

namespace DraftCards.Data
{
    // One rung on a unit's Upgrade / Evolution ladder. Levels are applied in order by
    // UpgradeManager; the base card is the un-upgraded state, so the first list entry is
    // the result of upgrading once.
    //
    // Three kinds of rung:
    //  - Stat-only bump: statMultiplier > 1, evolveToId empty, no branches. The unit keeps its
    //    identity and art; its stats scale by statMultiplier (e.g. 1.10 = +10% all stats).
    //  - Identity change (evolution): evolveToId names another CardData. The family switches
    //    to that card — its art, name, and stats (which should already bake in any carried
    //    multiplier from earlier rungs, authored in cards.json). From this point DeckManager
    //    deals the evolved card instead of the base, so the base never reappears this run.
    //  - Branching evolution: branches lists 2+ EvolutionLevel options and the player picks one
    //    (e.g. Knight's 2nd upgrade → Spartan OR Holy Knight). The chosen branch is resolved like
    //    a normal evolution/stat rung; UpgradeManager records which branch was taken so the
    //    family's current card stays correct afterwards. When branches is set, statMultiplier and
    //    evolveToId on THIS rung are ignored — they live on each branch instead.
    [Serializable]
    public class EvolutionLevel
    {
        // Stat scale applied to the base unit at this level (1 = no change, 1.10 = +10%).
        public float statMultiplier = 1f;

        // Optional cardId to evolve into at this level. Empty/null = stat-only upgrade.
        public string evolveToId;

        // Optional branch choices. When non-empty, this rung is a player choice between these
        // options (each its own EvolutionLevel); the rung's own statMultiplier/evolveToId are unused.
        public List<EvolutionLevel> branches;

        public bool HasBranches => branches != null && branches.Count > 0;
    }
}
