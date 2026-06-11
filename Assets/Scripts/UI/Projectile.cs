using System.Collections;
using System.Collections.Generic;
using DraftCards.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace DraftCards.UI
{
    // A thrown projectile (e.g. the Cyclop's rock). Spawned as a sibling of the
    // battle units so it shares their anchored-coordinate space. It arcs from the
    // attacker to the target, then on impact deals area-of-effect damage to every
    // opposing unit within its blast radius.
    public class Projectile : MonoBehaviour
    {
        private const float SpinDegreesPerSecond = 540f;
        // Base lob height, scaled by the thrower's view scale. The rock is thrown on a
        // high arc, so this is added to a distance-proportional component (see Fly) to
        // give longer throws a steeper, more dramatic trajectory.
        private const float ArcHeight = 90f;
        // Fraction of the horizontal throw distance added to the arc height, so the rock
        // climbs higher the farther it has to travel.
        private const float ArcDistanceFactor = 0.45f;
        // Below this throw distance the lob flattens out: at distance 0 the arc is ~0 (a
        // near-flat toss) and it ramps up to the full base lob by this distance. Keeps a
        // point-blank throw from launching the rock high into the air only to drop it right
        // beside the thrower.
        private const float ArcRampDistance = 220f;
        // Absolute backstop: a rock is destroyed this many seconds after launch no matter
        // what, so a stalled or orphaned projectile (e.g. its parent was torn down mid-air)
        // can never linger on the battlefield.
        private const float MaxLifetimeSeconds = 2f;

        public static void Launch(RectTransform parent, Vector2 start, BattleUnit primaryTarget,
            Vector2 fallbackImpact, int damage, Sprite sprite, float flightSpeed, float scale,
            IBattleSpatial spatial, bool attackerIsPlayer, float aoeRadius)
        {
            if (parent == null || sprite == null) return;

            GameObject go = new("Projectile", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = start;

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.SetNativeSize();
            float s = Mathf.Max(0.01f, scale);
            rect.localScale = new Vector3(s, s, 1f);

            Projectile projectile = go.AddComponent<Projectile>();
            projectile._image = image;
            projectile.StartCoroutine(projectile.Fly(rect, start, primaryTarget, fallbackImpact,
                damage, flightSpeed, scale, spatial, attackerIsPlayer, aoeRadius));
            Destroy(go, MaxLifetimeSeconds);
        }

        private Image _image;

        private IEnumerator Fly(RectTransform rect, Vector2 start, BattleUnit target,
            Vector2 fallbackImpact, int damage, float flightSpeed, float scale,
            IBattleSpatial spatial, bool attackerIsPlayer, float aoeRadius)
        {
            Vector2 impact = target != null && !target.IsDead ? target.Rect.anchoredPosition : fallbackImpact;
            float flightSpeedSafe = Mathf.Max(1f, flightSpeed);
            float totalFlat = Mathf.Max(1f, Vector2.Distance(start, impact));
            // Scale the base lob down for short throws so a close target gets a near-flat
            // toss instead of a tall arc landing right next to the thrower.
            float lobScale = Mathf.Clamp01(totalFlat / ArcRampDistance);
            float arcHeight = ArcHeight * Mathf.Max(0.01f, scale) * lobScale + totalFlat * ArcDistanceFactor;

            // The rock travels along the ground at a FIXED speed (px/sec) — near and far
            // targets fly at the same visible speed, the trip just takes longer when farther.
            // Progress u is the fraction of the launch->impact distance covered so far; it
            // drives both the horizontal lerp and the parabolic arc. Homing shifts the impact
            // point but never the travel speed.
            float traveled = 0f;
            float spin = 0f;
            float u = 0f;
            while (u < 1f)
            {
                traveled += flightSpeedSafe * Time.deltaTime;
                u = Mathf.Clamp01(traveled / totalFlat);

                // Light homing: keep aiming at the live target while it lives.
                if (target != null && !target.IsDead)
                {
                    impact = target.Rect.anchoredPosition;
                }

                Vector2 flat = Vector2.Lerp(start, impact, u);
                float arc = arcHeight * 4f * u * (1f - u); // parabola peaking mid-flight
                rect.anchoredPosition = flat + new Vector2(0f, arc);

                spin += SpinDegreesPerSecond * Time.deltaTime;
                rect.localRotation = Quaternion.Euler(0f, 0f, -spin);
                yield return null;
            }

            // Arrived: snap to the impact point, deal AOE damage, and destroy on the spot.
            rect.anchoredPosition = impact;
            ApplyAreaDamage(spatial, target, impact, attackerIsPlayer, aoeRadius, damage);
            // The rock must vanish the instant it reaches the target — hide its sprite
            // immediately so it never lingers on top of the units it just hit.
            if (_image != null) _image.enabled = false;
            Destroy(gameObject);
        }

        // Damages every opposing, living unit whose center is within the blast radius
        // of the impact point. Falls back to the primary target if no spatial is wired.
        private static void ApplyAreaDamage(IBattleSpatial spatial, BattleUnit primaryTarget,
            Vector2 impact, bool attackerIsPlayer, float aoeRadius, int damage)
        {
            if (spatial == null)
            {
                if (primaryTarget != null && !primaryTarget.IsDead) primaryTarget.TakeDamage(damage);
                return;
            }

            float radiusSqr = aoeRadius * aoeRadius;
            IEnumerable<BattleUnit> all = spatial.GetAllUnits();
            if (all == null) return;

            foreach (BattleUnit unit in all)
            {
                if (unit == null || unit.IsDead) continue;
                if (unit.IsPlayerUnit == attackerIsPlayer) continue; // only hit opponents
                if ((unit.Rect.anchoredPosition - impact).sqrMagnitude <= radiusSqr)
                {
                    unit.TakeDamage(damage);
                }
            }
        }

    }
}
