using System.Collections;
using System.Collections.Generic;
using DraftCards.Core;
using DraftCards.UI;
using DraftCards.Units;
using UnityEngine;

namespace DraftCards.Battle
{
    [RequireComponent(typeof(RectTransform))]
    public class BattleUnit : MonoBehaviour
    {
        [SerializeField] private float _bodyRadius = 25f;
        [SerializeField] private float _allySeparationDistance = 46f;
        [SerializeField] private float _separationStrength = 420f;
        // Hard cap on the combined per-frame speed (separation + settle + move toward target).
        // Without it, a unit blocked behind allies stacks its move force onto the summed
        // separation pushes from every overlapping neighbor and shoves the whole formation
        // across the field. Set above the fastest possible effective move speed (Archer 280 +
        // 40% Rally ≈ 392) so honest chasing is never throttled — it only trims the runaway
        // pile-up pushes, which run into the thousands. 0 disables the cap.
        [SerializeField] private float _maxSpeed = 420f;
        // How much to soften the separation push against an ally that's standing between this
        // unit and the enemy it's trying to reach (1 = full push, 0 = none). Rear units press
        // forward into the back of the line instead of bulldozing through it.
        [SerializeField, Range(0f, 1f)] private float _blockedPushFactor = 0.2f;
        // How much to soften the separation push against an *opponent* (1 = full push, 0 = none).
        // With a deep formation the front line gets shoved into the enemy by everyone stacked
        // behind it, and those small pushes sum into the enemy line and slide the whole enemy
        // formation backward. Easing the cross-side push keeps contact and jostle without the
        // mob bulldozing the enemies off the field.
        [SerializeField, Range(0f, 1f)] private float _enemyPushFactor = 0.2f;
        [SerializeField] private float _dropHeight = 150f;
        [SerializeField] private float _dropDuration = 0.35f;
        [SerializeField] private float _dropScatterRadius = 12f;
        [SerializeField] private float _postDropSeparationDelay = 0.15f;
        // Hysteresis for the walk animation, asymmetric on purpose. A unit that's in range
        // attacking is never perfectly still — separation keeps nudging it in and out of range
        // for a frame or two — so STARTING the walk requires "moving" to hold for a brief
        // _moveStartDelay (swallowing those one-frame nudges so it doesn't bob mid-strike), while
        // STOPPING the walk is quicker so it settles promptly once it truly reaches the line.
        // Kept small: a long start delay lets the unit visibly SLIDE (position moves every frame)
        // for that whole window before the march animation catches up, which reads as gliding.
        [SerializeField] private float _moveStartDelay = 0.06f;
        [SerializeField] private float _moveStopDelay = 0.1f;
        // Minimum horizontal speed/offset (local units) before the unit flips to face that way.
        // A deadzone so near-vertical movement (e.g. a cavalry unit swinging straight up to the
        // edge) doesn't rapidly flip-flop the sprite.
        [SerializeField] private float _facingDeadzone = 6f;
        [SerializeField] private float _settleSpeed = 160f;
        [SerializeField] private float _dropPopScale = 1.12f;
        [SerializeField] private float _dropStartScale = 0.85f;
        [SerializeField] private float _activationDelayMin = 0.05f;
        [SerializeField] private float _activationDelayMax = 0.18f;
        [SerializeField] private float _projectileSpeed = 650f;
        // Height of the throw origin above the unit center, as a fraction of the sprite's
        // rendered height (the Cyclop holds its rock overhead, ~0.31 up from center).
        [SerializeField] private float _projectileOriginHeight = 0.31f;
        // Fallback origin height (local units) when the view's sprite height is unknown.
        [SerializeField] private float _projectileFallbackHeight = 50f;
        // Projectile render size, as a multiple of the unit art scale. >1 keeps small
        // sprites like arrows readable and makes boulders feel heavier.
        [SerializeField] private float _projectileSizeMultiplier = 1.5f;
        // Length of the throw animation (windup -> release). Fixed so the throw reads as a
        // deliberate motion instead of a frame-flicker, regardless of how fast the unit
        // attacks. Clamped below the attack cooldown so it always finishes before the next.
        [SerializeField] private float _projectileThrowDuration = 0.32f;
        // Length of a melee strike animation. Fixed (like the throw) so the swing reads as a
        // deliberate motion even when the unit attacks very fast — without this, fast attackers
        // (e.g. a Goblin at 0.4s cooldown) flash the strike pose for ~0.1s and look idle while
        // swarming. Clamped below the attack cooldown so it always finishes before the next swing.
        [SerializeField] private float _meleeAttackDuration = 0.28f;

