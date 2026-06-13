using System;
using System.Collections.Generic;
using DraftCards.Cards;
using DraftCards.Core;
using DraftCards.Data;
using DraftCards.UI;
using DraftCards.Units;
using UnityEngine;

namespace DraftCards.Managers
{
    public class CardPlayManager : MonoBehaviour
    {
        private void Awake()
        {
            if (_battlefieldView == null)
            {
                _battlefieldView = FindFirstObjectByType<BattlefieldView>();
            }
            // Defensive fallbacks so the Upgrade Unit spell works even if the scene predates
            // these serialized refs (e.g. wasn't rebuilt after they were added).
            if (_battlefieldManager == null)
            {
                _battlefieldManager = FindFirstObjectByType<BattlefieldManager>();
            }
            if (_upgradeManager == null)
            {
                _upgradeManager = FindFirstObjectByType<UpgradeManager>();
            }
        }

        [SerializeField] private MPManager _mpManager;
        [SerializeField] private HandManager _handManager;
        [SerializeField] private DeckManager _deckManager;
        [SerializeField] private BattlefieldView _battlefieldView;
        [SerializeField] private BattlefieldManager _battlefieldManager;
        [SerializeField] private UpgradeManager _upgradeManager;

        private PendingUnitBuild _pendingUnitBuild;
        private bool _unitCardPlayedThisTurn;
        private int _holdSpellLimitThisTurn;
        private readonly HashSet<CardData> _spentCardsThisTurn = new();

        public event Action<PendingUnitBuild> OnPendingBuildChanged;
        public event Action OnCardPlayed;
        // Raised when an Upgrade Unit card is played. The UI opens the target-selection modal
        // in response, then calls CommitUpgrade once the player picks a family (or does nothing
        // on cancel). The card is not consumed and no MP is spent until CommitUpgrade.
        public event Action<CardData> OnUpgradeRequested;
        // Raised when an Emergency Draft card is played. The UI opens a modal listing two
        // randomly-rolled draftable units; the player picks one and CommitEmergencyDraft
        // spawns it as a temporary reinforcement. Like Upgrade, the card is not consumed and
        // no MP is spent until the player confirms a choice (Cancel keeps the card).
        public event Action<CardData> OnEmergencyDraftRequested;

        public UpgradeManager UpgradeManager => _upgradeManager;

        public PendingUnitBuild PendingUnitBuild => _pendingUnitBuild;
        public bool HasPendingUnit => _pendingUnitBuild != null;

        public bool CanPlayCard(CardData card)
        {
            if (card == null) return false;
            if (_spentCardsThisTurn.Contains(card)) return false;

            if (card.cardType == CardType.Unit)
            {
                return !_unitCardPlayedThisTurn;
            }

            if (card.cardType == CardType.Support)
            {
                // Upgrade Unit has a dynamic MP cost and needs at least one upgradeable unit
                // already on the field; it is otherwise a battlefield spell.
                if (IsUpgradeUnitCard(card))
                {
                    if (!_mpManager.CanPay(EffectiveMpCost(card))) return false;
                    return GetUpgradeableFamiliesOnField().Count > 0;
                }

                if (!_mpManager.CanPay(card.mpCost)) return false;
                if (IsBattlefieldSpell(card)) return true;
                return _pendingUnitBuild != null;
            }

            return false;
        }

        public bool TryPlayCard(CardData card)
        {
            return TryPlayCard(card, null, consume: false);
        }

        public bool TryPlayCard(CardData card, FormationLine? targetLane)
        {
            return TryPlayCard(card, targetLane, consume: false);
        }

