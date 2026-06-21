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
                _cardPlayManager.OnLuckyDrawResolved += HandleLuckyDrawResolved;
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
                _cardPlayManager.OnLuckyDrawResolved -= HandleLuckyDrawResolved;
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

        // Lucky Draw resolved: the rolled cards aren't in the hand yet. Play a reveal-then-fly
        // cinematic, then commit them so the fan re-positions exactly as the cards arrive.
        private void HandleLuckyDrawResolved(CardData luckyCard, List<CardData> drawnCards)
        {
            if (drawnCards == null || drawnCards.Count == 0)
            {
                return;
            }
            // If the scene lacks the bits the animation needs, just commit instantly (no loss).
            if (_handContainer == null || _cardViewPrefab == null || _cardPlayManager == null)
            {
                _cardPlayManager?.CommitLuckyDraw(drawnCards);
                return;
            }
            StartCoroutine(PlayLuckyDrawReveal(drawnCards));
        }

        [Header("Lucky Draw reveal")]
        [SerializeField] private float _luckyRevealScale = 1.35f;
        [SerializeField] private float _luckyRevealSpread = 280f;   // px between the two centered cards (clear gap, no overlap)
        [SerializeField] private float _luckyAppearDuration = 0.28f;
        [SerializeField] private float _luckyHoldDuration = 0.55f;
        [SerializeField] private float _luckyFlyDuration = 0.5f;
        [SerializeField] private float _luckyStagger = 0.08f;       // per-card delay so they cascade

        private IEnumerator PlayLuckyDrawReveal(List<CardData> drawnCards)
        {
            _playingCardAnimation = true;
            RefreshHandInteractable();

            int existing = _cardViews.Count;
            int incoming = drawnCards.Count;
            int newTotal = existing + incoming;

            // Where the hand container sits in its own anchored space; cards start their reveal at
            // canvas center, which we express relative to the hand container so the fly lands right.
            RectTransform handRect = (RectTransform)_handContainer;
            Vector2 centerInHand = WorldCenterAnchored(handRect);

            // Spawn the reveal cards parented to the hand so their coords share fan space. They show
            // fully opaque (active, like a normal card) — no fade — and pop in with a scale punch.
            List<RectTransform> temps = new(incoming);
            float spreadStart = -(incoming - 1) * 0.5f * _luckyRevealSpread;
            for (int i = 0; i < incoming; i++)
            {
                CardView view = Instantiate(_cardViewPrefab, _handContainer);
                view.Bind(drawnCards[i]);
                // Show the same effective cost the card will have in the hand (dynamic Upgrade cost
                // and/or the Lucky-Draw first-card discount), so the reveal matches what lands.
                if (_cardPlayManager != null && drawnCards[i] != null && drawnCards[i].cardType == CardType.Support)
                {
                    view.SetCostOverride(_cardPlayManager.EffectiveMpCost(drawnCards[i]));
                }
                // Keep it visually "active" (white artwork, not the grey disabled tint). The
                // CanvasGroup below blocks clicks during the reveal instead.
                view.SetInteractable(true);
                RectTransform rect = (RectTransform)view.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = centerInHand + new Vector2(spreadStart + i * _luckyRevealSpread, 0f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one * (_luckyRevealScale * 0.8f);
                rect.SetAsLastSibling();
                // Disable raycasts only (no alpha animation) so the cards aren't clickable mid-reveal.
                CanvasGroup cg = view.gameObject.GetComponent<CanvasGroup>();
                if (cg == null) cg = view.gameObject.AddComponent<CanvasGroup>();
                cg.alpha = 1f;
                cg.blocksRaycasts = false;
                temps.Add(rect);
            }

            // Appear: pure scale pop so they read as "dealt" (fully visible the whole time).
            float elapsed = 0f;
            while (elapsed < _luckyAppearDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _luckyAppearDuration);
                float eased = 1f - (1f - t) * (1f - t);
                for (int i = 0; i < temps.Count; i++)
                {
                    if (temps[i] != null) temps[i].localScale = Vector3.one * Mathf.Lerp(_luckyRevealScale * 0.8f, _luckyRevealScale, eased);
                }
                yield return null;
            }

            yield return new WaitForSeconds(_luckyHoldDuration);

            // Fly phase: existing cards spread to their future slots while the reveal cards fly to
            // the tail slots of the new fan. Capture starts first so both motions interpolate cleanly.
            Vector2[] existingStartPos = new Vector2[existing];
            Quaternion[] existingStartRot = new Quaternion[existing];
            for (int i = 0; i < existing; i++)
            {
                RectTransform r = (RectTransform)_cardViews[i].transform;
                existingStartPos[i] = r.anchoredPosition;
                existingStartRot[i] = r.localRotation;
            }

            Vector2[] tempStartPos = new Vector2[incoming];
            for (int i = 0; i < incoming; i++)
            {
                tempStartPos[i] = temps[i] != null ? temps[i].anchoredPosition : centerInHand;
            }

            float flyTotal = _luckyFlyDuration + _luckyStagger * Mathf.Max(0, incoming - 1);
            elapsed = 0f;
            while (elapsed < flyTotal)
            {
                elapsed += Time.deltaTime;

                // Reslide the existing hand toward its new layout for the whole window.
                float spreadT = Mathf.Clamp01(elapsed / _luckyFlyDuration);
                float spreadEased = 1f - (1f - spreadT) * (1f - spreadT);
                for (int i = 0; i < existing; i++)
                {
                    if (_cardViews[i] == null) continue;
                    RectTransform r = (RectTransform)_cardViews[i].transform;
                    FanPose(i, newTotal, out Vector2 target, out Quaternion targetRot);
                    r.anchoredPosition = Vector2.Lerp(existingStartPos[i], target, spreadEased);
                    r.localRotation = Quaternion.Slerp(existingStartRot[i], targetRot, spreadEased);
                }

                // Reveal cards fly to their tail slots, staggered.
                for (int i = 0; i < incoming; i++)
                {
                    if (temps[i] == null) continue;
                    float start = i * _luckyStagger;
                    float t = Mathf.Clamp01((elapsed - start) / _luckyFlyDuration);
                    float eased = 1f - (1f - t) * (1f - t);
                    FanPose(existing + i, newTotal, out Vector2 target, out Quaternion targetRot);
                    temps[i].anchoredPosition = Vector2.Lerp(tempStartPos[i], target, eased);
                    temps[i].localRotation = Quaternion.Slerp(Quaternion.identity, targetRot, eased);
                    temps[i].localScale = Vector3.Lerp(Vector3.one * _luckyRevealScale, Vector3.one, eased);
                }
                yield return null;
            }

            // Commit to the hand: RefreshHand rebuilds the fan at exactly the slots we flew to, so
            // the handoff is seamless. Then drop the reveal copies.
            _cardPlayManager.CommitLuckyDraw(drawnCards);
            foreach (RectTransform r in temps)
            {
                if (r != null) Destroy(r.gameObject);
            }

            _playingCardAnimation = false;
            RefreshHandInteractable();
        }

        // The hand container's local-anchored coordinate for the canvas centre, so reveal cards can
        // start centre-screen while parented to the hand (whose origin is near the bottom).
        private Vector2 WorldCenterAnchored(RectTransform handRect)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screenCenter = new(Screen.width * 0.5f, Screen.height * 0.5f);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(handRect, screenCenter, cam, out Vector2 local))
            {
                return local;
            }
            return new Vector2(0f, 260f);
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

        // Draws the player's eye to the MP pool when they try to play a card they can't afford:
        // flashes the number red while punching its scale up/down and shaking it side to side.
        // Restores the resting color/scale/position when done.
        private void PlayMpDeniedFeedback()
        {
            if (_mpText == null) return;
            if (_mpDeniedFeedback != null) StopCoroutine(_mpDeniedFeedback);
            _mpDeniedFeedback = StartCoroutine(MpDeniedFeedbackRoutine());
        }

        private Coroutine _mpDeniedFeedback;
        private static readonly Color _mpDeniedColor = new(0.95f, 0.2f, 0.18f, 1f);

        private IEnumerator MpDeniedFeedbackRoutine()
        {
            RectTransform rect = _mpText.rectTransform;
            Color baseColor = _mpText.color;
            Vector3 baseScale = rect.localScale;
            Vector2 basePos = rect.anchoredPosition;

            const float duration = 0.45f;
            const float shakeMagnitude = 7f;   // px peak side-to-side
            const float shakeFreq = 34f;        // rad/sec
            const float zoomAmount = 0.35f;     // peak extra scale

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (_mpText == null) yield break;
                float t = Mathf.Clamp01(elapsed / duration);
                float decay = 1f - t;                       // settle out over time
                float pulse = Mathf.Sin(t * Mathf.PI);      // 0→1→0 across the window

                // Blink red 3 times, easing back to the base color as it settles.
                float redBlend = (Mathf.Sin(t * Mathf.PI * 6f) * 0.5f + 0.5f) * decay;
                _mpText.color = Color.Lerp(baseColor, _mpDeniedColor, redBlend);

                // Zoom punch.
                rect.localScale = baseScale * (1f + zoomAmount * pulse);

                // Horizontal shake, amplitude decaying with time.
                rect.anchoredPosition = basePos + new Vector2(Mathf.Sin(elapsed * shakeFreq) * shakeMagnitude * decay, 0f);

                yield return null;
            }

            if (_mpText != null)
            {
                _mpText.color = baseColor;
                rect.localScale = baseScale;
                rect.anchoredPosition = basePos;
            }
            _mpDeniedFeedback = null;
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
                // Show the live effective cost rather than the static config value: the Upgrade
                // card's cost escalates each use, and Lucky-Draw cards may carry a one-off discount.
                // For an ordinary card with no discount this equals mpCost, so the face is unchanged.
                if (_cardPlayManager != null && card != null && card.cardType == CardType.Support)
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

            FanPose(index, total, out Vector2 anchored, out Quaternion rotation);
            rect.anchoredPosition = anchored;
            rect.localRotation = rotation;
            rect.localScale = Vector3.one;
        }

        // The resting anchored position + rotation a card at `index` of `total` takes in the hand
        // fan. Pure (mutates nothing) so animations can fly cards toward a slot before the hand is
        // rebuilt there. Mirrors the math applied in PositionInFan.
        private void FanPose(int index, int total, out Vector2 anchored, out Quaternion rotation)
        {
            if (total <= 1)
            {
                anchored = Vector2.zero;
                rotation = Quaternion.identity;
                return;
            }

            float center = (total - 1) / 2f;
            float t = (index - center) / center;
            float x = (index - center) * _fanSpacing;
            float y = -t * t * _fanArcDrop;
            float angle = -t * _fanMaxAngle;
            anchored = new Vector2(x, y);
            rotation = Quaternion.Euler(0f, 0f, angle);
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
                // Affordability no longer greys/disables the card: an unaffordable card stays
                // interactive so the player can still pick it up, and we blink its cost red on use.
                // Only the card-play animation lock actually disables interaction here.
                view.SetInteractable(!_playingCardAnimation);
                if (_cardPlayManager != null)
                {
                    view.SetAffordable(_cardPlayManager.CanAffordCard(view.CardData));
                }
            }
        }

        private void HandleCardClicked(CardView view, CardData card)
        {
            if (_cardPlayManager == null || _playingCardAnimation)
            {
                return;
            }

            // Can't afford it: keep the card in hand and call attention to the MP pool (blink red +
            // zoom + shake) instead of refusing silently. (Unit cards are free, so this only ever
            // fires for spell/support cards.)
            if (card != null && !_cardPlayManager.CanAffordCard(card))
            {
                PlayMpDeniedFeedback();
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
                // If MP is the reason, call attention to the MP pool so the player sees why it bounced.
                if (!_cardPlayManager.CanAffordCard(card))
                {
                    PlayMpDeniedFeedback();
                }
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

            // Player-ally-line buff spells (Rally, Quick Shield, Barrier, Duplicated) need a player
            // unit to act on. The nearest-band fallback in FindPlayerLaneAtScreenPoint always yields
            // a lane even with an empty player side, so without this the spell would resolve and waste
            // MP on nothing. Treat the drop as a cancel when there's no on-field unit or pending build.
            if (CardPlayManager.RequiresLaneTarget(card) && !CardPlayManager.TargetsEnemyLane(card)
                && _battlefieldView != null && !_battlefieldView.HasAnyPlayerPresence)
            {
                Debug.Log("[UIManager] Player-line spell with no player units — cancelling");
                view.SnapBackToLayout();
                return;
            }

            // Deferred-modal spells (Upgrade Unit, Emergency Draft) only OPEN a picker on play —
            // no MP is spent and the card stays in hand until the player confirms (CommitUpgrade /
            // CommitEmergencyDraft) or cancels. So unlike instant spells, the view must NOT be
            // detached and fade-destroyed here; we raise the modal and snap the card back into the
            // fan. On confirm, OnCardPlayed → RefreshHand rebuilds the hand without it; on cancel it
            // simply stays. (Destroying it here made a cancel look like the card vanished.)
            if (CardPlayManager.IsUpgradeUnitCard(card) || CardPlayManager.IsEmergencyDraftCard(card))
            {
                bool opened = _cardPlayManager.TryPlayCard(card, targetLane, consume: true);
                Debug.Log($"[UIManager] Deferred-modal spell {card.cardName} opened={opened}");
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
