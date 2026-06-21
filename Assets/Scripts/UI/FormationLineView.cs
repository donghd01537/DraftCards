using DraftCards.Core;
using UnityEngine;

namespace DraftCards.UI
{
    public class FormationLineView : MonoBehaviour
    {
        [SerializeField] private FormationLine _line;
        [SerializeField] private bool _isPlayerSide = true;
        [SerializeField] private Transform _unitContainer;

        public FormationLine Line => _line;
        public bool IsPlayerSide => _isPlayerSide;
        public Transform UnitContainer => _unitContainer;
    }
}