        public bool TryPlayCard(CardData card, FormationLine? targetLane, bool consume)
        {
            bool canPlay = CanPlayCard(card);
            Debug.Log($"[CardPlayManager] TryPlayCard card={card?.cardName} canPlay={canPlay} target={targetLane?.ToString() ?? "null"} consume={consume} mp={_mpManager?.CurrentMp}");
            if (!canPlay) return false;
            if (RequiresLaneTarget(card) && !targetLane.HasValue) return false;

            if (card.cardType == CardType.Unit)
            {
                _pendingUnitBuild = new PendingUnitBuild(card);
                _unitCardPlayedThisTurn = true;
                _spentCardsThisTurn.Add(card);
                foreach (CardData removed in _handManager.RemoveWhere(c => c != null && c.cardType == CardType.Unit))
                {
                    // Unit choices leave the hand as soon as one unit is selected.
                    _deckManager.Discard(removed);
                }
                OnPendingBuildChanged?.Invoke(_pendingUnitBuild);
                OnCardPlayed?.Invoke();
                return true;
            }

            // Upgrade Unit defers resolution to a target-selection modal: don't spend MP or
            // remove the card here. The UI opens the picker; CommitUpgrade finishes the play.
            if (IsUpgradeUnitCard(card))
            {
                OnUpgradeRequested?.Invoke(card);
                return true;
            }

            // Emergency Draft defers like Upgrade: open a unit-choice modal instead of resolving
            // now. CommitEmergencyDraft finishes the play once the player picks a unit.
            if (IsEmergencyDraftCard(card))
            {
                OnEmergencyDraftRequested?.Invoke(card);
                return true;
            }

            if (!_mpManager.Spend(card.mpCost)) return false;

            bool isBattlefieldSpell = IsBattlefieldSpell(card);
            if (isBattlefieldSpell)
            {
                ApplyBattlefieldEffects(card, targetLane);
            }
            else
            {
                ApplySupport(_pendingUnitBuild, card);
            }

            // Spell cards always leave the hand and are removed from the deck for the rest of the run.
            _handManager.Remove(card);

            // Battlefield spells do not rebuild previews; only support cards mutate the pending build.
            if (!isBattlefieldSpell)
            {
                OnPendingBuildChanged?.Invoke(_pendingUnitBuild);
            }
            OnCardPlayed?.Invoke();
            return true;
        }

        // Finishes an Upgrade Unit play once the player picks a family in the modal. Spends the
        // (dynamic) MP cost, advances the family's upgrade level, applies the result to on-field
        // units and the pending summon, swaps the evolved card into the deck pool, then discards
        // the Upgrade card. Returns false (and changes nothing) if the play is no longer valid.
        public bool CommitUpgrade(CardData card, string familyRootId)
        {
            if (!IsUpgradeUnitCard(card) || _upgradeManager == null) return false;
            if (string.IsNullOrEmpty(familyRootId) || !_upgradeManager.CanUpgrade(familyRootId)) return false;

            int cost = _upgradeManager.CurrentMpCost;
            if (!_mpManager.CanPay(cost)) return false;

            // Look up the family's current card BEFORE applying the upgrade so a deck swap
            // replaces the right pool entry on an evolution step.
            CardData beforeCard = _upgradeManager.GetCurrentCard(familyRootId);

            UpgradeManager.UpgradeStep step = _upgradeManager.ApplyUpgrade(familyRootId);
            if (!step.Valid) return false;

            if (!_mpManager.Spend(cost)) return false;

            if (_battlefieldView != null)
            {
                _battlefieldView.UpgradeOnFieldUnits(familyRootId, step, _pendingUnitBuild);
            }

            // On an evolution, the evolved form replaces the base everywhere it can still be
            // drafted: the deck piles (future draws) AND any copies already sitting in the hand
            // (dealt before the swap), so the base never reappears this run.
            if (step.IsEvolution && step.EvolvedCard != null && beforeCard != null)
            {
                _deckManager?.ReplaceCardInPool(beforeCard, step.EvolvedCard);
                _handManager.ReplaceCard(beforeCard, step.EvolvedCard);
            }

            _handManager.Remove(card);
            OnCardPlayed?.Invoke();
            return true;
        }

        public static bool IsEmergencyDraftCard(CardData card)
        {
            if (card == null || card.supportEffects == null) return false;
            foreach (SupportEffectData e in card.supportEffects)
            {
                if (e.effectType == SupportEffectType.EmergencyDraftUnits) return true;
            }
            return false;
        }

        // Rolls the unit choices Emergency Draft offers this play: `value` random draws from the
        // CURRENT draftable pool (so an un-evolved family yields the base form, an evolved one
        // yields the evolved form, and locked higher tiers never appear). The UI calls this once
        // to populate the modal.
        public List<CardData> GetEmergencyDraftOptions(CardData card)
        {
            if (!IsEmergencyDraftCard(card) || _deckManager == null) return new List<CardData>();
            int count = 0;
            foreach (SupportEffectData e in card.supportEffects)
            {
                if (e.effectType == SupportEffectType.EmergencyDraftUnits)
                {
                    count = Mathf.Max(count, Mathf.RoundToInt(e.value));
                }
            }
            if (count <= 0) count = 2;
            return _deckManager.SampleDraftableUnits(count);
        }

