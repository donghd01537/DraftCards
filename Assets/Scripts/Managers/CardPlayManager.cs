using System;
using System.Collections.Generic;
using DraftCards.Cards;
using DraftCards.Core;
using DraftCards.Data;
using DraftCards.UI;
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
        }

        [SerializeField] private MPManager _mpManager;
        [SerializeField] private HandManager _handManager;
        [SerializeField] private DeckManager _deckManager;
        [SerializeField] private BattlefieldView _battlefieldView;

        private PendingUnitBuild _pendingUnitBuild;
        private bool _unitCardPlayedThisTurn;
        private readonly HashSet<CardData> _spentCardsThisTurn = new();

        public event Action<PendingUnitBuild> OnPendingBuildChanged;
        public event Action OnCardPlayed;

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

            if (card.cardType == CardType.Unit)
            {
                _pendingUnitBuild = new PendingUnitBuild(card);
                _unitCardPlayedThisTurn = true;
                _spentCardsThisTurn.Add(card);
                if (consume)
                {
                    // Dragged unit cards are fully consumed — gone from hand and from the deck.
                    _handManager.Remove(card);
                }
                OnPendingBuildChanged?.Invoke(_pendingUnitBuild);
                OnCardPlayed?.Invoke();
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
            _spentCardsThisTurn.Clear();
            OnPendingBuildChanged?.Invoke(null);
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
    }
}