        private bool _dropping;
        private float _activationDelay;

        private UnitGroupView _view;
        private RectTransform _rect;

        public UnitGroup Group { get; private set; }
        public UnitGroupView View => _view != null ? _view : (_view = GetComponent<UnitGroupView>());
        public RectTransform Rect => _rect != null ? _rect : (_rect = (RectTransform)transform);
        public float BodyRadius => _bodyRadius;
        public bool IsPlayerUnit => Group != null && Group.IsPlayerUnit;
        public bool IsDead => Group == null || Group.IsDead;
        public Vector2 CurrentVelocity { get; private set; }

        private IBattleSpatial _spatial;
        private float _cooldownTimer;
        private bool _active;
        private bool _moveVisualState;
        private bool _pendingMoveVisualState;
        private float _moveVisualStateChangedAt;
        private bool _hasSettleTarget;
        private Vector2 _settleTarget;
        // Set each frame in Update: true when this melee unit is screened from its target by an
        // ally (front-rank gating). While blocked it neither attacks nor pushes the enemy, so a
        // deep mob can't stack its separation pushes onto the enemy line through the bodies ahead.
        private bool _frontBlocked;
        // Counts this unit's normal attacks so a support healer (Cleric / Shaman) can heal
        // its side every Group.HealEveryAttacks swings. Reset when combat begins.
        private int _attackCount;

        // --- Cavalry flank charge -------------------------------------------------------
        // A Cavalry unit (Wolf Rider) doesn't charge straight into the enemy front line. It
        // first arcs out to the nearest top/bottom field edge and runs along it past the enemy
        // formation, then dives inward at its Middle/Back target. Once it has cleared the front
        // (reached the dive column) it falls through to normal chasing, so the flank only shapes
        // the *approach* — combat targeting stays in FindClosestOpponent.
        // Cavalry range across the taller runway (empty top/bottom); everything else stays in
        // the tight infantry band.
        private bool IsCavalry => Group != null && Group.UnitType == UnitType.Cavalry;

        private enum FlankPhase { None, Arcing, Done }
        private FlankPhase _flankPhase = FlankPhase.None;
        private bool _flankLatched;      // true once edge/contact line are fixed for this charge
        private float _flankEdgeY;       // the top/bottom edge row this unit sweeps along
        private float _flankDiveX;       // the contact-line X; past it the flank ends and it dives
        private float _flankStartYGap;   // |edgeY - startY| at latch, the denominator for the curve blend
        // How far toward the chosen edge to push the sweep — fraction of the half-height.
        // 1 = hug the very edge. Pushed near max because the field is short on the Y axis, so
        // the unit needs to commit fully to the edge for the flank to read.
        [SerializeField, Range(0f, 1f)] private float _flankEdgeReach = 0.98f;
        // Inset from the absolute edge so the unit doesn't ride the very clamp line.
        [SerializeField] private float _flankEdgeInset = 8f;
        // How close (in Y) to the edge counts as "arrived" — within this band the curve has fully
        // blended to the horizontal cross. Larger = the corner starts rounding sooner / more gently
        // (good now the runway is taller); smaller = a tighter, later corner.
        [SerializeField] private float _flankEdgeBand = 60f;

        public void Init(UnitGroup group, IBattleSpatial spatial)
        {
            Group = group;
            _spatial = spatial;
            _cooldownTimer = group.EffectiveAttackCooldown * 0.5f;
        }