        // Finishes an Emergency Draft play once the player picks a unit in the modal. Spends the
        // card's MP cost, spawns the chosen unit as a temporary battle-only reinforcement (removed
        // at wave end), then discards the spell card. Returns false (changing nothing) if the play
        // is no longer valid. The summon does NOT count against the one-Unit-Card-per-wave rule.
        public bool CommitEmergencyDraft(CardData card, CardData chosenUnit)
        {
            if (!IsEmergencyDraftCard(card) || chosenUnit == null) return false;
            if (!CanPlayCard(card)) return false;
            if (!_mpManager.Spend(card.mpCost)) return false;

            if (_battlefieldView != null)
            {
                _battlefieldView.SummonTemporaryUnit(chosenUnit);
            }

            _handManager.Remove(card);
            OnCardPlayed?.Invoke();
            return true;
        }

        public PendingUnitBuild ConsumePendingBuild()
        {
            PendingUnitBuild build = _pendingUnitBuild;
            _pendingUnitBuild = null;
            OnPendingBuildChanged?.Invoke(null);
            return build;
        }

        public void BeginNewTurn()
        {
            _pendingUnitBuild = null;
            _unitCardPlayedThisTurn = false;
            _holdSpellLimitThisTurn = 0;
            _spentCardsThisTurn.Clear();
            OnPendingBuildChanged?.Invoke(null);
        }

        public List<CardData> ExtractHeldSpellCards(IReadOnlyList<CardData> cards)
        {
            List<CardData> held = new();
            if (_holdSpellLimitThisTurn <= 0 || cards == null) return held;

            foreach (CardData card in cards)
            {
                if (card == null || card.cardType != CardType.Support) continue;
                held.Add(card);
                if (held.Count >= _holdSpellLimitThisTurn) break;
            }

            _holdSpellLimitThisTurn = 0;
            return held;
        }

        private static bool IsBattlefieldSpell(CardData card)
        {
            if (card == null || card.supportEffects == null) return false;
            foreach (SupportEffectData e in card.supportEffects)
            {
                if (e.effectType == SupportEffectType.DuplicateAllPlayerUnits) return true;
                if (e.effectType == SupportEffectType.StrengthenAllPlayerUnits) return true;
                if (e.effectType == SupportEffectType.ShieldFrontLine) return true;
                if (e.effectType == SupportEffectType.RallyAllPlayerUnits) return true;
                if (e.effectType == SupportEffectType.ReviveFirstDead) return true;
                if (e.effectType == SupportEffectType.LightningStrikePriorityEnemy) return true;
                if (e.effectType == SupportEffectType.MarkEnemyLine) return true;
                if (e.effectType == SupportEffectType.ShieldPlayerLine) return true;
                if (e.effectType == SupportEffectType.DuplicatePlayerLineLimited) return true;
                if (e.effectType == SupportEffectType.DrawTemporarySpellCards) return true;
                if (e.effectType == SupportEffectType.HoldSpellForNextTurn) return true;
                if (e.effectType == SupportEffectType.IncreaseMaxMpNextTurn) return true;
                if (e.effectType == SupportEffectType.DamageEnemyLine) return true;
                if (e.effectType == SupportEffectType.SlowEnemyOpeningLines) return true;
                if (e.effectType == SupportEffectType.ReduceDamageFrontLine) return true;
                if (e.effectType == SupportEffectType.RallyPlayerLine) return true;
                if (e.effectType == SupportEffectType.LightningStrikePriorityEnemies) return true;
                if (e.effectType == SupportEffectType.EmergencyDraftUnits) return true;
                if (e.effectType == SupportEffectType.UpgradeUnit) return true;
                if (e.effectType == SupportEffectType.MeteorEnemyLine) return true;
            }
            return false;
        }

        public static bool IsUpgradeUnitCard(CardData card)
        {
            if (card == null || card.supportEffects == null) return false;
            foreach (SupportEffectData e in card.supportEffects)
            {
                if (e.effectType == SupportEffectType.UpgradeUnit) return true;
            }
            return false;
        }

