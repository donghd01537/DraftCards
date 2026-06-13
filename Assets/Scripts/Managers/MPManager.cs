using System;
using UnityEngine;

namespace DraftCards.Managers
{
    public class MPManager : MonoBehaviour
    {
        [SerializeField] private int _startMp = 10;
        [SerializeField] private int _maxMp = 10;

        private int _currentMp;
        private int _pendingMaxMpIncrease;

        public event Action<int, int> OnMpChanged;

        public int CurrentMp => _currentMp;
        public int MaxMp => _maxMp;

        private void Awake()
        {
            _currentMp = _startMp;
        }

        public bool CanPay(int cost)
        {
            return cost >= 0 && _currentMp >= cost;
        }

        public bool Spend(int cost)
        {
            if (!CanPay(cost))
            {
                return false;
            }

            _currentMp -= cost;
            OnMpChanged?.Invoke(_currentMp, _maxMp);
            return true;
        }

        public void Refund(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            _currentMp = Mathf.Min(_currentMp + amount, _maxMp);
            OnMpChanged?.Invoke(_currentMp, _maxMp);
        }

        public void ResetForNewTurn()
        {
            if (_pendingMaxMpIncrease > 0)
            {
                _maxMp += _pendingMaxMpIncrease;
                _pendingMaxMpIncrease = 0;
            }
            _currentMp = _maxMp;
            OnMpChanged?.Invoke(_currentMp, _maxMp);
        }

        public void IncreaseMaxMpNextTurn(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            _pendingMaxMpIncrease += amount;
        }
    }
}
