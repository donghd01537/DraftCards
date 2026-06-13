using System.Collections.Generic;
using DraftCards.Core;
using DraftCards.Data;
using UnityEngine;

namespace DraftCards.Managers
{
    public class DeckManager : MonoBehaviour
    {
        [SerializeField] private List<CardData> _startingDeck = new();
        [SerializeField] private string _resourcesFolder = "Cards";
        [SerializeField] private int _copiesPerCard = 2;

        private readonly List<CardData> _drawPile = new();
        private readonly List<CardData> _discardPile = new();

        public void InitializeFromStartingDeck()
        {
            _drawPile.Clear();
            _discardPile.Clear();

            if (_startingDeck != null && _startingDeck.Count > 0)
            {
                foreach (CardData card in _startingDeck)
                {
                    if (card != null && card.excludeFromInitialDeck) continue;
                    _drawPile.Add(card);
                }
            }
            else
            {
                CardData[] allCards = Resources.LoadAll<CardData>(_resourcesFolder);
                if (allCards.Length == 0)
                {
                    Debug.LogWarning($"[DeckManager] No CardData found at Resources/{_resourcesFolder}. " +
                                     "Run 'DraftCards > Create Starter Cards'.");
                    return;
                }

                int copies = Mathf.Max(1, _copiesPerCard);
                for (int i = 0; i < copies; i++)
                {
                    foreach (CardData card in allCards)
                    {
                        // Evolved forms (e.g. Spartan) only enter play by upgrading a base
                        // unit, so they must not be drafted from scratch before being unlocked.
                        if (card != null && card.excludeFromInitialDeck) continue;
                        _drawPile.Add(card);
                    }
                }
            }

            Shuffle(_drawPile);
        }

        public CardData DrawOne()
        {
            if (_drawPile.Count == 0)
            {
                ReshuffleDiscardIntoDraw();
            }

            if (_drawPile.Count == 0)
            {
                return null;
            }

            int lastIndex = _drawPile.Count - 1;
            CardData card = _drawPile[lastIndex];
            _drawPile.RemoveAt(lastIndex);
            return card;
        }

        public List<CardData> Draw(int count)
        {
            List<CardData> result = new(count);
            for (int i = 0; i < count; i++)
            {
                CardData card = DrawOne();
                if (card == null)
                {
                    break;
                }
                result.Add(card);
            }
            return result;
        }

        public List<CardData> Draw(CardType cardType, int count)
        {
            List<CardData> result = new(count);
            for (int i = 0; i < count; i++)
            {
                CardData card = DrawOne(cardType);
                if (card == null)
                {
                    break;
                }
                result.Add(card);
            }
            return result;
        }

        private CardData DrawOne(CardType cardType)
        {
            int index = FindLastIndexOfType(_drawPile, cardType);
            if (index < 0)
            {
                ReshuffleDiscardIntoDraw();
                index = FindLastIndexOfType(_drawPile, cardType);
            }

            if (index < 0)
            {
                return null;
            }

            CardData card = _drawPile[index];
            _drawPile.RemoveAt(index);
            return card;
        }

        public void Discard(CardData card)
        {
            if (card == null)
            {
                return;
            }
            if (card.temporary)
            {
                return;
            }
            _discardPile.Add(card);
        }

        // Swaps every copy of `from` in the deck (draw + discard piles) for `to`. Used by the
        // Unit Upgrade system when a family evolves (e.g. Swordsman -> Spartan): future draws then
        // deal the evolved card and the base never reappears for the rest of the run.
        public void ReplaceCardInPool(CardData from, CardData to)
        {
            if (from == null || to == null || from == to) return;
            for (int i = 0; i < _drawPile.Count; i++)
            {
                if (_drawPile[i] == from) _drawPile[i] = to;
            }
            for (int i = 0; i < _discardPile.Count; i++)
            {
                if (_discardPile[i] == from) _discardPile[i] = to;
            }
        }

        // The distinct unit cards the player can currently draft, in their CURRENT tier. Because
        // an evolution swaps base->evolved in both piles via ReplaceCardInPool, the live piles
        // already hold the right form (e.g. Spartan, not Swordsman, once evolved), and evolved
        // forms that haven't been unlocked never entered the piles at all. So sampling from the
        // piles — not Resources.LoadAll — is what keeps Emergency Draft from offering a locked
        // higher tier. Temporary copies are skipped so a previous draft can't seed the pool.
        public List<CardData> GetDraftableUnitPool()
        {
            List<CardData> pool = new();
            CollectDistinctUnits(_drawPile, pool);
            CollectDistinctUnits(_discardPile, pool);
            return pool;
        }

        private static void CollectDistinctUnits(IList<CardData> source, List<CardData> into)
        {
            foreach (CardData card in source)
            {
                if (card == null || card.cardType != CardType.Unit || card.temporary) continue;
                if (!into.Contains(card)) into.Add(card);
            }
        }

        // Picks `count` unit cards (with replacement) at random from the current draftable pool,
        // for Emergency Draft's two-option roll. Returns fewer (or none) if the pool is empty.
        public List<CardData> SampleDraftableUnits(int count)
        {
            List<CardData> result = new(Mathf.Max(0, count));
            if (count <= 0) return result;

            List<CardData> pool = GetDraftableUnitPool();
            if (pool.Count == 0) return result;

            for (int i = 0; i < count; i++)
            {
                result.Add(pool[Random.Range(0, pool.Count)]);
            }
            return result;
        }

        public List<CardData> CreateTemporaryCards(CardType cardType, int count, int firstMpDiscount = 0)
        {
            List<CardData> result = new(count);
            if (count <= 0)
            {
                return result;
            }

            CardData[] allCards = Resources.LoadAll<CardData>(_resourcesFolder);
            List<CardData> candidates = new();
            foreach (CardData card in allCards)
            {
                if (card != null && card.cardType == cardType && !card.excludeFromInitialDeck)
                {
                    candidates.Add(card);
                }
            }

            if (candidates.Count == 0)
            {
                Debug.LogWarning($"[DeckManager] No {cardType} CardData found at Resources/{_resourcesFolder}.");
                return result;
            }

            for (int i = 0; i < count; i++)
            {
                CardData source = candidates[Random.Range(0, candidates.Count)];
                CardData copy = Instantiate(source);
                copy.name = $"{source.name}_Temporary";
                copy.temporary = true;
                if (i == 0 && firstMpDiscount > 0)
                {
                    copy.mpCost = Mathf.Max(0, copy.mpCost - firstMpDiscount);
                }
                result.Add(copy);
            }

            return result;
        }

        private void ReshuffleDiscardIntoDraw()
        {
            if (_discardPile.Count == 0)
            {
                return;
            }

            _drawPile.AddRange(_discardPile);
            _discardPile.Clear();
            Shuffle(_drawPile);
        }

        private static int FindLastIndexOfType(IList<CardData> list, CardType cardType)
        {
            for (int i = list.Count - 1; i >= 0; i--)
            {
                CardData card = list[i];
                if (card != null && card.cardType == cardType)
                {
                    return i;
                }
            }
            return -1;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int swap = Random.Range(0, i + 1);
                (list[i], list[swap]) = (list[swap], list[i]);
            }
        }
    }
}
