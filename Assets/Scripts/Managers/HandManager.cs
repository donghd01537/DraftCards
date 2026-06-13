using System;
using System.Collections.Generic;
using DraftCards.Data;
using UnityEngine;

namespace DraftCards.Managers
{
    public class HandManager : MonoBehaviour
    {
        private readonly List<CardData> _hand = new();

        public event Action OnHandChanged;

        public IReadOnlyList<CardData> Cards => _hand;

        public void AddCards(IReadOnlyList<CardData> cards)
        {
            if (cards == null || cards.Count == 0)
            {
                return;
            }

            _hand.AddRange(cards);
            OnHandChanged?.Invoke();
        }

        public bool Remove(CardData card)
        {
            if (card == null)
            {
                return false;
            }

            bool removed = _hand.Remove(card);
            if (removed)
            {
                OnHandChanged?.Invoke();
            }
            return removed;
        }

        // Swaps every copy of `from` already in the hand for `to`. Used when a unit family
        // evolves (Upgrade Unit): the cards in hand were dealt before the swap, so the deck-pile
        // swap alone wouldn't update them — this keeps the hand showing the evolved card.
        public int ReplaceCard(CardData from, CardData to)
        {
            if (from == null || to == null || from == to) return 0;
            int swapped = 0;
            for (int i = 0; i < _hand.Count; i++)
            {
                if (_hand[i] == from)
                {
                    _hand[i] = to;
                    swapped++;
                }
            }
            if (swapped > 0)
            {
                OnHandChanged?.Invoke();
            }
            return swapped;
        }

        public List<CardData> RemoveWhere(Predicate<CardData> predicate)
        {
            List<CardData> removed = new();
            if (predicate == null || _hand.Count == 0)
            {
                return removed;
            }

            for (int i = _hand.Count - 1; i >= 0; i--)
            {
                CardData card = _hand[i];
                if (!predicate(card))
                {
                    continue;
                }

                removed.Add(card);
                _hand.RemoveAt(i);
            }

            if (removed.Count > 0)
            {
                removed.Reverse();
                OnHandChanged?.Invoke();
            }

            return removed;
        }

        public void Clear()
        {
            if (_hand.Count == 0)
            {
                return;
            }
            _hand.Clear();
            OnHandChanged?.Invoke();
        }
    }
}
