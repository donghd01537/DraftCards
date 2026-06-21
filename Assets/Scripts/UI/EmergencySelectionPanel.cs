using System.Collections.Generic;
using DraftCards.Data;
using DraftCards.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DraftCards.UI
{
    // Modal unit picker for the Emergency Draft spell. When the spell is played, this opens and
    // lists the unit choices rolled for this play (two random draftable units, in their current
    // tier — see CardPlayManager.GetEmergencyDraftOptions). Clicking a card commits the draft:
    // that unit's full group spawns onto the field as TEMPORARY reinforcements for this wave.
    // Cancel (or the dimmed backdrop) aborts with no MP spent and the card left in hand.
    //
    // Mirrors UpgradeSelectionPanel: self-building (constructs its overlay at runtime if _root
    // isn't wired in the Inspector), and only needs _cardPlayManager + _cardViewPrefab (both
    // auto-resolved when possible). The two pickers are intentionally separate panels so each
    // owns its own roll/commit lifecycle.
    public class EmergencySelectionPanel : MonoBehaviour
    {
        [SerializeField] private CardPlayManager _cardPlayManager;
        [SerializeField] private CardView _cardViewPrefab;

        [SerializeField] private GameObject _root;
        [SerializeField] private Transform _cardListContainer;
        [SerializeField] private Button _cancelButton;
        [SerializeField] private Button _backdropButton;
        [SerializeField] private TMP_Text _titleText;

        [Header("Card layout")]
        // Wider than the upgrade picker's default: Emergency Draft always shows 2 cards, so they
        // need a clear gap (cards are ~176px wide and the per-card hint label is 200px). At 220px
        // the cards/labels overlapped ("TEMPORARYTEMPORARY").
        [SerializeField] private float _cardSpacing = 320f;

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
            if (_cardPlayManager != null) _cardPlayManager.OnEmergencyDraftRequested += Open;
        }

        private void OnDisable()
        {
            if (_cardPlayManager != null) _cardPlayManager.OnEmergencyDraftRequested -= Open;
        }

        public bool IsOpen => _root != null && _root.activeSelf;

        // Opens the picker for an Emergency Draft play. Builds a card per rolled unit option.
        public void Open(CardData emergencyCard)
        {
            EnsureBuilt();
            if (_cardPlayManager == null || _cardViewPrefab == null || _cardListContainer == null)
            {
                Debug.LogWarning("[EmergencySelectionPanel] Missing refs (cardPlayManager/cardViewPrefab); cannot open. Emergency Draft cancelled.");
                return;
            }

            _pendingCard = emergencyCard;
            ClearCards();

            List<CardData> options = _cardPlayManager.GetEmergencyDraftOptions(emergencyCard);
            if (options.Count == 0)
            {
                // No draftable units to offer — don't strand the player in an empty modal.
                _pendingCard = null;
                return;
            }

            int cost = _cardPlayManager.EffectiveMpCost(emergencyCard);
            if (_titleText != null) _titleText.text = $"EMERGENCY DRAFT  —  {cost} MP  —  choose a unit";

            // Activate the modal BEFORE instantiating cards so each CardView's Awake runs
            // (instantiating under an inactive root leaves _button null → SetInteractable NREs).
            if (_root != null) _root.SetActive(true);

            int count = options.Count;
            float startX = -(count - 1) * 0.5f * _cardSpacing;
            for (int i = 0; i < count; i++)
            {
                CardData unitCard = options[i];
                if (unitCard == null) continue;

                CardView view = Instantiate(_cardViewPrefab, _cardListContainer);
                view.Bind(unitCard);
                view.DisableHoverLift();
                view.SetInteractable(true);
                RectTransform rect = (RectTransform)view.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = new Vector2(startX + i * _cardSpacing, 0f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                view.RecordLayoutPose();

                CardData captured = unitCard;
                view.OnClicked += (_, _) => Confirm(captured);

                AddHintLabel(rect, "TEMPORARY");
                _spawnedCards.Add(view);
            }

            if (_spawnedCards.Count == 0)
            {
                // No valid cards built — close the modal we just opened.
                _pendingCard = null;
                if (_root != null) _root.SetActive(false);
            }
        }

        private void Confirm(CardData chosenUnit)
        {
            if (_pendingCard == null) { Close(); return; }
            _cardPlayManager.CommitEmergencyDraft(_pendingCard, chosenUnit);
            _pendingCard = null;
            CloseImmediate();
        }

        private void Close()
        {
            // Cancel: no MP spent, the Emergency Draft card stays in hand.
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
            GameObject go = new("EmergencyHint", typeof(RectTransform));
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
            GameObject root = NewFullScreen("EmergencyDraftModal", parent);
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
