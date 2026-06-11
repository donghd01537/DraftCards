using System.Collections.Generic;
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

        public int DrawPileCount => _drawPile.Count;
        public int DiscardPileCount => _discardPile.Count;

        public void InitializeFromStartingDeck()
        {
            _drawPile.Clear();
            _discardPile.Clear();

            if (_startingDeck != null && _startingDeck.Count > 0)
            {
                _drawPile.AddRange(_startingDeck);
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
                    _drawPile.AddRange(allCards);
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

        public void Discard(CardData card)
        {
            if (card == null)
            {
                return;
            }
            _discardPile.Add(card);
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
