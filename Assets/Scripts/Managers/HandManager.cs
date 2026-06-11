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
