using System.Collections.Generic;
using DraftCards.Data;
using UnityEngine;

namespace DraftCards.Managers
{
    // Tracks the Unit Upgrade / Evolution system: how many times each unit family has been
    // upgraded this run, and the escalating MP cost of the next upgrade. This is the single
    // source of truth for upgrade state; it persists for the whole run (never resets).
    //
    // A "family" is identified by the root (base) unit's cardId — the un-upgraded form.
    // On-field units carry that root as their FamilyId even after evolving, so an evolved
    // Spartan still belongs to the knight_3 family and is matched here.
    public class UpgradeManager : MonoBehaviour
    {
        [SerializeField] private string _resourcesFolder = "Cards";
        [SerializeField] private int _startMpCost = 5;
        [SerializeField] private int _mpCostStep = 1;
        // Fallback upgrade for a family with no (or an exhausted) evolution ladder: a flat
        // +10% to HP and Attack, repeatable forever. The spec: "any unit can upgrade; if no
        // evolution tree, just +10% to (Hp/Attack)."
        [SerializeField] private float _genericStatMultiplier = 1.10f;

        // Current MP cost of the NEXT upgrade. Starts at _startMpCost and rises by
        // _mpCostStep after every successful upgrade, for the whole run.
        private int _currentMpCost;

        // Upgrades already applied to each family, keyed by root cardId. 0 = un-upgraded.
        private readonly Dictionary<string, int> _levelByRoot = new();

        // The cardId a family evolved into, keyed by root cardId. Set when an evolution rung is
        // applied — including a chosen branch (Spartan vs Holy Knight) — so GetCurrentCard can
        // reconstruct the family's current form when the ladder branched and the level counter
        // alone no longer determines the path. Absent for families still on their base form.
        private readonly Dictionary<string, string> _evolvedIdByRoot = new();

        // All unit CardData, indexed by cardId, loaded once from Resources.
        private Dictionary<string, CardData> _unitsById;

        public int CurrentMpCost => _currentMpCost;

        private void Awake()
        {
            _currentMpCost = _startMpCost;
        }

        // Result of applying (or previewing) an upgrade on a family.
        public readonly struct UpgradeStep
        {
            // Stat scale of THIS rung alone (1.10 = +10%), to apply to a unit's current stats
            // for a stat-only upgrade. For an evolve step the new stats come from EvolvedCard
            // instead (its authored stats already bake in any carried multiplier), so callers
            // should re-skin rather than multiply.
            public readonly float IncrementalMultiplier;
            // Non-null when this upgrade changes the family's identity (evolution).
            public readonly CardData EvolvedCard;
            // The display card for the family AFTER this upgrade (evolved card if any, else
            // the current root card). Used to show the resulting form in the picker.
            public readonly CardData ResultCard;
            public readonly bool Valid;
            // True for the generic fallback rung (no evolution ladder): scale ONLY HP and
            // Attack by IncrementalMultiplier, leaving speed/range/attack-speed untouched.
            // A ladder rung scales every stat instead.
            public readonly bool HpAttackOnly;
            // For a branching rung (e.g. Knight's 2nd upgrade → Spartan OR Holy Knight): the
            // resolved option cards the player must choose between. Empty for a normal rung.
            // When set, the caller must pick one (its cardId) and pass it to ApplyUpgrade so the
            // step is resolved to that single evolution before being applied to the field.
            public readonly IReadOnlyList<CardData> BranchOptions;

            public bool IsEvolution => EvolvedCard != null;
            public bool IsBranchChoice => BranchOptions != null && BranchOptions.Count > 0;

            public UpgradeStep(float incrementalMultiplier, CardData evolvedCard, CardData resultCard, bool valid, bool hpAttackOnly = false, IReadOnlyList<CardData> branchOptions = null)
            {
                IncrementalMultiplier = incrementalMultiplier;
                EvolvedCard = evolvedCard;
                ResultCard = resultCard;
                Valid = valid;
                HpAttackOnly = hpAttackOnly;
                BranchOptions = branchOptions;
            }
        }