        // The MP cost of playing the Upgrade card right now (dynamic, escalates per use), or
        // the card's own mpCost for any other card.
        public int EffectiveMpCost(CardData card)
        {
            if (IsUpgradeUnitCard(card) && _upgradeManager != null)
            {
                return _upgradeManager.CurrentMpCost;
            }
            return card != null ? card.mpCost : 0;
        }

        // The distinct on-field player unit families that can still be upgraded. Returns each
        // family's root id once. Used by the UI to populate the upgrade-target picker and to
        // decide whether the Upgrade card is playable at all.
        public List<string> GetUpgradeableFamiliesOnField()
        {
            List<string> result = new();
            if (_battlefieldManager == null || _upgradeManager == null)
            {
                Debug.LogWarning($"[CardPlayManager] GetUpgradeableFamiliesOnField: missing ref battlefieldManager={_battlefieldManager != null} upgradeManager={_upgradeManager != null}");
                return result;
            }

            int total = 0;
            foreach (UnitGroup unit in _battlefieldManager.PlayerUnits)
            {
                if (unit == null || unit.IsDead) continue;
                total++;
                string root = unit.FamilyId;
                bool canUpgrade = !string.IsNullOrEmpty(root) && _upgradeManager.CanUpgrade(root);
                Debug.Log($"[CardPlayManager]   field unit '{unit.DisplayName}' family='{root}' temp={unit.TemporaryBattleOnly} canUpgrade={canUpgrade}");
                if (string.IsNullOrEmpty(root) || result.Contains(root)) continue;
                if (!canUpgrade) continue;
                result.Add(root);
            }
            Debug.Log($"[CardPlayManager] GetUpgradeableFamiliesOnField: {total} alive player units → {result.Count} upgradeable families");
            return result;
        }

        public static bool RequiresLaneTarget(CardData card)
        {
            if (card == null || card.cardType != CardType.Support || card.supportEffects == null) return false;
            foreach (SupportEffectData e in card.supportEffects)
            {
                if (e.effectType == SupportEffectType.DuplicateAllPlayerUnits) return true;
                if (e.effectType == SupportEffectType.MarkEnemyLine) return true;
                if (e.effectType == SupportEffectType.ShieldPlayerLine) return true;
                if (e.effectType == SupportEffectType.DuplicatePlayerLineLimited) return true;
                if (e.effectType == SupportEffectType.RallyPlayerLine) return true;
                if (e.effectType == SupportEffectType.DamageEnemyLine && e.value2 < 0f) return true;
                if (e.effectType == SupportEffectType.MeteorEnemyLine) return true;
            }
            return false;
        }

        public static bool TargetsEnemyLane(CardData card)
        {
            if (card == null || card.supportEffects == null) return false;
            foreach (SupportEffectData e in card.supportEffects)
            {
                if (e.effectType == SupportEffectType.MarkEnemyLine) return true;
                if (e.effectType == SupportEffectType.DamageEnemyLine && e.value2 < 0f) return true;
                if (e.effectType == SupportEffectType.MeteorEnemyLine) return true;
            }
            return false;
        }