        public void SetActive(bool active)
        {
            _active = active;
            if (active)
            {
                _hasSettleTarget = false;
                _activationDelay = Random.Range(_activationDelayMin, _activationDelayMax);
                _attackCount = 0;
                // Cavalry re-plans its flank each battle (it may have regrouped to a new spot).
                _flankPhase = Group != null && Group.UnitType == UnitType.Cavalry
                    ? FlankPhase.Arcing
                    : FlankPhase.None;
                _flankLatched = false;
                // Start the Fortify "first X seconds" window the moment combat begins.
                if (Group != null && Group.HasShield)
                {
                    Group.ActivateShield();
                    View?.SetShield(true);
                }
                // Likewise start the Rally speed-buff window when combat begins.
                if (Group != null && Group.HasRally)
                {
                    Group.ActivateRally();
                }
                if (Group != null && Group.HasSlow)
                {
                    Group.ActivateSlow();
                    View?.SetSlowTint(true);
                }
            }
        }

        public void ClearBattlefieldSpellEffects()
        {
            Group?.ClearBattlefieldSpellEffects();
            View?.SetShield(false);
            View?.SetSlowTint(false);
        }

        public void SetSpatial(IBattleSpatial spatial)
        {
            _spatial = spatial;
        }

        public void SetSettleTarget(Vector2 target)
        {
            _settleTarget = target;
            _hasSettleTarget = true;
        }

        public void StartDrop()
        {
            StartCoroutine(DropRoutine(_dropHeight, _dropDuration));
        }

        private IEnumerator DropRoutine(float fromHeight, float duration)
        {
            _dropping = true;

            RectTransform visual = View != null ? View.SpriteContainer : null;
            // Disable the legacy bounce animator during (and after) the drop. Every unit now
            // walks with UnitWalkAnimator instead, and UnitGroupView.ConfigureWalk keeps the
            // bounce animator off; we never re-enable it here, otherwise the two animators would
            // fight over the sprite container's transform and make the unit drift/float.
            MoveBounceAnimator bounce = visual != null ? visual.GetComponent<MoveBounceAnimator>() : null;
            if (bounce != null) bounce.enabled = false;

            Vector3 baseLocal = visual != null ? visual.localPosition : Vector3.zero;
            Vector2 scatter = Random.insideUnitCircle * _dropScatterRadius;
            Vector3 startLocal = baseLocal + new Vector3(scatter.x, fromHeight + scatter.y, 0f);
            Vector3 baseScale = visual != null ? visual.localScale : Vector3.one;
            Vector3 startScale = baseScale * _dropStartScale;
            Vector3 popScale = baseScale * _dropPopScale;
            if (visual != null)
            {
                visual.localPosition = startLocal;
                visual.localScale = startScale;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - (1f - u) * (1f - u);
                if (visual != null)
                {
                    visual.localPosition = Vector3.Lerp(startLocal, baseLocal, eased);
                    Vector3 scale = u < 0.75f
                        ? Vector3.Lerp(startScale, popScale, u / 0.75f)
                        : Vector3.Lerp(popScale, baseScale, (u - 0.75f) / 0.25f);
                    visual.localScale = scale;
                }
                yield return null;
            }
            if (visual != null)
            {
                visual.localPosition = baseLocal;
                visual.localScale = baseScale;
            }
            if (_postDropSeparationDelay > 0f)
            {
                yield return new WaitForSeconds(_postDropSeparationDelay);
            }

            _dropping = false;
        }

        public void TakeDamage(int damage)
        {
            if (IsDead) return;
            Group.TakeDamage(damage);
            // Hit feedback is the flash only. Rebuilding the sprites here would destroy the
            // frame animators mid-swing, so a unit under steady fire would never get to show
            // its own attack animation — it would look idle while still dealing damage.
            UnitGroupView view = View;
            if (view != null)
            {
                view.PlayHitFlash();
            }
            if (Group.IsDead && _spatial != null)
            {
                _spatial.NotifyDeath(this);
            }
        }

        public void Revive()
        {
            if (Group == null) return;
            Group.Revive();
            _cooldownTimer = Group.EffectiveAttackCooldown * 0.5f;
            _active = false;
            View?.BindReal(Group);
        }

        // Resurrects this unit mid-battle at a fraction of max HP (Revive spell). Unlike the
        // end-of-round Revive, the unit stays active so it rejoins the fight immediately, and
        // its activation/cooldown timers reset so it doesn't strike the instant it returns.
        public void ReviveInBattle(float hpFraction)
        {
            if (Group == null) return;
            Group.ReviveWithHpFraction(hpFraction);
            _cooldownTimer = Group.EffectiveAttackCooldown * 0.5f;
            _activationDelay = Random.Range(_activationDelayMin, _activationDelayMax);
            _active = true;
            View?.BindReal(Group);
        }

