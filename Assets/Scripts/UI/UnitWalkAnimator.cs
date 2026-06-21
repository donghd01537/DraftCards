using UnityEngine;

namespace DraftCards.UI
{
    // Standard walk animation for EVERY unit (player and enemy). Replaces the legacy
    // MoveBounceAnimator (which is left in place but disabled by UnitGroupView).
    //
    // Style: "toy-like marching" — not a realistic run. The unit charges forward at a steady
    // pace like a little toy soldier, with the whole body bouncing rather than the feet doing
    // the work. Three layered motions, all in the sprite container's local space (the container
    // is X-flipped to face the travel direction, so local +X is always "forward"):
    //
    //   1. Bounce  — the dominant motion. A springy up/down per step: drop low on contact,
    //      pop up, surge forward. Driven at twice the sway rate so every left/right step bounces.
    //   2. Forward lean — the body tilts a small fixed amount toward the travel direction and
    //      holds it (it leans into the charge), it does not rock symmetrically about upright.
    //   3. Step sway — a small alternating left/right wobble layered on top of the lean, the
    //      only nod to alternating footfalls; kept subtle so the eye reads the whole-body bounce.
    //
    // The lean + sway rotate about the container origin, which sits at the unit's feet because
    // UnitGroupView parks each sprite's authored (feet) pivot there — so the figure rocks on its
    // feet with no manual pivot compensation needed.
    //
    // Attached and driven by UnitGroupView (ConfigureWalk); SetMoving toggles the march.
    public class UnitWalkAnimator : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        // Fixed tilt (degrees) the body holds toward the travel direction while marching.
        [SerializeField] private float _forwardLean = 5f;
        // Amplitude (degrees) of the alternating left/right step wobble around the lean.
        [SerializeField] private float _swayAngle = 9f;
        // Marching cadence in radians/second. One sway cycle (left+right) per 2π; the bounce
        // runs at twice this, so one hop per step.
        [SerializeField] private float _swaySpeed = 34f;
        // Peak hop height (local units) of the per-step bounce. Small — the wobble dominates.
        [SerializeField] private float _bobHeight = 2f;

        // Per-instance phase offset, randomized once so a line of goblins marches out of step
        // with each other (a crowd of individuals, not one block bouncing in unison).
        private float _phaseOffset;
        private float _timer;
        private Vector3 _baseLocalPosition;
        private Quaternion _baseLocalRotation;
        private bool _moving;

        // Lets UnitGroupView feed enemy-specific tuning at bind time. Keeps the per-instance
        // random spread so values still vary unit to unit.
        public void Configure(float forwardLean, float swayAngle, float swaySpeed, float bobHeight)
        {
            _forwardLean = forwardLean;
            _swayAngle = swayAngle * Random.Range(0.85f, 1.20f);
            _swaySpeed = swaySpeed * Random.Range(0.9f, 1.15f);
            _bobHeight = bobHeight * Random.Range(0.9f, 1.15f);
        }

        public void SetMoving(bool moving)
        {
            if (_target == null) _target = transform;

            if (moving && !_moving)
            {
                // Capture the rest pose at the moment movement starts, not in Awake/first
                // Update — by now the spawn drop (which moves the container) has finished, so
                // this is the true settled position. Capturing earlier would lock onto the
                // mid-drop position and leave the unit floating above the ground.
                _baseLocalPosition = _target.localPosition;
                _baseLocalRotation = _target.localRotation;
            }
            else if (!moving && _moving && _target != null)
            {
                // Settle back to the captured rest pose when stopping.
                _target.localPosition = _baseLocalPosition;
                _target.localRotation = _baseLocalRotation;
            }
            _moving = moving;
        }

        private void Awake()
        {
            if (_target == null) _target = transform;
            _phaseOffset = Random.Range(0f, Mathf.PI * 2f);
            _swayAngle *= Random.Range(0.85f, 1.20f);
            _swaySpeed *= Random.Range(0.9f, 1.15f);
            _bobHeight *= Random.Range(0.9f, 1.15f);
        }

        private void Update()
        {
            if (_target == null || !_moving) return;

            _timer += Time.deltaTime;
            float phase = _phaseOffset + _timer * _swaySpeed;

            // Rotation: a fixed forward lean plus a small alternating step wobble. Facing is done
            // by flipping the container's X scale (UnitGroupView.SetFacing): a negative scale.x
            // MIRRORS the Z rotation on screen, so the same +angle would tilt left-facing units
            // the opposite way from right-facing ones. Multiply by sign(scale.x) so the lean
            // always reads as "into the charge" for both sides.
            float facingSign = _target.localScale.x < 0f ? -1f : 1f;
            float angle = (_forwardLean + Mathf.Sin(phase) * _swayAngle) * facingSign;

            // Bounce: the dominant motion, one springy hop per step (twice the sway rate). A
            // skewed curve gives the toy "drop on contact, pop up" feel — it sits low most of
            // the cycle and snaps up near the top rather than easing symmetrically. Using a
            // raised, sharpened sine: 0 at contact, 1 at the peak of each hop.
            float hop = Mathf.Abs(Mathf.Sin(phase));   // 0..1, two hops per sway cycle
            // Raised to the 5th power: the unit stays low most of the step, then snaps up
            // hard and fast — a very decisive, punchy stomp rather than a soft float.
            float hop2 = hop * hop;
            float bob = hop2 * hop2 * hop * _bobHeight;

            // The container origin already sits at the feet (UnitGroupView parks the sprite's
            // feet pivot there), so rotating the container rotates about the feet directly — no
            // pivot-compensation offset needed. Just add the vertical bounce.
            _target.localPosition = _baseLocalPosition + new Vector3(0f, bob, 0f);
            _target.localRotation = _baseLocalRotation * Quaternion.Euler(0f, 0f, angle);
        }
    }
}