        private void ApplyBattlefieldEffects(CardData card, FormationLine? targetLane)
        {
            if (_battlefieldView == null || card.supportEffects == null) return;

            foreach (SupportEffectData e in card.supportEffects)
            {
                switch (e.effectType)
                {
                    case SupportEffectType.DuplicateAllPlayerUnits:
                        _battlefieldView.DuplicatePlayerUnits(_pendingUnitBuild, targetLane);
                        break;
                    case SupportEffectType.StrengthenAllPlayerUnits:
                        _battlefieldView.StrengthenPlayerUnits(e.value, _pendingUnitBuild);
                        break;
                    case SupportEffectType.ShieldFrontLine:
                        _battlefieldView.ShieldFrontLineUnits(e.value, _pendingUnitBuild);
                        break;
                    case SupportEffectType.RallyAllPlayerUnits:
                        _battlefieldView.RallyPlayerUnits(e.value, e.value2, _pendingUnitBuild);
                        break;
                    case SupportEffectType.ReviveFirstDead:
                        // value = how many fallen units to revive; value2 = HP fraction (0.5 = 50%).
                        _battlefieldView.RevivePlayerUnits(Mathf.RoundToInt(e.value), e.value2);
                        break;
                    case SupportEffectType.LightningStrikePriorityEnemy:
                        _battlefieldView.LightningStrikePriorityEnemy(Mathf.RoundToInt(e.value), e.value2, card.projectileSprite);
                        break;
                    case SupportEffectType.MarkEnemyLine:
                        _battlefieldView.MarkEnemyLine(targetLane, e.value);
                        break;
                    case SupportEffectType.ShieldPlayerLine:
                        _battlefieldView.ShieldPlayerLine(targetLane, e.value, _pendingUnitBuild, IsBarrierCard(card));
                        break;
                    case SupportEffectType.DuplicatePlayerLineLimited:
                        _battlefieldView.DuplicatePlayerUnits(_pendingUnitBuild, targetLane, temporaryBattleOnly: true);
                        break;
                    case SupportEffectType.DrawTemporarySpellCards:
                        _handManager.AddCards(_deckManager.CreateTemporaryCards(CardType.Support, Mathf.RoundToInt(e.value), Mathf.RoundToInt(e.value2)));
                        break;
                    case SupportEffectType.HoldSpellForNextTurn:
                        _holdSpellLimitThisTurn = Mathf.Max(_holdSpellLimitThisTurn, Mathf.RoundToInt(e.value));
                        break;
                    case SupportEffectType.IncreaseMaxMpNextTurn:
                        _mpManager.IncreaseMaxMpNextTurn(Mathf.RoundToInt(e.value));
                        break;
                    case SupportEffectType.DamageEnemyLine:
                        _battlefieldView.DamageEnemyLine(targetLane, e.value, e.value2, e.value3);
                        break;
                    case SupportEffectType.MeteorEnemyLine:
                        _battlefieldView.MeteorEnemyLine(targetLane, e.value, e.value3);
                        break;
                    case SupportEffectType.SlowEnemyOpeningLines:
                        _battlefieldView.SlowEnemyOpeningLines(e.value, e.value2);
                        break;
                    case SupportEffectType.ReduceDamageFrontLine:
                        _battlefieldView.ReduceFrontLineDamage(e.value, _pendingUnitBuild);
                        break;
                    case SupportEffectType.RallyPlayerLine:
                        _battlefieldView.RallyPlayerLine(targetLane, e.value, e.value2, _pendingUnitBuild);
                        break;
                    case SupportEffectType.LightningStrikePriorityEnemies:
                        _battlefieldView.LightningStrikePriorityEnemies(Mathf.RoundToInt(e.value), Mathf.RoundToInt(e.value2), card.projectileSprite);
                        break;
                    // EmergencyDraftUnits is handled by the deferred modal flow (see TryPlayCard /
                    // CommitEmergencyDraft); it never resolves here.
                }
            }
        }

        private static void ApplySupport(PendingUnitBuild build, CardData supportCard)
        {
            if (build == null || supportCard == null || supportCard.supportEffects == null) return;

            foreach (SupportEffectData effect in supportCard.supportEffects)
            {
                switch (effect.effectType)
                {
                    case SupportEffectType.MultiplyUnitCount:
                        build.count *= Mathf.RoundToInt(effect.value);
                        break;
                    case SupportEffectType.AddAttackPercent:
                        build.attack = Mathf.RoundToInt(build.attack * (1f + effect.value));
                        break;
                    case SupportEffectType.AddAttackFlat:
                        build.attack += Mathf.RoundToInt(effect.value);
                        break;
                    case SupportEffectType.AddHpPercent:
                        build.hp = Mathf.RoundToInt(build.hp * (1f + effect.value));
                        break;
                    case SupportEffectType.AddHpFlat:
                        build.hp += Mathf.RoundToInt(effect.value);
                        break;
                    case SupportEffectType.ChangeLine:
                        build.line = (FormationLine)Mathf.RoundToInt(effect.value);
                        break;
                }
            }

            build.appliedCards.Add(supportCard);
        }

        private static bool IsBarrierCard(CardData card)
        {
            if (card == null) return false;
            if (string.Equals(card.cardId, "barrier", StringComparison.OrdinalIgnoreCase)) return true;
            return string.Equals(card.cardName, "Barrier", StringComparison.OrdinalIgnoreCase);
        }
    }
}