        private void Update()
        {
            if (_dropping)
            {
                CurrentVelocity = Vector2.zero;
                View?.SetMoving(false);
                return;
            }
            if (IsDead || _spatial == null || Group == null)
            {
                CurrentVelocity = Vector2.zero;
                View?.SetMoving(false);
                return;
            }

            Vector2 myPos = Rect.anchoredPosition;

            if (_active && _activationDelay > 0f)
            {
                _activationDelay -= Time.deltaTime;
            }

            // Run down the Fortify shield window during combat; drop the visual when it ends.
            if (_active && Group.IsShielded && !Group.TickShield(Time.deltaTime))
            {
                View?.SetShield(false);
            }

            // Run down the Rally speed-buff window during combat.
            if (_active && Group.IsRallied)
            {
                Group.TickRally(Time.deltaTime);
            }

            if (_active && Group.IsSlowed && !Group.TickSlow(Time.deltaTime))
            {
                View?.SetSlowTint(false);
            }

            // Figure out where we want to go *before* separation, so separation can soften
            // pushes against allies standing in that direction (rear units press the line
            // instead of bulldozing through it).
            Vector2 moveDir = Vector2.zero;
            // Which way (in X) the unit should face this frame: while attacking, toward the
            // target; while chasing, the velocity decides (set after separation, below). 0 = no
            // opinion this frame, keep the current facing.
            float faceDirX = 0f;
            // Recomputed below when actively targeting; cleared here so a stale "blocked" state
            // from a previous frame can't keep suppressing this unit's enemy push.
            _frontBlocked = false;
            if (_active && _activationDelay <= 0f)
            {
                BattleUnit target = _spatial.FindClosestOpponent(this);
                if (target != null)
                {
                    Vector2 targetPos = target.Rect.anchoredPosition;
                    float distance = Vector2.Distance(myPos, targetPos);

                    // When the target is itself enemy cavalry, the two skirmishers clash in the
                    // open — no front line to go around — so skip the arc and charge straight.
                    bool targetIsCavalry = target.Group != null && target.Group.UnitType == UnitType.Cavalry;

                    // Front-rank gating for melee: a rear-rank melee unit screened from the
                    // target by its own front line may NOT attack or push through it — it has to
                    // wait for the front to open. Ranged units (they shoot over heads) and cavalry
                    // (flankers) are exempt. Computed once here and reused by separation below.
                    bool isMelee = Group.ProjectileSprite == null && !IsCavalry;

                    // The branches below are MUTUALLY EXCLUSIVE (else-if chain) — exactly one sets
                    // moveDir / attacks this frame. The cavalry flank arc MUST stay the first
                    // branch and own the frame outright: if a later branch also ran, it would
                    // overwrite the arc's moveDir with a straight charge and the Wolf Rider would
                    // never swing wide. Cavalry is the special-cased unit type here.
                    if (!targetIsCavalry && _flankPhase == FlankPhase.Arcing
                        && TryGetFlankDir(myPos, targetPos, out Vector2 flankDir))
                    {
                        // Still arcing out to the edge to get around the front line — steer to
                        // the flank waypoint instead of charging straight. No attack while arcing.
                        moveDir = flankDir;
                    }
                    else if (distance > Group.AttackRange
                             || (_frontBlocked = isMelee && _spatial.IsFrontBlocked(this, target)))
                    {
                        // Out of range, or (melee) in range but screened by our own front line:
                        // keep pressing toward the target. When blocked, enemy-side separation is
                        // suppressed below so this press packs in behind the front line instead of
                        // shoving through it.
                        moveDir = (targetPos - myPos).normalized;
                        if (_frontBlocked) faceDirX = targetPos.x - myPos.x;
                    }
                    else
                    {
                        // In range and attacking: face the target so the strike never plays
                        // backwards, even when the target is behind us (e.g. a cavalry dive).
                        faceDirX = targetPos.x - myPos.x;
                        _cooldownTimer -= Time.deltaTime;
                        if (_cooldownTimer <= 0f)
                        {
                            _cooldownTimer = Group.EffectiveAttackCooldown;
                            PerformAttack(target);
                        }
                    }
                }
            }

            Vector2 velocity = ComputeSeparation(myPos, moveDir);

            if (moveDir != Vector2.zero)
            {
                velocity += moveDir * Group.EffectiveMoveSpeed;
            }

            if (!_active && _hasSettleTarget)
            {
                Vector2 toTarget = _settleTarget - myPos;
                if (toTarget.sqrMagnitude <= 4f)
                {
                    Rect.anchoredPosition = _spatial.ClampToBounds(_settleTarget, IsCavalry);
                    _hasSettleTarget = false;
                }
                else
                {
                    velocity += toTarget.normalized * _settleSpeed;
                }
            }

            // Cap the combined speed so a pile-up of separation pushes plus move force can't
            // fling a unit across the field in one frame. Sized above the fastest move speed
            // so honest chasing isn't throttled.
            if (_maxSpeed > 0f)
            {
                float speed = velocity.magnitude;
                if (speed > _maxSpeed) velocity *= _maxSpeed / speed;
            }

            // The walk animation should play only while the unit is actively travelling toward
            // its target (or arcing on a flank), NOT when it's standing in range attacking. Key
            // off moveDir, which is non-zero only when we chose to advance this frame — separation
            // jitter alone (which still nudges velocity while attacking) must not trigger the walk,
            // or units would keep bobbing/leaning mid-strike.
            bool moving = _active && moveDir != Vector2.zero;
            UpdateMoveVisualState(moving);

            // Update which way the unit faces. Attacking already chose faceDirX (toward the
            // target); otherwise, when actually travelling, the horizontal velocity decides.
            // A small deadzone stops near-vertical motion from jittering the flip. Applied
            // before the idle early-out so a unit standing and attacking still faces its target.
            if (Mathf.Abs(faceDirX) < _facingDeadzone && _active)
            {
                faceDirX = velocity.x;
            }
            if (Mathf.Abs(faceDirX) >= _facingDeadzone)
            {
                View?.SetFacing(faceDirX > 0f);
            }

            if (velocity.sqrMagnitude <= 0.001f)
            {
                CurrentVelocity = Vector2.zero;
                return;
            }

            Vector2 next = myPos + velocity * Time.deltaTime;
            CurrentVelocity = velocity;
            Rect.anchoredPosition = _spatial.ClampToBounds(next, IsCavalry);
        }

