using System;
using System.Collections.Generic;
using DraftCards.Units;
using UnityEngine;

namespace DraftCards.Managers
{
    public class BattlefieldManager : MonoBehaviour
    {
        private readonly List<UnitGroup> _playerUnits = new();
        private readonly List<UnitGroup> _enemyUnits = new();

        public event Action<UnitGroup> OnUnitPlaced;
        public event Action<UnitGroup> OnUnitRemoved;

        public IReadOnlyList<UnitGroup> PlayerUnits => _playerUnits;
        public IReadOnlyList<UnitGroup> EnemyUnits => _enemyUnits;

        public bool HasAnyPlayerUnit() => _playerUnits.Count > 0;
        public bool HasAnyEnemyUnit() => _enemyUnits.Count > 0;

        public void PlaceUnit(UnitGroup unit)
        {
            if (unit == null) return;
            List<UnitGroup> list = unit.IsPlayerUnit ? _playerUnits : _enemyUnits;
            list.Add(unit);
            OnUnitPlaced?.Invoke(unit);
        }

        public void AddSilent(UnitGroup unit)
        {
            if (unit == null) return;
            List<UnitGroup> list = unit.IsPlayerUnit ? _playerUnits : _enemyUnits;
            list.Add(unit);
        }

        public void RemoveUnit(UnitGroup unit)
        {
            if (unit == null) return;
            List<UnitGroup> list = unit.IsPlayerUnit ? _playerUnits : _enemyUnits;
            if (list.Remove(unit))
            {
                OnUnitRemoved?.Invoke(unit);
            }
        }

        public void ClearEnemies()
        {
            _enemyUnits.Clear();
        }

        public void Clear()
        {
            _playerUnits.Clear();
            _enemyUnits.Clear();
        }
    }
}
