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

        // Current MP cost of the NEXT upgrade. Starts at _startMpCost and rises by
        // _mpCostStep after every successful upgrade, for the whole run.
        private int _currentMpCost;

        // Upgrades already applied to each family, keyed by root cardId. 0 = un-upgraded.
        private readonly Dictionary<string, int> _levelByRoot = new();

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

            public bool IsEvolution => EvolvedCard != null;

            public UpgradeStep(float incrementalMultiplier, CardData evolvedCard, CardData resultCard, bool valid)
            {
                IncrementalMultiplier = incrementalMultiplier;
                EvolvedCard = evolvedCard;
                ResultCard = resultCard;
                Valid = valid;
            }
        }

        // The card a family currently presents (root card stepped through its applied
        // evolutions). Returns null if the root id is unknown.
        public CardData GetCurrentCard(string rootId)
        {
            EnsureCatalog();
            if (string.IsNullOrEmpty(rootId)) return null;
            if (!_unitsById.TryGetValue(rootId, out CardData root)) return null;

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
            if (root.evolutionLevels == null || level >= root.evolutionLevels.Count)
            {
                return default;
            }

            float incrementalMultiplier = root.evolutionLevels[level].statMultiplier;
            if (incrementalMultiplier <= 0f) incrementalMultiplier = 1f;
            CardData evolved = EvolvedCardAtLevel(root, level);
            CardData currentBeforeIdentity = GetCurrentCard(rootId);
            CardData result = evolved != null ? evolved : currentBeforeIdentity;
            return new UpgradeStep(incrementalMultiplier, evolved, result, valid: true);
        }

        // Applies the next upgrade to a family: bumps its level and the run-wide MP cost,
        // and returns the step so callers can scale on-field units and swap the deck pool.
        // Returns an invalid step (and changes nothing) if the family can't upgrade.
        public UpgradeStep ApplyUpgrade(string rootId)
        {
            UpgradeStep step = PreviewNext(rootId);
            if (!step.Valid) return step;

            _levelByRoot[rootId] = GetLevel(rootId) + 1;
            _currentMpCost += _mpCostStep;
            return step;
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