        private void UpdateMoveVisualState(bool moving)
        {
            if (moving != _pendingMoveVisualState)
            {
                _pendingMoveVisualState = moving;
                _moveVisualStateChangedAt = Time.time;
            }

            if (_moveVisualState == _pendingMoveVisualState) return;
            // Require the pending state to persist: longer to START walking, shorter to STOP.
            float requiredHold = _pendingMoveVisualState ? _moveStartDelay : _moveStopDelay;
            if (Time.time - _moveVisualStateChangedAt < requiredHold) return;

            _moveVisualState = _pendingMoveVisualState;
            View?.SetMoving(_moveVisualState);
        }

        private bool IsMovingTowardTarget()
        {
            BattleUnit target = _spatial.FindClosestOpponent(this);
            if (target == null) return false;
            float distance = Vector2.Distance(Rect.anchoredPosition, target.Rect.anchoredPosition);
            return distance > Group.AttackRange;
        }

        // Cavalry flank steering. Cavalry have their own tall RUNWAY band (CavalryBoundsMin/Max)
        // that reaches well above and below the tight infantry band — the empty top/bottom space.
        // "Going around the front" means sweeping out to that runway edge (the Y axis) and
        // crossing the contact line there, where there are no front-liners — NOT running the full
        // width of the map. The path is one continuous swing-out-then-cross curve (blended by
        // progress toward the edge) so it reads as a rounded arc, not a single 99%-horizontal
        // diagonal and not a right-angle hook:
        //
        //   leaves mostly vertical (swing out toward the edge Y),
        //   rounds the corner gradually as it nears the edge,
        //   arrives mostly horizontal (cross in X toward the target along the edge).
        //
        // The arc ends — and normal chasing/diving resumes — once we've crossed past the enemy
        // front (over to the target's side of the contact line and near its X). edge/contact are
        // latched on the first arcing frame so the path is stable.
        private bool TryGetFlankDir(Vector2 myPos, Vector2 targetPos, out Vector2 dir)
        {
            dir = Vector2.zero;
            if (_spatial == null) return false;

            // Sweep along the CAVALRY runway edge (the empty top/bottom), not the infantry band —
            // that's the whole point of the runway: the cavalry ride out into the open space the
            // line units never enter.
            Vector2 boundsMin = _spatial.CavalryBoundsMin;
            Vector2 boundsMax = _spatial.CavalryBoundsMax;
            float midY = (boundsMin.y + boundsMax.y) * 0.5f;
            float halfHeight = (boundsMax.y - boundsMin.y) * 0.5f;

            if (!_flankLatched)
            {
                bool useTop = myPos.y >= midY;
                _flankEdgeY = useTop
                    ? midY + halfHeight * _flankEdgeReach - _flankEdgeInset
                    : midY - halfHeight * _flankEdgeReach + _flankEdgeInset;
                // The contact line is the X midpoint between this unit and its target — the
                // band the front line holds. The flank is "done" once we're past it on the
                // target's side, so the unit dives in rather than running to the far wall.
                _flankDiveX = (myPos.x + targetPos.x) * 0.5f;
                // Remember how far we have to climb to the edge so the blend below can normalise
                // progress (0 at the start → 1 at the edge) into a smooth curve.
                _flankStartYGap = Mathf.Max(Mathf.Abs(_flankEdgeY - myPos.y), 1f);
                _flankLatched = true;
            }

            // Which way (in X) is the target? Enemies attack leftward (target.x < my.x),
            // players rightward. The arc is "spent" once we've crossed the contact line.
            float toTargetX = targetPos.x - myPos.x;
            bool crossedFront = Mathf.Abs(toTargetX) < 1f
                || (toTargetX < 0f ? myPos.x <= _flankDiveX : myPos.x >= _flankDiveX);
            if (crossedFront)
            {
                _flankPhase = FlankPhase.Done;
                return false;
            }

            float yGap = _flankEdgeY - myPos.y;
            float xDir = Mathf.Sign(toTargetX);
            float yDir = Mathf.Sign(yGap);

            // One continuous curve instead of two hard legs (which kinked into a right angle):
            // `reach` runs 0 at the start of the swing-out → 1 once we're at the edge band. We
            // blend the X (cross) and Y (swing-out) weights against it with a smootherstep, so the
            // unit leaves mostly vertical, rounds the corner gradually, and arrives mostly
            // horizontal — no sudden snap from "up" to "across".
            float reach = 1f - Mathf.Clamp01((Mathf.Abs(yGap) - _flankEdgeBand) / _flankStartYGap);
            float t = Mathf.SmoothStep(0f, 1f, reach);
            // X weight grows 0.18 → 1 with progress; Y weight fades 1 → 0.15 so the path keeps a
            // little vertical hold at the edge without ever going fully flat.
            float xWeight = Mathf.Lerp(0.18f, 1f, t);
            float yWeight = Mathf.Lerp(1f, 0.15f, t);
            Vector2 steer = new(xDir * xWeight, yDir * yWeight);

            if (steer.sqrMagnitude <= 0.0001f)
            {
                _flankPhase = FlankPhase.Done;
                return false;
            }
            dir = steer.normalized;
            return true;
        }

