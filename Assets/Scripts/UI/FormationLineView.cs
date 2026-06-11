using DraftCards.Core;
using TMPro;
using UnityEngine;

namespace DraftCards.UI
{
    public class FormationLineView : MonoBehaviour
    {
        [SerializeField] private FormationLine _line;
        [SerializeField] private bool _isPlayerSide = true;
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private Transform _unitContainer;

        public FormationLine Line => _line;
        public bool IsPlayerSide => _isPlayerSide;
        public Transform UnitContainer => _unitContainer;

        private void Awake()
        {
            if (_titleText != null)
            {
                _titleText.text = $"{(_isPlayerSide ? "P" : "E")} {_line}";
            }
        }
    }
}
