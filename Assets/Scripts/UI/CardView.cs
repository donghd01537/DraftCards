using System;
using System.Collections;
using DraftCards.Core;
using DraftCards.Data;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DraftCards.UI
{
    [RequireComponent(typeof(Button))]
    public class CardView : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler
    {
        [SerializeField] private TMP_Text _nameText;
        [SerializeField] private TMP_Text _typeText;
        [SerializeField] private TMP_Text _costText;
        [SerializeField] private TMP_Text _statsText;
        [SerializeField] private Image _frame;
        [SerializeField] private Image _artwork;
        [SerializeField] private GameObject _selectedBorder;

        [SerializeField] private Color _unitColor = new(0.3f, 0.45f, 0.85f);
        [SerializeField] private Color _supportColor = new(0.45f, 0.75f, 0.4f);
        [SerializeField] private Color _disabledColor = new(0.75f, 0.75f, 0.75f, 1f);

        // Archer_2.png is the current card-size source of truth (235x321).
        // Keep the existing 240px hand height and use the Archer aspect for width.
        [SerializeField] private Vector2 _fixedCardSize = new(176f, 240f);
        [SerializeField] private bool _scaleArtworkToFixedSize = true;

        [SerializeField] private float _hoverScale = 1.3f;
        [SerializeField] private float _hoverLift = 80f;
        [SerializeField] private float _selectPulseScale = 1.15f;
        [SerializeField] private float _selectPulseDuration = 0.18f;
        [SerializeField] private float _selectFlyDuration = 0.32f;
        [SerializeField] private float _selectReturnDuration = 0.18f;

        private Button _button;
        private CardData _cardData;
        private bool _interactable;
        private Vector3 _baseScale = Vector3.one;
        private Vector2 _baseAnchored;
        private Vector3 _layoutScale = Vector3.one;
        private Quaternion _layoutRotation = Quaternion.identity;
        private Vector2 _layoutAnchored;
        private bool _hoverActive;
        private bool _dragging;
        private bool _textOverlaysReady;
        // True between OnBeginDrag and the click that Unity fires immediately after OnEndDrag.
        // Lets us drop that synthetic click so a drag never also triggers the click flow.
        private bool _dragOccurred;

        public event Action<CardView, CardData> OnClicked;
        public event Action<CardView, CardData> OnDragStarted;
        public event Action<CardView, CardData, Vector2> OnDragMoved;
        public event Action<CardView, CardData, Vector2> OnDragReleased;
        // Raised when a hover/drag ends and the card should drop back into its hand slot.
        // The owner (UIManager) reasserts the full sibling order — restoring a single card by
        // its recorded absolute index is unreliable once other cards have reordered themselves.
        public event Action<CardView> OnLayoutOrderDirty;

        public CardData CardData => _cardData;
        public bool IsAnimatingSelection { get; private set; }

        // When set (>= 0), overrides the MP cost shown on the card face. Used for the Upgrade
        // card, whose cost escalates each use and so differs from the static config mpCost.
        private int _costOverride = -1;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(HandleClick);
            ApplyFixedCardSize();
            _baseScale = transform.localScale;
        }

        public void RecordLayoutPose()
        {
            RectTransform rect = (RectTransform)transform;
            _layoutScale = rect.localScale;
            _layoutRotation = rect.localRotation;
            _layoutAnchored = rect.anchoredPosition;

            _baseScale = _layoutScale;
            _baseAnchored = _layoutAnchored;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            // Fresh interaction: clear any leftover drag suppression so a plain click is honoured.
            _dragOccurred = false;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (!_interactable || _cardData == null)
            {
                Debug.Log($"[CardView] OnBeginDrag SKIPPED — interactable={_interactable}, card={_cardData?.cardName ?? "null"}");
                return;
            }
            _dragging = true;
            // A real drag started; the click Unity raises after OnEndDrag must be ignored.
            _dragOccurred = true;
            Debug.Log($"[CardView] OnBeginDrag: {_cardData.cardName}");
            if (_hoverActive)
            {
                _hoverActive = false;
                RestoreLayoutPose((RectTransform)transform);
            }
            transform.SetAsLastSibling();
            transform.localScale = _baseScale * _hoverScale;
            OnDragStarted?.Invoke(this, _cardData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging) return;
            RectTransform parentRect = transform.parent as RectTransform;
            if (parentRect == null) return;
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, eventData.position, camera, out Vector2 local))
            {
                ((RectTransform)transform).anchoredPosition = local;
            }
            if (_cardData != null)
            {
                OnDragMoved?.Invoke(this, _cardData, eventData.position);
            }
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
            {
                Debug.Log("[CardView] OnEndDrag SKIPPED — not dragging");
                return;
            }
            _dragging = false;
            Debug.Log($"[CardView] OnEndDrag: {_cardData?.cardName} at screen {eventData.position}");
            if (_cardData != null)
            {
                OnDragReleased?.Invoke(this, _cardData, eventData.position);
            }
        }

        public void SnapBackToLayout()
        {
            RestoreLayoutPose((RectTransform)transform);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_interactable || _hoverActive || _dragging) return;
            _hoverActive = true;

            RectTransform rect = (RectTransform)transform;
            _baseAnchored = _layoutAnchored;

            transform.SetAsLastSibling();
            transform.localScale = _baseScale * _hoverScale;
            rect.localRotation = Quaternion.identity;
            rect.anchoredPosition = _baseAnchored + new Vector2(0f, _hoverLift);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (_dragging) return;
            if (!_hoverActive) return;
            _hoverActive = false;

            RectTransform rect = (RectTransform)transform;
            RestoreLayoutPose(rect);
        }

        public void Bind(CardData cardData)
        {
            _cardData = cardData;
            _costOverride = -1;
            RefreshDisplay();
        }

        // Overrides the MP cost shown on the card face. Pass a negative value to clear it and
        // fall back to the card's static mpCost.
        public void SetCostOverride(int cost)
        {
            _costOverride = cost;
            RefreshDisplay();
        }

        public void SetInteractable(bool interactable)
        {
            _interactable = interactable;
            // _button is cached in Awake; guard in case this is called the same frame the view
            // is instantiated (e.g. before Awake has run).
            if (_button == null) _button = GetComponent<Button>();
            if (_button != null) _button.interactable = interactable;
            if (_artwork != null)
            {
                _artwork.color = interactable ? Color.white : _disabledColor;
            }
        }

        public void SetSelected(bool selected)
        {
            if (_selectedBorder != null)
            {
                _selectedBorder.SetActive(selected);
            }
        }

        public IEnumerator PlayUnitSelectAnimation()
        {
            if (IsAnimatingSelection) yield break;
            IsAnimatingSelection = true;

            RectTransform rect = (RectTransform)transform;
            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform parentRect = rect.parent as RectTransform;
            Vector2 startAnchored = rect.anchoredPosition;
            Quaternion startRotation = rect.localRotation;
            Vector3 startScale = rect.localScale;

            _hoverActive = false;
            transform.SetAsLastSibling();
            SetSelected(true);

            float elapsed = 0f;
            while (elapsed < _selectPulseDuration)
            {
                elapsed += Time.deltaTime;
                if (this == null || rect == null) { IsAnimatingSelection = false; yield break; }
                float t = Mathf.Clamp01(elapsed / _selectPulseDuration);
                float pulse = Mathf.Sin(t * Mathf.PI);
                rect.localScale = Vector3.Lerp(startScale, startScale * _selectPulseScale, pulse);
                if (_selectedBorder != null)
                {
                    _selectedBorder.SetActive(Mathf.FloorToInt(t * 6f) % 2 == 0);
                }
                yield return null;
            }

            if (this == null || rect == null) { IsAnimatingSelection = false; yield break; }
            SetSelected(true);
            Vector2 targetAnchored = startAnchored;
            if (parentRect != null)
            {
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                Vector2 screenCenter = new(Screen.width * 0.5f, Screen.height * 0.5f);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenCenter, camera, out targetAnchored);
            }

            elapsed = 0f;
            while (elapsed < _selectFlyDuration)
            {
                elapsed += Time.deltaTime;
                if (this == null || rect == null) { IsAnimatingSelection = false; yield break; }
                float t = Mathf.Clamp01(elapsed / _selectFlyDuration);
                float eased = 1f - (1f - t) * (1f - t);
                rect.anchoredPosition = Vector2.Lerp(startAnchored, targetAnchored, eased);
                rect.localRotation = Quaternion.Slerp(startRotation, Quaternion.identity, eased);
                rect.localScale = Vector3.Lerp(startScale * _selectPulseScale, startScale, eased);
                yield return null;
            }

            if (this == null || rect == null) { IsAnimatingSelection = false; yield break; }
            Vector2 centerAnchored = rect.anchoredPosition;
            Quaternion centerRotation = rect.localRotation;
            Vector3 centerScale = rect.localScale;
            elapsed = 0f;
            while (elapsed < _selectReturnDuration)
            {
                elapsed += Time.deltaTime;
                if (this == null || rect == null) { IsAnimatingSelection = false; yield break; }
                float t = Mathf.Clamp01(elapsed / _selectReturnDuration);
                float eased = 1f - (1f - t) * (1f - t);
                rect.anchoredPosition = Vector2.Lerp(centerAnchored, _layoutAnchored, eased);
                rect.localRotation = Quaternion.Slerp(centerRotation, _layoutRotation, eased);
                rect.localScale = Vector3.Lerp(centerScale, _layoutScale, eased);
                yield return null;
            }

            if (this == null || rect == null) { IsAnimatingSelection = false; yield break; }
            RestoreLayoutPose(rect);
            SetSelected(false);
            IsAnimatingSelection = false;
        }

        private void RestoreLayoutPose(RectTransform rect)
        {
            rect.localScale = _layoutScale;
            rect.localRotation = _layoutRotation;
            rect.anchoredPosition = _layoutAnchored;
            // Don't reorder by the recorded absolute index: while this card was lifted to the
            // front, other cards may have lifted too, so that index no longer maps to this slot.
            // Ask the owner to reassert the whole hand's order from its source-of-truth list.
            OnLayoutOrderDirty?.Invoke(this);
        }

        private void HandleClick()
        {
            // Unity fires Button.onClick right after OnEndDrag when the pointer is released over
            // the same card. Drop that synthetic click so drag and click stay independent flows.
            if (_dragOccurred)
            {
                _dragOccurred = false;
                Debug.Log($"[CardView] HandleClick suppressed (followed a drag): {_cardData?.cardName}");
                return;
            }
            if (!_interactable || _cardData == null)
            {
                return;
            }
            OnClicked?.Invoke(this, _cardData);
        }

        private void RefreshDisplay()
        {
            if (_cardData == null) return;
            ApplyFixedCardSize();
            if (_artwork != null) _artwork.sprite = _cardData.artwork;
            bool isSupport = _cardData.cardType == DraftCards.Core.CardType.Support;
            if (isSupport)
            {
                EnsureTextOverlays();
            }
            ApplyTextLayout(isSupport);
            if (_nameText != null) _nameText.text = string.Empty;
            if (_costText != null) _costText.text = isSupport
                ? (_costOverride >= 0 ? _costOverride : _cardData.mpCost).ToString()
                : string.Empty;
            if (_typeText != null) _typeText.text = string.Empty;
            if (_statsText != null) _statsText.text = isSupport ? SpellDescriptionText(_cardData) : string.Empty;
        }

        private void ApplyFixedCardSize()
        {
            RectTransform rect = (RectTransform)transform;
            if (_fixedCardSize.x > 0f && _fixedCardSize.y > 0f)
            {
                rect.sizeDelta = _fixedCardSize;
            }

            if (_artwork == null)
            {
                return;
            }

            _artwork.preserveAspect = !_scaleArtworkToFixedSize;
            if (_artwork.transform != transform)
            {
                ApplyRect(_artwork.rectTransform, Vector2.zero, Vector2.one);
            }
        }

        private void EnsureTextOverlays()
        {
            if (_textOverlaysReady) return;
            _textOverlaysReady = true;

            if (_costText == null)
            {
                _costText = BuildOverlayText("CostText", new Vector2(0.055f, 0.77f), new Vector2(0.235f, 0.965f),
                    24f, TextAlignmentOptions.Center, FontStyles.Bold, GameFontRole.Title);
            }
            if (_statsText == null)
            {
                _statsText = BuildOverlayText("StatsText", DescriptionAnchorMin, DescriptionAnchorMax,
                    11f, TextAlignmentOptions.Center, FontStyles.Bold, GameFontRole.Normal);
            }
        }

        private static readonly Vector2 DescriptionAnchorMin = new(0.22f, 0.12f);
        private static readonly Vector2 DescriptionAnchorMax = new(0.78f, 0.275f);

        private void ApplyTextLayout(bool isSupport)
        {
            if (_costText != null)
            {
                ApplyRect(_costText.rectTransform, new Vector2(0.055f, 0.77f), new Vector2(0.235f, 0.965f));
                ApplyTextAlignment(_costText, TextAlignmentOptions.Center);
            }

            if (_statsText != null)
            {
                ApplyRect(_statsText.rectTransform, DescriptionAnchorMin, DescriptionAnchorMax);
                ApplyTextAlignment(_statsText, TextAlignmentOptions.Center);
                _statsText.enableAutoSizing = true;
                _statsText.fontSizeMax = isSupport ? 10.5f : 10f;
                _statsText.fontSizeMin = 5.5f;
                _statsText.fontStyle = FontStyles.Bold;
                _statsText.margin = Vector4.zero;
                _statsText.lineSpacing = -12f;
            }
        }

        private static void ApplyRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            if (rect == null) return;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.anchoredPosition = Vector2.zero;
        }

        private static void ApplyTextAlignment(TMP_Text text, TextAlignmentOptions alignment)
        {
            if (text == null) return;
            text.alignment = alignment;
            text.horizontalAlignment = HorizontalAlignmentOptions.Center;
            text.verticalAlignment = VerticalAlignmentOptions.Middle;
        }

        private TMP_Text BuildOverlayText(string objectName, Vector2 anchorMin, Vector2 anchorMax,
            float fontSize, TextAlignmentOptions alignment, FontStyles style, GameFontRole fontRole)
        {
            GameObject go = new(objectName, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            TextMeshProUGUI text = go.AddComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            text.text = string.Empty;
            text.color = new Color(0.08f, 0.065f, 0.045f, 1f);
            ApplyTextAlignment(text, alignment);
            text.fontSize = fontSize;
            text.enableAutoSizing = true;
            text.fontSizeMax = fontSize;
            text.fontSizeMin = Mathf.Max(6f, fontSize * 0.55f);
            text.fontStyle = style;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.margin = Vector4.zero;
            GameFonts.Apply(text, fontRole);
            return text;
        }

        private static string SpellDescriptionText(CardData card)
        {
            if (card == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(card.cardDescription)) return card.cardDescription;
            return card.rulesText ?? string.Empty;
        }

        private void RefreshFrameColor()
        {
            // Card art is the full visual; no frame tint applied.
        }
    }
}
