using System.Collections;
using System.Collections.Generic;
using DraftCards.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace DraftCards.UI
{
    // A thrown or fired projectile. Spawned as a sibling of the battle units so it
    // shares their anchored-coordinate space. Lobbed projectiles arc and spin; direct
    // shots fly point-first and damage only their selected target.
    public class Projectile : MonoBehaviour
    {
        private const float SpinDegreesPerSecond = 540f;
        private const float ArcHeight = 90f;
        private const float ArcDistanceFactor = 0.45f;
        private const float ArcRampDistance = 220f;
        private const float MaxLifetimeSeconds = 2f;
        private const float DirectHitRadius = 26f;

        public static void Launch(RectTransform parent, Vector2 start, BattleUnit primaryTarget,
            Vector2 impact, int damage, Sprite sprite, float flightSpeed, float scale,
            IBattleSpatial spatial, bool attackerIsPlayer, float aoeRadius, bool lobbed)
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
            projectile.StartCoroutine(projectile.Fly(rect, start, primaryTarget, impact,
                damage, flightSpeed, scale, spatial, attackerIsPlayer, aoeRadius, lobbed));
            Destroy(go, MaxLifetimeSeconds);
        }

        private Image _image;

        private IEnumerator Fly(RectTransform rect, Vector2 start, BattleUnit target,
            Vector2 impact, int damage, float flightSpeed, float scale,
            IBattleSpatial spatial, bool attackerIsPlayer, float aoeRadius, bool lobbed)
        {
            float flightSpeedSafe = Mathf.Max(1f, flightSpeed);
            float totalFlat = Mathf.Max(1f, Vector2.Distance(start, impact));

            float arcHeight = 0f;
            if (lobbed)
            {
                float lobScale = Mathf.Clamp01(totalFlat / ArcRampDistance);
                arcHeight = ArcHeight * Mathf.Max(0.01f, scale) * lobScale + totalFlat * ArcDistanceFactor;
            }

            float traveled = 0f;
            float spin = 0f;
            float u = 0f;
            Vector2 previous = start;
            while (u < 1f)
            {
                traveled += flightSpeedSafe * Time.deltaTime;
                u = Mathf.Clamp01(traveled / totalFlat);

                if (lobbed && target != null && !target.IsDead)
                {
                    impact = target.Rect.anchoredPosition;
                }

                Vector2 flat = Vector2.Lerp(start, impact, u);
                float arc = arcHeight * 4f * u * (1f - u);
                Vector2 next = flat + new Vector2(0f, arc);
                rect.anchoredPosition = next;

                if (lobbed)
                {
                    spin += SpinDegreesPerSecond * Time.deltaTime;
                    rect.localRotation = Quaternion.Euler(0f, 0f, -spin);
                }
                else
                {
                    Vector2 travel = next - previous;
                    if (travel.sqrMagnitude > 0.001f)
                    {
                        float angle = Mathf.Atan2(travel.y, travel.x) * Mathf.Rad2Deg;
                        rect.localRotation = Quaternion.Euler(0f, 0f, angle);
                    }
                }

                previous = next;
                yield return null;
            }

            rect.anchoredPosition = impact;
            ApplyDamage(spatial, target, impact, attackerIsPlayer, aoeRadius, damage, lobbed);
            if (_image != null) _image.enabled = false;
            Destroy(gameObject);
        }

        private static void ApplyDamage(IBattleSpatial spatial, BattleUnit primaryTarget,
            Vector2 impact, bool attackerIsPlayer, float aoeRadius, int damage, bool lobbed)
        {
            if (!lobbed)
            {
                if (primaryTarget != null
                    && !primaryTarget.IsDead
                    && (primaryTarget.Rect.anchoredPosition - impact).sqrMagnitude <= DirectHitRadius * DirectHitRadius)
                {
                    primaryTarget.TakeDamage(damage);
                }
                return;
            }

            if (spatial == null)
            {
                if (primaryTarget != null && !primaryTarget.IsDead) primaryTarget.TakeDamage(damage);
                return;
            }

            float radiusSqr = aoeRadius * aoeRadius;
            IEnumerable<BattleUnit> all = spatial.GetAllUnits();
            if (all == null) return;

            List<BattleUnit> targets = new();
            foreach (BattleUnit unit in all)
            {
                if (unit == null || unit.IsDead) continue;
                if (unit.IsPlayerUnit == attackerIsPlayer) continue;
                if ((unit.Rect.anchoredPosition - impact).sqrMagnitude <= radiusSqr)
                {
                    targets.Add(unit);
                }
            }

            foreach (BattleUnit unit in targets)
            {
                if (unit != null && !unit.IsDead) unit.TakeDamage(damage);
            }
        }
    }
}
