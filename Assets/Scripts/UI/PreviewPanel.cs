using DraftCards.Cards;
using TMPro;
using UnityEngine;

namespace DraftCards.UI
{
    public class PreviewPanel : MonoBehaviour
    {
        [SerializeField] private GameObject _root;
        [SerializeField] private TMP_Text _attackText;
        [SerializeField] private TMP_Text _hpText;
        [SerializeField] private TMP_Text _countText;
        [SerializeField] private TMP_Text _lineText;
        [SerializeField] private TMP_Text _appliedSupportsText;

        public void ShowEmpty()
        {
            if (_root != null) _root.SetActive(false);
        }

        public void Show(PendingUnitBuild build)
        {
            if (build == null)
            {
                ShowEmpty();
                return;
            }

            if (_root != null) _root.SetActive(true);
            if (_attackText != null) _attackText.text = $"ATK {build.attack}";
            if (_hpText != null) _hpText.text = $"HP {build.hp}";
            if (_countText != null) _countText.text = $"x{build.count}";
            if (_lineText != null) _lineText.text = build.line.ToString();

            if (_appliedSupportsText != null)
            {
                string supports = "";
                for (int i = 1; i < build.appliedCards.Count; i++)
                {
                    if (supports.Length > 0) supports += ", ";
                    supports += build.appliedCards[i].cardName;
                }
                _appliedSupportsText.text = supports;
            }
        }
    }
}
