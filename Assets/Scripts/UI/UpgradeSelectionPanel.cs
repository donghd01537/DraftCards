using System.Collections.Generic;
using DraftCards.Data;
using DraftCards.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DraftCards.UI
{
    // Modal target picker for the Upgrade Unit spell. When the spell is played, this opens and
    // lists one card per distinct on-field player unit family that can still upgrade, each
    // showing its current card face and a hint of the next step (+stat% or the evolved name).
    // Clicking a card commits the upgrade on that family; Cancel (or the dimmed backdrop)
    // aborts with no MP spent and the card left in hand.
    //
    // Self-building: if _root is not wired in the Inspector, the panel constructs its whole
    // overlay (backdrop, title, card row, cancel button) at runtime under the canvas. So it
    // works in a scene that doesn't author the modal by hand — only _cardPlayManager and
    // _cardViewPrefab need to be supplied (and even those are auto-resolved when possible).
    public class UpgradeSelectionPanel : MonoBehaviour
    {
        [SerializeField] private CardPlayManager _cardPlayManager;
        [SerializeField] private CardView _cardViewPrefab;

        [SerializeField] private GameObject _root;
        [SerializeField] private Transform _cardListContainer;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Button _backdropButton;
        [SerializeField] private TMP_Text _titleText;

        [Header("Card layout")]
        [SerializeField] private float _cardSpacing = 220f;

        private readonly List<CardView> _spawnedCards = new();
        private readonly List<GameObject> _hintLabels = new();
        private CardData _pendingCard;
        private bool _built;

        // Lets a host (e.g. UIManager) inject its already-wired refs before the first open.
        public void Configure(CardPlayManager cardPlayManager, CardView cardViewPrefab)
        {
            if (_cardPlayManager == null) _cardPlayManager = cardPlayManager;
            if (_cardViewPrefab == null) _cardViewPrefab = cardViewPrefab;
        }

        private void Awake()
        {
            if (_cardPlayManager == null) _cardPlayManager = FindFirstObjectByType<CardPlayManager>();
            if (_root != null) _root.SetActive(false);
        }

        private void OnEnable()
        {
            if (_cardPlayManager != null) _cardPlayManager.OnUpgradeRequested += Open;
        }

        private void OnDisable()
        {
            if (_cardPlayManager != null) _cardPlayManager.OnUpgradeRequested -= Open;
        }

        public bool IsOpen => _root != null && _root.activeSelf;

        // Opens the picker for an Upgrade card play. Builds a card per upgradeable family.
        public void Open(CardData upgradeCard)
        {
            EnsureBuilt();
            if (_cardPlayManager == null || _cardViewPrefab == null || _cardListContainer == null)
            {
                Debug.LogWarning("[UpgradeSelectionPanel] Missing refs (cardPlayManager/cardViewPrefab); cannot open. Upgrade cancelled.");
                return;
            }

            _pendingCard = upgradeCard;
            ClearCards();

            UpgradeManager upgradeManager = _cardPlayManager.UpgradeManager;
            List<string> families = _cardPlayManager.GetUpgradeableFamiliesOnField();
            if (upgradeManager == null || families.Count == 0)
            {
                // Nothing to upgrade — don't strand the player in an empty modal.
                _pendingCard = null;
                return;
            }

            int cost = _cardPlayManager.EffectiveMpCost(upgradeCard);
            if (_titleText != null) _titleText.text = $"UPGRADE  —  {cost} MP  —  choose a unit";

            // Activate the modal BEFORE instantiating cards so each CardView's Awake runs
            // (instantiating under an inactive root leaves _button null → SetInteractable NREs).
            if (_root != null) _root.SetActive(true);

            int count = families.Count;
            float startX = -(count - 1) * 0.5f * _cardSpacing;
            for (int i = 0; i < count; i++)
            {
                string rootId = families[i];
                CardData currentCard = upgradeManager.GetCurrentCard(rootId);
                UpgradeManager.UpgradeStep step = upgradeManager.PreviewNext(rootId);
                if (currentCard == null || !step.Valid) continue;

                CardView view = Instantiate(_cardViewPrefab, _cardListContainer);
                view.Bind(currentCard);
                view.DisableHoverLift();
                view.SetInteractable(true);
                RectTransform rect = (RectTransform)view.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(startX + i * _cardSpacing, 0f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                view.RecordLayoutPose();

                string capturedRoot = rootId;
                view.OnClicked += (_, _) => OnFamilyChosen(capturedRoot);

                AddHintLabel(rect, UpgradeHint(step));
                _spawnedCards.Add(view);
            }

            if (_spawnedCards.Count == 0)
            {
                // No valid cards built — close the modal we just opened.
                _pendingCard = null;
                if (_root != null) _root.SetActive(false);
            }
        }

        private static string UpgradeHint(UpgradeManager.UpgradeStep step)
        {
            if (step.IsBranchChoice)
            {
                // Show the available evolution paths, e.g. "→ Spartan / Holy Knight".
                string names = string.Join(" / ", BranchNames(step));
                return $"→ {names}";
            }
            if (step.IsEvolution && step.EvolvedCard != null)
            {
                return $"→ {step.EvolvedCard.cardName}";
            }
            int pct = Mathf.RoundToInt((step.IncrementalMultiplier - 1f) * 100f);
            return step.HpAttackOnly ? $"+{pct}% HP / ATK" : $"+{pct}% all stats";
        }

        private static List<string> BranchNames(UpgradeManager.UpgradeStep step)
        {
            List<string> names = new();
            if (step.BranchOptions != null)
            {
                foreach (CardData option in step.BranchOptions)
                {
                    if (option != null) names.Add(option.cardName);
                }
            }
            return names;
        }

        // A family was picked. If its next upgrade is a single path, commit immediately. If it
        // branches (e.g. Spartan vs Holy Knight), open a second pick listing the options instead.
        private void OnFamilyChosen(string familyRootId)
        {
            if (_pendingCard == null) { Close(); return; }

            UpgradeManager upgradeManager = _cardPlayManager.UpgradeManager;
            UpgradeManager.UpgradeStep step = upgradeManager != null
                ? upgradeManager.PreviewNext(familyRootId)
                : default;

            if (step.Valid && step.IsBranchChoice)
            {
                OpenBranchChoice(familyRootId, step);
                return;
            }

            Confirm(familyRootId, chosenEvolveToId: null);
        }

        // Second-stage picker: replaces the family row with one card per branch option for the
        // chosen family. Picking one commits the upgrade down that branch; Cancel still aborts.
        private void OpenBranchChoice(string familyRootId, UpgradeManager.UpgradeStep step)
        {
            ClearCards();

            if (_titleText != null) _titleText.text = "CHOOSE AN EVOLUTION";

            IReadOnlyList<CardData> options = step.BranchOptions;
            int count = options.Count;
            float startX = -(count - 1) * 0.5f * _cardSpacing;
            for (int i = 0; i < count; i++)
            {
                CardData option = options[i];
                if (option == null) continue;

                CardView view = Instantiate(_cardViewPrefab, _cardListContainer);
                view.Bind(option);
                view.DisableHoverLift();
                view.SetInteractable(true);
                RectTransform rect = (RectTransform)view.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(startX + i * _cardSpacing, 0f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                view.RecordLayoutPose();

                string capturedRoot = familyRootId;
                string capturedEvolveId = option.cardId;
                view.OnClicked += (_, _) => Confirm(capturedRoot, capturedEvolveId);

                AddHintLabel(rect, $"→ {option.cardName}");
                _spawnedCards.Add(view);
            }
        }

        private void Confirm(string familyRootId, string chosenEvolveToId)
        {
            if (_pendingCard == null) { Close(); return; }
            _cardPlayManager.CommitUpgrade(_pendingCard, familyRootId, chosenEvolveToId);
            _pendingCard = null;
            CloseImmediate();
        }

        private void Close()
        {
            // Cancel: no MP spent, the Upgrade card stays in hand.
            _pendingCard = null;
            CloseImmediate();
        }

        private void CloseImmediate()
        {
            ClearCards();
            if (_root != null) _root.SetActive(false);
        }

        private void AddHintLabel(RectTransform cardRect, string text)
        {
            GameObject go = new("UpgradeHint", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(cardRect, false);
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -10f);
            rect.sizeDelta = new Vector2(200f, 40f);

            TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
            label.text = text;
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 22f;
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(1f, 0.92f, 0.55f, 1f);
            label.raycastTarget = false;
            GameFonts.Apply(label, GameFontRole.Title);
            _hintLabels.Add(go);
        }

        private void ClearCards()
        {
            foreach (CardView view in _spawnedCards)
            {
                if (view != null) Destroy(view.gameObject);
            }
            _spawnedCards.Clear();
            foreach (GameObject go in _hintLabels)
            {
                if (go != null) Destroy(go);
            }
            _hintLabels.Clear();
        }

        // Builds the modal overlay the first time it's needed if it wasn't authored in the scene:
        // a full-screen raycast-blocking dim backdrop (click = cancel), a title, a centered card
        // row, and a Cancel button. Parented to the top canvas so it draws over everything.
        private void EnsureBuilt()
        {
            if (_built || _root != null) { _built = true; return; }
            _built = true;

            Canvas canvas = FindFirstObjectByType<Canvas>();
            Transform parent = canvas != null ? canvas.transform : transform;

            // Root + full-screen dim backdrop (blocks clicks behind the modal).
            GameObject root = NewFullScreen("UpgradeModal", parent);
            _root = root;
            Image dim = root.AddComponent<Image>();
            dim.color = new Color(0f, 0f, 0f, 0.72f);
            _backdropButton = root.AddComponent<Button>();
            _backdropButton.transition = Selectable.Transition.None;
            _backdropButton.onClick.AddListener(Close);

            // Title.
            GameObject titleGo = new("Title", typeof(RectTransform));
            RectTransform titleRect = titleGo.GetComponent<RectTransform>();
            titleRect.SetParent(root.transform, false);
            titleRect.anchorMin = new Vector2(0.5f, 0.5f);
            titleRect.anchorMax = new Vector2(0.5f, 0.5f);
            titleRect.pivot = new Vector2(0.5f, 0.5f);
            titleRect.anchoredPosition = new Vector2(0f, 210f);
            titleRect.sizeDelta = new Vector2(900f, 70f);
            _titleText = titleGo.AddComponent<TextMeshProUGUI>();
            _titleText.alignment = TextAlignmentOptions.Center;
            _titleText.fontSize = 40f;
            _titleText.fontStyle = FontStyles.Bold;
            _titleText.color = Color.white;
            _titleText.raycastTarget = false;
            GameFonts.Apply(_titleText, GameFontRole.Title);

            // Card row container (clicks on cards must pass through the backdrop, so this sits
            // above it as a later sibling).
            GameObject list = new("CardList", typeof(RectTransform));
            RectTransform listRect = list.GetComponent<RectTransform>();
            listRect.SetParent(root.transform, false);
            listRect.anchorMin = listRect.anchorMax = new Vector2(0.5f, 0.5f);
            listRect.pivot = new Vector2(0.5f, 0.5f);
            listRect.anchoredPosition = Vector2.zero;
            listRect.sizeDelta = new Vector2(1600f, 360f);
            _cardListContainer = listRect;

            // Cancel button.
            GameObject cancelGo = new("CancelButton", typeof(RectTransform));
            RectTransform cancelRect = cancelGo.GetComponent<RectTransform>();
            cancelRect.SetParent(root.transform, false);
            cancelRect.anchorMin = cancelRect.anchorMax = new Vector2(0.5f, 0.5f);
            cancelRect.pivot = new Vector2(0.5f, 0.5f);
            cancelRect.anchoredPosition = new Vector2(0f, -250f);
            cancelRect.sizeDelta = new Vector2(240f, 72f);
            Image cancelBg = cancelGo.AddComponent<Image>();
            cancelBg.color = new Color(0.55f, 0.18f, 0.20f, 0.95f);
            _cancelButton = cancelGo.AddComponent<Button>();
            _cancelButton.onClick.AddListener(Close);

            GameObject cancelLabelGo = new("Label", typeof(RectTransform));
            RectTransform cancelLabelRect = cancelLabelGo.GetComponent<RectTransform>();
            cancelLabelRect.SetParent(cancelGo.transform, false);
            cancelLabelRect.anchorMin = Vector2.zero;
            cancelLabelRect.anchorMax = Vector2.one;
            cancelLabelRect.offsetMin = Vector2.zero;
            cancelLabelRect.offsetMax = Vector2.zero;
            TextMeshProUGUI cancelLabel = cancelLabelGo.AddComponent<TextMeshProUGUI>();
            cancelLabel.text = "CANCEL";
            cancelLabel.alignment = TextAlignmentOptions.Center;
            cancelLabel.fontSize = 30f;
            cancelLabel.fontStyle = FontStyles.Bold;
            cancelLabel.color = Color.white;
            cancelLabel.raycastTarget = false;
            GameFonts.Apply(cancelLabel, GameFontRole.Title);

            root.SetActive(false);
        }

        private static GameObject NewFullScreen(string name, Transform parent)
        {
            GameObject go = new(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.SetAsLastSibling();
            return go;
        }
    }
}