        // moveDir is the (normalized) direction this unit wants to travel this frame, or zero
        // if it isn't chasing. Pushes against an ally sitting in that direction are softened so
        // a rear unit presses into the back of the line instead of shoving it across the field.
        private Vector2 ComputeSeparation(Vector2 myPos, Vector2 moveDir)
        {
            Vector2 force = Vector2.zero;
            IEnumerable<BattleUnit> all = _spatial.GetAllUnits();
            if (all == null) return force;

            foreach (BattleUnit other in all)
            {
                if (other == null || other == this || other.IsDead) continue;
                Vector2 delta = myPos - other.Rect.anchoredPosition;
                float dist = delta.magnitude;
                bool sameSide = IsPlayerUnit == other.IsPlayerUnit;
                float minDist = sameSide ? _allySeparationDistance : _bodyRadius + other.BodyRadius;
                if (dist < minDist)
                {
                    if (dist <= 0.01f)
                    {
                        delta = PairSeparationDirection(other);
                        dist = 1f;
                    }

                    Vector2 pushDir = delta.normalized;
                    float push = (minDist - dist) / minDist;
                    float strength = _separationStrength;

                    if (sameSide)
                    {
                        // If this is an ally roughly between us and where we're headed, ease off so
                        // we don't bulldoze the formation forward. The dot is how directly the ally
                        // blocks our path (1 = dead ahead); scale the push down toward _blockedPushFactor.
                        if (moveDir != Vector2.zero)
                        {
                            float blocking = Mathf.Clamp01(Vector2.Dot(moveDir, -pushDir));
                            strength *= Mathf.Lerp(1f, _blockedPushFactor, blocking);
                        }
                    }
                    else
                    {
                        // Cross-side: soften so a deep formation can't sum its pushes and slide
                        // the enemy line off the field. They still get jostled, just not bulldozed.
                        // A rear-rank unit screened by its own front line (front-rank gating) gets
                        // NO enemy push at all — it isn't in contact, so it must not contribute to
                        // shoving the enemy through the front rank ahead of it.
                        strength *= _frontBlocked ? 0f : _enemyPushFactor;
                    }

                    force += pushDir * push * strength;
                }
            }
            return force;
        }

