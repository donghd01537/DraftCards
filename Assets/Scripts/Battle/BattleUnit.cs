using System.Collections;
using System.Collections.Generic;
using DraftCards.UI;
using DraftCards.Units;
using UnityEngine;

namespace DraftCards.Battle
{
    [RequireComponent(typeof(RectTransform))]
    public class BattleUnit : MonoBehaviour
    {
        [SerializeField] private float _bodyRadius = 25f;
        [SerializeField] private float _allySeparationDistance = 67f;
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
        [SerializeField, Range(0f, 1f)] private float _blockedPushFactor = 0.35f;
        [SerializeField] private float _dropHeight = 150f;
        [SerializeField] private float _dropDuration = 0.35f;
        [SerializeField] private float _dropScatterRadius = 12f;
        [SerializeField] private float _postDropSeparationDelay = 0.15f;
        [SerializeField] private float _moveVisualStateDelay = 0.08f;
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

            if (bounce != null) bounce.enabled = true;
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
            if (_active && _activationDelay <= 0f)
            {
                BattleUnit target = _spatial.FindClosestOpponent(this);
                if (target != null)
                {
                    Vector2 targetPos = target.Rect.anchoredPosition;
                    float distance = Vector2.Distance(myPos, targetPos);

                    if (distance > Group.AttackRange)
                    {
                        moveDir = (targetPos - myPos).normalized;
                    }
                    else
                    {
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
                    Rect.anchoredPosition = _spatial.ClampToBounds(_settleTarget);
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

            bool moving = _active && velocity.sqrMagnitude > 0.001f && IsMovingTowardTarget();
            UpdateMoveVisualState(moving);
            if (velocity.sqrMagnitude <= 0.001f)
            {
                CurrentVelocity = Vector2.zero;
                return;
            }

            Vector2 next = myPos + velocity * Time.deltaTime;
            CurrentVelocity = velocity;
            Rect.anchoredPosition = _spatial.ClampToBounds(next);
        }

        private void UpdateMoveVisualState(bool moving)
        {
            if (moving != _pendingMoveVisualState)
            {
                _pendingMoveVisualState = moving;
                _moveVisualStateChangedAt = Time.time;
            }

            if (_moveVisualState == _pendingMoveVisualState) return;
            if (Time.time - _moveVisualStateChangedAt < _moveVisualStateDelay) return;

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

                    // If this is an ally roughly between us and where we're headed, ease off so
                    // we don't bulldoze the formation forward. The dot is how directly the ally
                    // blocks our path (1 = dead ahead); scale the push down toward _blockedPushFactor.
                    if (sameSide && moveDir != Vector2.zero)
                    {
                        float blocking = Mathf.Clamp01(Vector2.Dot(moveDir, -pushDir));
                        strength *= Mathf.Lerp(1f, _blockedPushFactor, blocking);
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

            // Ranged throwers (e.g. Cyclop): show the release frame (frame 2, empty hands)
            // and hurl the rock at that instant, then the animator snaps back to the idle
            // frame (frame 1, rock overhead). The rock deals AOE damage on impact.
            if (Group.ProjectileSprite != null && transform.parent is RectTransform fieldRoot)
            {
                float throwDuration = Mathf.Min(_projectileThrowDuration, Group.EffectiveAttackCooldown * 0.9f);
                if (view != null) view.PlayThrow(throwDuration);
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
        void NotifyDeath(BattleUnit unit);
        IEnumerable<BattleUnit> GetAllUnits();
        Vector2 ClampToBounds(Vector2 position);
    }
}
