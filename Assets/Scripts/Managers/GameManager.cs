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
            // Wave 1: Goblin x10 (front) + Goblin Archer x1 (back)
            new[]
            {
                new EnemySpawn("goblin", 10, FormationLine.Front),
                new EnemySpawn("goblin_archer", 1, FormationLine.Back),
            },
            // Wave 2: Goblin x10 (front) + Wolf Rider x2 (middle) + Goblin Archer x3 (back)
            new[]
            {
                new EnemySpawn("goblin", 10, FormationLine.Front),
                new EnemySpawn("wolf_rider", 2, FormationLine.Middle),
                new EnemySpawn("goblin_archer", 3, FormationLine.Back),
            },
            // Wave 3: Orc x2 + Goblin x10 (front) + Wolf Rider x4 (middle) + Goblin Archer x4 (back)
            new[]
            {
                new EnemySpawn("orc", 2, FormationLine.Front),
                new EnemySpawn("goblin", 10, FormationLine.Front),
                new EnemySpawn("wolf_rider", 4, FormationLine.Middle),
                new EnemySpawn("goblin_archer", 4, FormationLine.Back),
            },
            // Wave 4: Orc x3 + Goblin x10 (front) + Shaman x2 + Wolf Rider x6 (middle)
            //         + Goblin Archer x4 (back)
            new[]
            {
                new EnemySpawn("orc", 3, FormationLine.Front),
                new EnemySpawn("goblin", 10, FormationLine.Front),
                new EnemySpawn("shaman", 2, FormationLine.Middle),
                new EnemySpawn("wolf_rider", 6, FormationLine.Middle),
                new EnemySpawn("goblin_archer", 4, FormationLine.Back),
            },
            // Wave 5: Orc x4 + Goblin x12 (front) + Shaman x3 + Wolf Rider x8 (middle)
            //         + Goblin Archer x5 (back)
            new[]
            {
                new EnemySpawn("orc", 4, FormationLine.Front),
                new EnemySpawn("goblin", 12, FormationLine.Front),
                new EnemySpawn("shaman", 3, FormationLine.Middle),
                new EnemySpawn("wolf_rider", 8, FormationLine.Middle),
                new EnemySpawn("goblin_archer", 5, FormationLine.Back),
            },
            // Wave 6: Orc x4 + Goblin x14 (front) + Shaman x3 + Wolf Rider x8 (middle)
            //         + Goblin Archer x5 + Thunder Bird x2 (back)
            new[]
            {
                new EnemySpawn("orc", 4, FormationLine.Front),
                new EnemySpawn("goblin", 14, FormationLine.Front),
                new EnemySpawn("shaman", 3, FormationLine.Middle),
                new EnemySpawn("wolf_rider", 8, FormationLine.Middle),
                new EnemySpawn("goblin_archer", 5, FormationLine.Back),
                new EnemySpawn("thunder_bird", 2, FormationLine.Back),
            },
            // Wave 7: Orc x5 + Goblin x14 (front) + Shaman x3 + Wolf Rider x8 (middle)
            //         + Goblin Archer x5 + Cyclop x1 (back)
            new[]
            {
                new EnemySpawn("orc", 5, FormationLine.Front),
                new EnemySpawn("goblin", 14, FormationLine.Front),
                new EnemySpawn("shaman", 3, FormationLine.Middle),
                new EnemySpawn("wolf_rider", 8, FormationLine.Middle),
                new EnemySpawn("goblin_archer", 5, FormationLine.Back),
                new EnemySpawn("cyclop", 1, FormationLine.Back),
            },
            // Wave 8: Orc x6 + Goblin x16 (front) + Shaman x4 + Wolf Rider x10 (middle)
            //         + Goblin Archer x6 + Cyclop x2 + Thunder Bird x3 (back)
            new[]
            {
                new EnemySpawn("orc", 6, FormationLine.Front),
                new EnemySpawn("goblin", 16, FormationLine.Front),
                new EnemySpawn("shaman", 4, FormationLine.Middle),
                new EnemySpawn("wolf_rider", 10, FormationLine.Middle),
                new EnemySpawn("goblin_archer", 6, FormationLine.Back),
                new EnemySpawn("cyclop", 2, FormationLine.Back),
                new EnemySpawn("thunder_bird", 3, FormationLine.Back),
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

            // Each wave draws a fresh, independent hand: leftover cards are simply dropped (there
            // is no finite draw/discard pool to recycle into — DrawRandom samples the available
            // cards each time), so the spell pool can never run dry no matter how many you play.
            _handManager.Clear();

            _handManager.AddCards(_deckManager.DrawRandom(CardType.Unit, _unitCardsPerWave));
            _handManager.AddCards(_deckManager.DrawRandom(CardType.Support, _spellCardsPerWave));
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