        private Vector2 PairSeparationDirection(BattleUnit other)
        {
            int myId = GetInstanceID();
            int otherId = other.GetInstanceID();
            int minId = Mathf.Min(myId, otherId);
            int maxId = Mathf.Max(myId, otherId);

            unchecked
            {
                int hash = (minId * 73856093) ^ (maxId * 19349663);
                float angle = (hash & 0x7fffffff) / (float)int.MaxValue * Mathf.PI * 2f;
                Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));
                return myId < otherId ? direction : -direction;
            }
        }

        private void PerformAttack(BattleUnit target)
        {
            int damage = Group.TotalDamage;
            UnitGroupView view = View;

            // Support healer (Cleric / Shaman): every Nth normal attack, heal ONE ally — the
            // most-hurt unit on this side (itself included). Counted on the swing so it fires
            // the same for melee and ranged healers, regardless of projectile travel time.
            if (Group.IsHealer)
            {
                _attackCount++;
                if (_attackCount % Group.HealEveryAttacks == 0 && _spatial != null)
                {
                    _spatial.HealAlly(this, Group.HealAmount);
                }
            }

            // Ranged units use their release animation first. Most then launch a traveling
            // projectile; Thunder Bird instead calls the battlefield lightning-strike VFX.
            if (Group.ProjectileSprite != null && transform.parent is RectTransform fieldRoot)
            {
                float throwDuration = Mathf.Min(_projectileThrowDuration, Group.EffectiveAttackCooldown * 0.9f);
                if (view != null) view.PlayThrow(throwDuration);
                if (IsThunderBirdLightningAttack() && _spatial != null
                    && _spatial.TryCastUnitLightningStrike(this, target, damage, Group.ProjectileSprite))
                {
                    return;
                }
                LaunchProjectile(fieldRoot, target, damage);
                return;
            }

            // Use a fixed strike length so the swing stays readable no matter how short the
            // cooldown is, but never let it run past the next attack (0.9 leaves a small gap).
            float attackDuration = Mathf.Min(_meleeAttackDuration, Group.EffectiveAttackCooldown * 0.9f);
            if (view != null) view.PlayAttack(attackDuration);
            target.TakeDamage(damage);
        }

        private void LaunchProjectile(RectTransform fieldRoot, BattleUnit target, int damage)
        {
            if (target == null) return;

            float viewScale = View != null ? Mathf.Abs(View.transform.localScale.y) : 1f;
            float renderedHeight = View != null ? View.SpriteRenderedHeight : 0f;
            float originLocal = renderedHeight > 0f ? renderedHeight * _projectileOriginHeight : _projectileFallbackHeight;
            float unitScale = View != null ? View.SpriteScale : 0.47f;
            float projectileScale = unitScale * _projectileSizeMultiplier * viewScale;

            // Per-unit projectile speed when authored (>0); otherwise the launcher default.
            float projectileSpeed = Group.ProjectileSpeed > 0f ? Group.ProjectileSpeed : _projectileSpeed;

            Vector2 start = Rect.anchoredPosition + new Vector2(0f, originLocal * viewScale);
            float aoeRadius = Group.ProjectileAoeRadius;
            bool lobbed = aoeRadius > 0f;
            Vector2 impact = lobbed
                ? target.Rect.anchoredPosition
                : PredictIntercept(start, target.Rect.anchoredPosition, target.CurrentVelocity, projectileSpeed);
            Projectile.Launch(fieldRoot, start, target, impact,
                damage, Group.ProjectileSprite, projectileSpeed, projectileScale,
                _spatial, IsPlayerUnit, aoeRadius, lobbed);
        }

        private bool IsThunderBirdLightningAttack()
        {
            if (Group == null || Group.ProjectileSprite == null) return false;
            string displayName = Group.DisplayName ?? string.Empty;
            return displayName.IndexOf("Thunder Bird", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Vector2 PredictIntercept(Vector2 start, Vector2 targetPosition, Vector2 targetVelocity, float projectileSpeed)
        {
            float speed = Mathf.Max(1f, projectileSpeed);
            Vector2 toTarget = targetPosition - start;

            float a = Vector2.Dot(targetVelocity, targetVelocity) - speed * speed;
            float b = 2f * Vector2.Dot(toTarget, targetVelocity);
            float c = Vector2.Dot(toTarget, toTarget);

            float t = 0f;
            if (Mathf.Abs(a) < 0.001f)
            {
                if (Mathf.Abs(b) > 0.001f) t = -c / b;
            }
            else
            {
                float discriminant = b * b - 4f * a * c;
                if (discriminant >= 0f)
                {
                    float sqrt = Mathf.Sqrt(discriminant);
                    float t1 = (-b - sqrt) / (2f * a);
                    float t2 = (-b + sqrt) / (2f * a);
                    t = SmallestPositive(t1, t2);
                }
            }

            return t > 0f ? targetPosition + targetVelocity * t : targetPosition;
        }

        private static float SmallestPositive(float a, float b)
        {
            bool aValid = a > 0f;
            bool bValid = b > 0f;
            if (aValid && bValid) return Mathf.Min(a, b);
            if (aValid) return a;
            return bValid ? b : 0f;
        }
    }

    public interface IBattleSpatial
    {
        BattleUnit FindClosestOpponent(BattleUnit self);
        // True when a living ally of `self` stands between it and `target` — i.e. `self` is a
        // rear-rank unit whose path to the target is screened by its own front line. Melee units
        // use this to gate attacking and enemy-pushing: only the front rank fights, so a deep mob
        // can't have every member strike (and shove) through the bodies ahead of it.
        bool IsFrontBlocked(BattleUnit self, BattleUnit target);
        void NotifyDeath(BattleUnit unit);
        IEnumerable<BattleUnit> GetAllUnits();
        Vector2 ClampToBounds(Vector2 position);
        // Cavalry-aware clamp: forCavalry units use the taller runway bounds so they can sweep
        // into the empty space above/below the infantry band.
        Vector2 ClampToBounds(Vector2 position, bool forCavalry);
        // Infantry movement bounds (anchored space). min = (left, bottom), max = (right, top).
        Vector2 BattleBoundsMin { get; }
        Vector2 BattleBoundsMax { get; }
        // Cavalry runway bounds — taller than the infantry band. Cavalry clamp to these and
        // their flank arc sweeps along this top/bottom edge.
        Vector2 CavalryBoundsMin { get; }
        Vector2 CavalryBoundsMax { get; }
        bool TryCastUnitLightningStrike(BattleUnit attacker, BattleUnit target, int damage, Sprite lightningSprite);
        // Heals every living unit on `healer`'s side (the healer included) for `amount` HP.
        // Kept for possible later use (e.g. a group-heal spell); not currently called.
        void HealAllies(BattleUnit healer, int amount);
        // Heals a SINGLE ally on `healer`'s side (the healer itself is eligible) for `amount`
        // HP — the most-hurt living unit by HP fraction. Used by support healers (Cleric /
        // Shaman) every Nth attack.
        void HealAlly(BattleUnit healer, int amount);
    }
}