        // The card a family currently presents (root card stepped through its applied
        // evolutions). Returns null if the root id is unknown.
        //
        // Once the family has taken an evolution (recorded in _evolvedIdByRoot, including a chosen
        // branch like Spartan vs Holy Knight) that card IS the current form — it's terminal in the
        // base ladder, and generic post-evolution rungs leave identity unchanged. Otherwise we
        // walk the un-branched ladder by level, which only contains stat-only/single-evolve rungs
        // reachable without a choice.
        public CardData GetCurrentCard(string rootId)
        {
            EnsureCatalog();
            if (string.IsNullOrEmpty(rootId)) return null;
            if (!_unitsById.TryGetValue(rootId, out CardData root)) return null;

            if (_evolvedIdByRoot.TryGetValue(rootId, out string evolvedId)
                && !string.IsNullOrEmpty(evolvedId)
                && _unitsById.TryGetValue(evolvedId, out CardData evolvedCard))
            {
                return evolvedCard;
            }

            CardData current = root;
            int level = GetLevel(rootId);
            for (int i = 0; i < level; i++)
            {
                CardData evolved = EvolvedCardAtLevel(root, i);
                if (evolved != null) current = evolved;
            }
            return current;
        }

        public int GetLevel(string rootId)
        {
            if (string.IsNullOrEmpty(rootId)) return 0;
            return _levelByRoot.TryGetValue(rootId, out int level) ? level : 0;
        }

        // True if this family has an upgrade level beyond its current one.
        public bool CanUpgrade(string rootId)
        {
            return PreviewNext(rootId).Valid;
        }

        // Describes the NEXT upgrade for a family without applying it. Invalid step if the
        // family is unknown or already at max level.
        public UpgradeStep PreviewNext(string rootId)
        {
            EnsureCatalog();
            if (string.IsNullOrEmpty(rootId) || !_unitsById.TryGetValue(rootId, out CardData root))
            {
                return default;
            }

            int level = GetLevel(rootId);
            CardData currentCard = GetCurrentCard(rootId);

            // No authored ladder, or the family has walked the whole ladder: fall back to a
            // generic, repeatable +10% HP/Attack bump so every unit can keep upgrading. We
            // count consumed ladder rungs as part of the level, so generic rungs stack on top.
            // A family that took a branch evolution is treated as past the ladder (the override
            // in _evolvedIdByRoot is terminal), so it gets the generic fallback from here on.
            int ladderRungs = root.evolutionLevels != null ? root.evolutionLevels.Count : 0;
            if (level >= ladderRungs || _evolvedIdByRoot.ContainsKey(rootId))
            {
                float genericMultiplier = _genericStatMultiplier > 0f ? _genericStatMultiplier : 1.10f;
                return new UpgradeStep(genericMultiplier, evolvedCard: null, resultCard: currentCard, valid: true, hpAttackOnly: true);
            }

            EvolutionLevel rung = root.evolutionLevels[level];

            // Branching rung: the player must choose one of the options. Report a branch-choice
            // step carrying the resolved option cards; the actual evolution is resolved in
            // ApplyUpgrade once a branch is picked.
            if (rung.HasBranches)
            {
                List<CardData> options = new();
                foreach (EvolutionLevel branch in rung.branches)
                {
                    CardData option = ResolveBranchCard(branch);
                    if (option != null) options.Add(option);
                }
                if (options.Count > 0)
                {
                    return new UpgradeStep(1f, evolvedCard: null, resultCard: currentCard, valid: true, branchOptions: options);
                }
                // Misconfigured rung (no resolvable options) — fall through to generic.
                float genericMultiplier = _genericStatMultiplier > 0f ? _genericStatMultiplier : 1.10f;
                return new UpgradeStep(genericMultiplier, evolvedCard: null, resultCard: currentCard, valid: true, hpAttackOnly: true);
            }

            return BuildRungStep(rung, level, root, currentCard);
        }

        // Builds the concrete UpgradeStep for a single (non-branching) ladder rung.
        private UpgradeStep BuildRungStep(EvolutionLevel rung, int levelIndex, CardData root, CardData currentCard)
        {
            float incrementalMultiplier = rung.statMultiplier;
            if (incrementalMultiplier <= 0f) incrementalMultiplier = 1f;
            CardData evolved = EvolvedCardAtLevel(root, levelIndex);
            CardData result = evolved != null ? evolved : currentCard;
            return new UpgradeStep(incrementalMultiplier, evolved, result, valid: true);
        }

