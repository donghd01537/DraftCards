using System;
using System.Collections;
using System.Collections.Generic;
using DraftCards.Cards;
using DraftCards.Core;
using DraftCards.Data;
using DraftCards.UI;
using DraftCards.Units;
using UnityEngine;

namespace DraftCards.Managers
{
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private DeckManager _deckManager;
        [SerializeField] private HandManager _handManager;
        [SerializeField] private CardPlayManager _cardPlayManager;
        [SerializeField] private MPManager _mpManager;
        [SerializeField] private BattlefieldManager _battlefieldManager;
        [SerializeField] private BattlefieldView _battlefieldView;

        [SerializeField] private int _unitCardsPerWave = 3;
        [SerializeField] private int _spellCardsPerWave = 5;

        [Header("Battle pacing")]
        [SerializeField] private float _postBattlePause = 0.5f;
        [SerializeField] private float _maxBattleSeconds = 30f;

        // A group of identical enemy fighters to drop into one lane at round start.
        private readonly struct EnemySpawn
        {
            public readonly string CardId;
            public readonly int Count;
            public readonly FormationLine Line;

            public EnemySpawn(string cardId, int count, FormationLine line)
            {
                CardId = cardId;
                Count = count;
                Line = line;
            }
        }

        // Per-round enemy composition. The active round loops back to the first entry
        // once the last wave has been cleared, so play continues indefinitely.
        private static readonly EnemySpawn[][] _waves =
        {
            // Wave 1: Goblin x8 (front)
            new[]
            {
                new EnemySpawn("goblin", 8, FormationLine.Front),
            },
            // Wave 2: Goblin x8 (front) + Goblin Archer x2 (back)
            new[]
            {
                new EnemySpawn("goblin", 8, FormationLine.Front),
                new EnemySpawn("goblin_archer", 2, FormationLine.Back),
            },
            // Wave 3: Goblin x12 (front) + Goblin Archer x2 (back)
            new[]
            {
                new EnemySpawn("goblin", 12, FormationLine.Front),
                new EnemySpawn("goblin_archer", 2, FormationLine.Back),
            },
            // Wave 4: Orc x1 + Goblin x8 (front) + Goblin Archer x2 (back)
            new[]
            {
                new EnemySpawn("orc", 1, FormationLine.Front),
                new EnemySpawn("goblin", 8, FormationLine.Front),
                new EnemySpawn("goblin_archer", 2, FormationLine.Back),
            },
            // Wave 5: Orc x2 + Goblin x8 (front) + Shaman x1 (middle) + Goblin Archer x4 (back)
            new[]
            {
                new EnemySpawn("orc", 2, FormationLine.Front),
                new EnemySpawn("goblin", 8, FormationLine.Front),
                new EnemySpawn("shaman", 1, FormationLine.Middle),
                new EnemySpawn("goblin_archer", 4, FormationLine.Back),
            },
            // Wave 6: Orc x2 + Wolf Rider x2 + Goblin x8 (front) + Shaman x2 (middle)
            //         + Goblin Archer x4 + Cyclop x1 (back)
            new[]
            {
                new EnemySpawn("orc", 2, FormationLine.Front),
                new EnemySpawn("wolf_rider", 2, FormationLine.Front),
                new EnemySpawn("goblin", 8, FormationLine.Front),
                new EnemySpawn("shaman", 2, FormationLine.Middle),
                new EnemySpawn("goblin_archer", 4, FormationLine.Back),
                new EnemySpawn("cyclop", 1, FormationLine.Back),
            },
        };

        private GameState _state = GameState.DrawPhase;
        private bool _resolving;
        private int _roundIndex;

        public event Action<GameState> OnStateChanged;

        public GameState State => _state;

        private void Start()
        {
            StartBattle();
        }

        public void StartBattle()
        {
            _deckManager.InitializeFromStartingDeck();
            _handManager.Clear();
            _mpManager.ResetForNewTurn();
            _cardPlayManager.BeginNewTurn();
            _battlefieldManager.Clear();
            _roundIndex = 0;
            SpawnWave(_roundIndex);
            BeginPlayerTurn();
        }

