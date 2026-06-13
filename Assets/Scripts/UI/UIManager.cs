using System;
using System.Collections;
using System.Collections.Generic;
using DraftCards.Core;
using DraftCards.Data;
using DraftCards.Managers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DraftCards.UI
{
    public class UIManager : MonoBehaviour
    {
        [SerializeField] private MPManager _mpManager;
        [SerializeField] private HandManager _handManager;
        [SerializeField] private CardPlayManager _cardPlayManager;
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private BattlefieldView _battlefieldView;

        [SerializeField] private Transform _handContainer;
        [SerializeField] private CardView _cardViewPrefab;
        [SerializeField] private PreviewPanel _previewPanel;
        [SerializeField] private TMP_Text _mpText;
        [SerializeField] private Button _endButton;
        [SerializeField] private UpgradeSelectionPanel _upgradePanel;
        [SerializeField] private EmergencySelectionPanel _emergencyPanel;

        [Header("Hand fan layout")]
        [SerializeField] private float _fanSpacing = 130f;
        [SerializeField] private float _fanMaxAngle = 10f;
        [SerializeField] private float _fanArcDrop = 24f;
        [SerializeField, Range(0f, 0.5f)] private float _cancelZoneScreenHeightFraction = 0.25f;

        private readonly List<CardView> _cardViews = new();
        private Action<DraftCards.Cards.PendingUnitBuild> _pendingChangedHandler;
        private bool _playingCardAnimation;
        private FormationLine? _lastHoveredDragLane;

        private void OnEnable()
        {
            // Defensive fallback: when the scene wasn't rebuilt after _battlefieldView was added,
            // the serialized field is null. Find it at runtime so drag-to-lane still works.
            if (_battlefieldView == null)
            {
                _battlefieldView = FindFirstObjectByType<BattlefieldView>();
            }

            if (_handManager != null) _handManager.OnHandChanged += RefreshHand;
            if (_mpManager != null) _mpManager.OnMpChanged += RefreshMp;
            if (_cardPlayManager != null)
            {
                _pendingChangedHandler = HandlePendingBuildChanged;
                _cardPlayManager.OnPendingBuildChanged += _pendingChangedHandler;
                _cardPlayManager.OnCardPlayed += HandleCardPlayed;
            }
            if (_gameManager != null) _gameManager.OnStateChanged += HandleStateChanged;

            EnsureUpgradePanel();
            EnsureEmergencyPanel();

            if (_mpManager != null) RefreshMp(_mpManager.CurrentMp, _mpManager.MaxMp);
            _previewPanel?.ShowEmpty();
        }

        // The Upgrade Unit spell opens a modal target picker. If the scene doesn't author one,
        // create it at runtime and hand it the refs it needs (it builds its own UI on first use).
        private void EnsureUpgradePanel()
        {
            if (_upgradePanel == null)
            {
                _upgradePanel = FindFirstObjectByType<UpgradeSelectionPanel>();
            }
            if (_upgradePanel == null)
            {
                GameObject go = new("UpgradeSelectionPanel");
                go.transform.SetParent(transform, false);
                _upgradePanel = go.AddComponent<UpgradeSelectionPanel>();
            }
            _upgradePanel.Configure(_cardPlayManager, _cardViewPrefab);
        }

        // The Emergency Draft spell opens its own modal unit picker. Same self-building pattern as
        // the upgrade panel: create it at runtime if the scene doesn't author one, then hand it
        // the refs it needs (it builds its own UI on first use).
        private void EnsureEmergencyPanel()
        {
            if (_emergencyPanel == null)
            {
                _emergencyPanel = FindFirstObjectByType<EmergencySelectionPanel>();
            }
            if (_emergencyPanel == null)
            {
                GameObject go = new("EmergencySelectionPanel");
                go.transform.SetParent(transform, false);
                _emergencyPanel = go.AddComponent<EmergencySelectionPanel>();
            }
            _emergencyPanel.Configure(_cardPlayManager, _cardViewPrefab);
        }

        private void OnDisable()
        {
            if (_handManager != null) _handManager.OnHandChanged -= RefreshHand;
            if (_mpManager != null) _mpManager.OnMpChanged -= RefreshMp;
            if (_cardPlayManager != null)
            {
                if (_pendingChangedHandler != null)
                {
                    _cardPlayManager.OnPendingBuildChanged -= _pendingChangedHandler;
                    _pendingChangedHandler = null;
                }
                _cardPlayManager.OnCardPlayed -= HandleCardPlayed;
            }
            if (_gameManager != null) _gameManager.OnStateChanged -= HandleStateChanged;
        }

        private void HandlePendingBuildChanged(DraftCards.Cards.PendingUnitBuild build)
        {
            _previewPanel?.Show(build);
            RefreshHandInteractable();
        }

        private void HandleCardPlayed()
        {
            RefreshHandInteractable();
            // Belt-and-braces: ensure lane overlays are never left visible after any card play.
            if (_battlefieldView != null) _battlefieldView.HidePlayerLaneTargets();
            _lastHoveredDragLane = null;
        }

        private void HandleStateChanged(GameState state)
        {
            bool drafting = state == GameState.DrawPhase
                            || state == GameState.SelectCardPhase
                            || state == GameState.PreviewPhase;
            if (_handContainer != null) _handContainer.gameObject.SetActive(drafting);
            if (_endButton != null) _endButton.gameObject.SetActive(drafting);
        }


        private void RefreshMp(int current, int max)
        {
            if (_mpText != null)
            {
                _mpText.text = current.ToString();
            }
        }

        private void RefreshHand()
        {
            foreach (CardView view in _cardViews)
            {
                if (view != null) Destroy(view.gameObject);
            }
            _cardViews.Clear();

            if (_handManager == null || _cardViewPrefab == null || _handContainer == null)
            {
                return;
            }

            int total = _handManager.Cards.Count;
            for (int i = 0; i < total; i++)
            {
                CardData card = _handManager.Cards[i];
                CardView view = Instantiate(_cardViewPrefab, _handContainer);
                view.Bind(card);
                // The Upgrade card's cost escalates each use, so show the live cost, not the
                // static config value.
                if (_cardPlayManager != null && CardPlayManager.IsUpgradeUnitCard(card))
                {
                    view.SetCostOverride(_cardPlayManager.EffectiveMpCost(card));
                }
                view.OnClicked += HandleCardClicked;
                view.OnDragStarted += HandleCardDragStarted;
                view.OnDragMoved += HandleCardDragMoved;
                view.OnDragReleased += HandleCardDragReleased;
                view.OnLayoutOrderDirty += HandleLayoutOrderDirty;
                PositionInFan(view, i, total);
                view.RecordLayoutPose();
                _cardViews.Add(view);
            }

            RefreshHandInteractable();
        }

        private void PositionInFan(CardView view, int index, int total)
        {
            RectTransform rect = (RectTransform)view.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.localScale = Vector3.one;

            if (total <= 1)
            {
                rect.anchoredPosition = Vector2.zero;
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                return;
            }

            float center = (total - 1) / 2f;
            float t = (index - center) / center;

            float x = (index - center) * _fanSpacing;
            float y = -t * t * _fanArcDrop;
            float angle = -t * _fanMaxAngle;

            rect.anchoredPosition = new Vector2(x, y);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        // A card finished a hover/drag and dropped back into the fan. Reassert the whole hand's
        // sibling order from _cardViews (deck order) so the just-released card returns to its slot
        // and cards that were lifted earlier aren't left out of order.
        private void HandleLayoutOrderDirty(CardView _)
        {
            for (int i = 0; i < _cardViews.Count; i++)
            {
                CardView view = _cardViews[i];
                if (view != null) view.transform.SetSiblingIndex(i);
            }
        }

        private void RefreshHandInteractable()
        {
            if (_cardPlayManager == null)
            {
                return;
            }
            foreach (CardView view in _cardViews)
            {
                view.SetInteractable(!_playingCardAnimation && _cardPlayManager.CanPlayCard(view.CardData));
            }
        }

        private void HandleCardClicked(CardView view, CardData card)
        {
            if (_cardPlayManager == null || _playingCardAnimation)
            {
                return;
            }

            if (card != null && card.cardType == CardType.Unit)
            {
                StartCoroutine(PlayUnitCardThenApply(view, card));
                return;
            }

            _cardPlayManager.TryPlayCard(card);
        }

        private IEnumerator PlayUnitCardThenApply(CardView view, CardData card)
        {
            _playingCardAnimation = true;
            RefreshHandInteractable();

            if (view != null)
            {
                yield return view.PlayUnitSelectAnimation();
            }

            _cardPlayManager.TryPlayCard(card);
            _playingCardAnimation = false;
            RefreshHandInteractable();
        }

        private void HandleCardDragStarted(CardView view, CardData card)
        {
            bool requires = CardPlayManager.RequiresLaneTarget(card);
            bool targetsEnemy = CardPlayManager.TargetsEnemyLane(card);
            _lastHoveredDragLane = null;
            Debug.Log($"[UIManager] DragStarted card={card?.cardName} requiresLane={requires} targetsEnemy={targetsEnemy} battlefieldView={(_battlefieldView != null ? "OK" : "NULL")}");
            if (_battlefieldView == null) return;
            if (requires)
            {
                if (targetsEnemy) _battlefieldView.ShowEnemyLaneTargets();
                else _battlefieldView.ShowPlayerLaneTargets();
            }
        }

        private void HandleCardDragMoved(CardView view, CardData card, Vector2 screenPos)
        {
            if (_battlefieldView == null) return;
            if (!CardPlayManager.RequiresLaneTarget(card)) return;
            bool targetsEnemy = CardPlayManager.TargetsEnemyLane(card);
            FormationLine? lane = targetsEnemy
                ? _battlefieldView.FindEnemyLaneAtScreenPoint(screenPos)
                : _battlefieldView.FindPlayerLaneAtScreenPoint(screenPos);
            _lastHoveredDragLane = lane;
            _battlefieldView.HighlightHoveredLane(lane);
        }

        private bool IsInCancelZone(Vector2 screenPos)
        {
            float fraction = _cancelZoneScreenHeightFraction > 0f ? _cancelZoneScreenHeightFraction : 0.25f;
            float threshold = Screen.height * fraction;
            bool inZone = screenPos.y < threshold;
            Debug.Log($"[UIManager] IsInCancelZone screenY={screenPos.y} screenH={Screen.height} threshold={threshold} fraction={fraction} → {inZone}");
            return inZone;
        }

        private void HandleCardDragReleased(CardView view, CardData card, Vector2 screenPos)
        {
            Debug.Log($"[UIManager] DragReleased card={card?.cardName} screenPos={screenPos}");
            if (_battlefieldView != null) _battlefieldView.HidePlayerLaneTargets();
            if (_cardPlayManager == null || card == null || view == null)
            {
                Debug.Log($"[UIManager] DragReleased early-out: cardPlayMgr={_cardPlayManager != null} card={card != null} view={view != null}");
                return;
            }

            // Cancel if released below the lower fraction of the canvas (hand/deck area).
            if (IsInCancelZone(screenPos))
            {
                Debug.Log($"[UIManager] Drag cancelled (in bottom cancel zone) — snapping back");
                view.SnapBackToLayout();
                _lastHoveredDragLane = null;
                return;
            }

            if (!_cardPlayManager.CanPlayCard(card))
            {
                Debug.Log($"[UIManager] Cannot play {card.cardName}, snapping back");
                view.SnapBackToLayout();
                return;
            }

            // Prefer the lane the user was hovering when they released — matches the visible highlight.
            DraftCards.Core.FormationLine? targetLane = _lastHoveredDragLane;
            if (!targetLane.HasValue && _battlefieldView != null)
            {
                targetLane = CardPlayManager.TargetsEnemyLane(card)
                    ? _battlefieldView.FindEnemyLaneAtScreenPoint(screenPos)
                    : _battlefieldView.FindPlayerLaneAtScreenPoint(screenPos);
            }
            _lastHoveredDragLane = null;
            Debug.Log($"[UIManager] Resolved targetLane={(targetLane.HasValue ? targetLane.Value.ToString() : "null")}");

            // Lane-target spells (Duplicate): if user dropped off-lane, treat as cancel.
            if (CardPlayManager.RequiresLaneTarget(card) && !targetLane.HasValue)
            {
                Debug.Log("[UIManager] Lane-target spell dropped off-lane — cancelling");
                view.SnapBackToLayout();
                return;
            }

            Canvas canvas = GetComponentInParent<Canvas>();
            Transform fadeParent = canvas != null ? canvas.transform : view.transform.parent;
            view.transform.SetParent(fadeParent, worldPositionStays: true);
            view.transform.SetAsLastSibling();
            _cardViews.Remove(view);

            bool played = _cardPlayManager.TryPlayCard(card, targetLane, consume: true);
            Debug.Log($"[UIManager] TryPlayCard result={played}");
            if (!played)
            {
                view.transform.SetParent(_handContainer, worldPositionStays: true);
                view.SnapBackToLayout();
                _cardViews.Add(view);
                return;
            }

            StartCoroutine(FadeAndDestroyCard(view));
        }

        private IEnumerator FadeAndDestroyCard(CardView view)
        {
            if (view == null) yield break;
            GameObject go = view.gameObject;
            if (go == null) yield break;
            Debug.Log($"[UIManager] FadeAndDestroyCard start, world pos={go.transform.position}");
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            if (cg == null) cg = go.AddComponent<CanvasGroup>();
            cg.blocksRaycasts = false;
            cg.interactable = false;

            Transform t = go.transform;
            Vector3 startScale = t.localScale;
            Vector3 endScale = startScale * 0.55f;

            const float duration = 0.55f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                if (go == null || t == null || cg == null) yield break;
                float u = Mathf.Clamp01(elapsed / duration);
                cg.alpha = 1f - u;
                t.localScale = Vector3.Lerp(startScale, endScale, u);
                yield return null;
            }

            if (go != null) Destroy(go);
        }
    }
}