        // Resolves the destination card of a single branch option (its evolveToId), or null.
        private CardData ResolveBranchCard(EvolutionLevel branch)
        {
            if (branch == null || string.IsNullOrEmpty(branch.evolveToId)) return null;
            EnsureCatalog();
            _unitsById.TryGetValue(branch.evolveToId, out CardData card);
            return card;
        }

        // Applies the next upgrade to a family: bumps its level and the run-wide MP cost,
        // and returns the step so callers can scale on-field units and swap the deck pool.
        // Returns an invalid step (and changes nothing) if the family can't upgrade.
        //
        // chosenEvolveToId selects a branch when the next rung is a player choice (e.g. Spartan
        // vs Holy Knight). It is ignored for non-branching rungs. A branching rung with no (or an
        // unrecognized) choice does nothing and returns an invalid step, so the caller re-prompts.
        public UpgradeStep ApplyUpgrade(string rootId, string chosenEvolveToId = null)
        {
            UpgradeStep step = PreviewNext(rootId);
            if (!step.Valid) return step;

            if (step.IsBranchChoice)
            {
                step = ResolveBranchStep(rootId, chosenEvolveToId);
                if (!step.Valid) return step;
            }

            // Record an evolution (single or chosen branch) so GetCurrentCard tracks the family's
            // form even when the ladder branched and the level counter alone can't.
            if (step.IsEvolution && step.EvolvedCard != null && !string.IsNullOrEmpty(step.EvolvedCard.cardId))
            {
                _evolvedIdByRoot[rootId] = step.EvolvedCard.cardId;
            }

            _levelByRoot[rootId] = GetLevel(rootId) + 1;
            _currentMpCost += _mpCostStep;
            return step;
        }

        // Resolves a branching rung to the concrete evolution step for the player's chosen option.
        // Returns an invalid step if no rung is branching or the choice isn't one of its options.
        private UpgradeStep ResolveBranchStep(string rootId, string chosenEvolveToId)
        {
            EnsureCatalog();
            if (string.IsNullOrEmpty(chosenEvolveToId)
                || !_unitsById.TryGetValue(rootId, out CardData root)
                || root.evolutionLevels == null)
            {
                return default;
            }

            int level = GetLevel(rootId);
            if (level < 0 || level >= root.evolutionLevels.Count) return default;
            EvolutionLevel rung = root.evolutionLevels[level];
            if (!rung.HasBranches) return default;

            foreach (EvolutionLevel branch in rung.branches)
            {
                if (branch == null || !string.Equals(branch.evolveToId, chosenEvolveToId)) continue;
                CardData evolved = ResolveBranchCard(branch);
                if (evolved == null) return default;
                float multiplier = branch.statMultiplier > 0f ? branch.statMultiplier : 1f;
                return new UpgradeStep(multiplier, evolved, evolved, valid: true);
            }
            return default;
        }

        // The evolved CardData introduced at a given level index (0-based into
        // evolutionLevels), or null if that rung is a stat-only bump or out of range.
        private CardData EvolvedCardAtLevel(CardData root, int levelIndex)
        {
            if (root.evolutionLevels == null || levelIndex < 0 || levelIndex >= root.evolutionLevels.Count)
            {
                return null;
            }
            string evolveToId = root.evolutionLevels[levelIndex].evolveToId;
            if (string.IsNullOrEmpty(evolveToId)) return null;
            EnsureCatalog();
            _unitsById.TryGetValue(evolveToId, out CardData evolved);
            return evolved;
        }

        private void EnsureCatalog()
        {
            if (_unitsById != null) return;
            _unitsById = new Dictionary<string, CardData>();
            foreach (CardData card in Resources.LoadAll<CardData>(_resourcesFolder))
            {
                if (card != null && !string.IsNullOrEmpty(card.cardId))
                {
                    _unitsById[card.cardId] = card;
                }
            }
        }
    }
}