        public void OnConfirmPressed()
        {
            if (_resolving) return;
            if (_state != GameState.SelectCardPhase && _state != GameState.PreviewPhase) return;
            bool canFight = _cardPlayManager.HasPendingUnit
                            || (_battlefieldView != null && _battlefieldView.HasAlivePlayer);
            if (!canFight) return;

            StartCoroutine(EndTurnSequence());
        }

        private IEnumerator EndTurnSequence()
        {
            _resolving = true;
            SummonPendingUnit();

            yield return RunBattle();

            // Each battle cycle is a new round: tear down whatever is left of the enemy
            // side and rebuild it from the next wave's composition.
            if (_battlefieldView != null)
            {
                _battlefieldView.ClearTemporaryPlayerUnits();
                _battlefieldView.ClearEnemyUnits();
            }
            _battlefieldManager.ClearEnemies();

            _roundIndex++;
            SpawnWave(_roundIndex);

            if (_battlefieldView != null)
            {
                _battlefieldView.ReviveAllDead();
                _battlefieldView.RegroupAllUnits();
            }

            BeginPlayerTurn();
            _resolving = false;
        }

        private void BeginPlayerTurn()
        {
            ChangeState(GameState.DrawPhase);
            List<CardData> heldCards = _cardPlayManager != null
                ? _cardPlayManager.ExtractHeldSpellCards(_handManager.Cards)
                : new List<CardData>();
            _mpManager.ResetForNewTurn();
            _cardPlayManager.BeginNewTurn();
            // Clear one-turn battlefield effects (e.g. an unspent Revive budget) so a spell
            // cast last turn can't bleed into this round.
            if (_battlefieldView != null) _battlefieldView.ResetTurnEffects();

            // Discard any leftover hand cards so each round starts with a fresh draw.
            foreach (Data.CardData card in _handManager.Cards)
            {
                if (heldCards.Contains(card)) continue;
                _deckManager.Discard(card);
            }
            _handManager.Clear();

            _handManager.AddCards(_deckManager.Draw(CardType.Unit, _unitCardsPerWave));
            _handManager.AddCards(_deckManager.Draw(CardType.Support, _spellCardsPerWave));
            _handManager.AddCards(heldCards);
            ChangeState(GameState.SelectCardPhase);
        }

        private void SummonPendingUnit()
        {
            PendingUnitBuild build = _cardPlayManager.ConsumePendingBuild();
            if (build == null) return;

            int spawnCount = Mathf.Max(1, build.count);
            for (int i = 0; i < spawnCount; i++)
            {
                UnitGroup unit = new(build, isPlayerUnit: true);
                _battlefieldManager.PlaceUnit(unit);
            }
        }

        private IEnumerator RunBattle()
        {
            ChangeState(GameState.ResolvePhase);

            if (_battlefieldView == null)
            {
                yield break;
            }

            _battlefieldView.StartBattle();

            float elapsed = 0f;
            while (_battlefieldView.BothSidesAlive && elapsed < _maxBattleSeconds)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            _battlefieldView.PauseBattle();
            yield return new WaitForSeconds(_postBattlePause);
        }

        private void SpawnWave(int roundIndex)
        {
            if (_waves.Length == 0) return;

            EnemySpawn[] wave = _waves[roundIndex % _waves.Length];
            foreach (EnemySpawn spawn in wave)
            {
                CardData card = LoadEnemyCardById(spawn.CardId);
                if (card == null)
                {
                    Debug.LogWarning($"[GameManager] Enemy card '{spawn.CardId}' not found in Resources/Enemies. Run 'DraftCards > Create Starter Cards'.");
                    continue;
                }

                PendingUnitBuild build = new(card) { line = spawn.Line };
                for (int i = 0; i < spawn.Count; i++)
                {
                    UnitGroup enemy = new(build, isPlayerUnit: false);
                    _battlefieldManager.PlaceUnit(enemy);
                }
            }
        }

        private static CardData LoadEnemyCardById(string cardId)
        {
            CardData[] all = Resources.LoadAll<CardData>("Enemies");
            foreach (CardData card in all)
            {
                if (card.cardId == cardId) return card;
            }
            return null;
        }

        private void ChangeState(GameState newState)
        {
            if (_state == newState) return;
            _state = newState;
            OnStateChanged?.Invoke(_state);
        }
    }
}
