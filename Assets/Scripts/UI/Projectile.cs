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
            bool playGroundImpactVfx = ShouldPlayGroundImpactVfx(sprite, lobbed);
            projectile.StartCoroutine(projectile.Fly(rect, start, primaryTarget, impact,
                damage, flightSpeed, scale, spatial, attackerIsPlayer, aoeRadius, lobbed, playGroundImpactVfx));
            Destroy(go, MaxLifetimeSeconds);
        }

        private Image _image;

        private IEnumerator Fly(RectTransform rect, Vector2 start, BattleUnit target,
            Vector2 impact, int damage, float flightSpeed, float scale,
            IBattleSpatial spatial, bool attackerIsPlayer, float aoeRadius, bool lobbed, bool playGroundImpactVfx)
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
            if (playGroundImpactVfx && rect.parent is RectTransform parent)
            {
                GroundCrackVfx.Play(parent, GroundImpactPosition(target, impact), aoeRadius);
            }
            ApplyDamage(spatial, target, impact, attackerIsPlayer, aoeRadius, damage, lobbed);
            if (_image != null) _image.enabled = false;
            Destroy(gameObject);
        }

        private static bool ShouldPlayGroundImpactVfx(Sprite sprite, bool lobbed)
        {
            return lobbed && sprite != null && sprite.name.ToLowerInvariant().Contains("rock");
        }

        private static Vector2 GroundImpactPosition(BattleUnit target, Vector2 fallback)
        {
            UnitGroupView view = target != null ? target.View : null;
            if (target == null || view == null || view.SpriteRenderedHeight <= 0f) return fallback;

            float viewScale = Mathf.Abs(view.transform.localScale.y);
            return target.Rect.anchoredPosition - new Vector2(0f, view.SpriteRenderedHeight * 0.45f * viewScale);
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

    internal sealed class GroundCrackVfx : MonoBehaviour
    {
        private const float Duration = 0.72f;
        private const int DustCount = 10;

        private Image _cracks;
        private Image _ring;
        private Image _flash;
        private DustPuff[] _dust;
        private float _size;

        public static void Play(RectTransform parent, Vector2 anchoredPos, float aoeRadius)
        {
            if (parent == null) return;

            GameObject go = new("GroundCrackVFX", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;

            GroundCrackVfx fx = go.AddComponent<GroundCrackVfx>();
            fx._size = Mathf.Clamp(aoeRadius > 0f ? aoeRadius * 1.65f : 190f, 170f, 300f);
            rect.sizeDelta = new Vector2(fx._size, fx._size * 0.56f);
            fx.Build();
            fx.StartCoroutine(fx.Animate());
        }

        private void Build()
        {
            _flash = SpawnImage("ImpactFlash", GetDustSprite(), Vector2.zero,
                new Vector2(_size * 0.42f, _size * 0.22f), new Color(1f, 0.77f, 0.34f, 0.62f));
            _ring = SpawnImage("ShockRing", GetRingSprite(), Vector2.zero,
                new Vector2(_size * 0.82f, _size * 0.32f), new Color(0.45f, 0.28f, 0.14f, 0.72f));
            _cracks = SpawnImage("Cracks", GetCrackSprite(), Vector2.zero,
                new Vector2(_size, _size * 0.50f), new Color(0.18f, 0.10f, 0.055f, 0.95f));

            _dust = new DustPuff[DustCount];
            for (int i = 0; i < _dust.Length; i++)
            {
                float t = i / (float)_dust.Length;
                float angle = Mathf.Lerp(198f, 342f, t) * Mathf.Deg2Rad;
                float jitter = Noise01(i, 17f) * 18f - 9f;
                float distance = Mathf.Lerp(_size * 0.16f, _size * 0.42f, Noise01(i, 31f));
                Vector2 dir = new(Mathf.Cos(angle), Mathf.Sin(angle) * 0.42f);
                Vector2 start = dir * (_size * 0.08f);
                Vector2 end = dir * distance + new Vector2(jitter, Mathf.Lerp(10f, 36f, Noise01(i, 43f)));
                float puffSize = Mathf.Lerp(_size * 0.09f, _size * 0.15f, Noise01(i, 59f));
                Image image = SpawnImage($"Dust_{i}", GetDustSprite(), start,
                    new Vector2(puffSize, puffSize * 0.72f), new Color(0.54f, 0.38f, 0.23f, 0.68f));
                _dust[i] = new DustPuff
                {
                    Rect = (RectTransform)image.transform,
                    Image = image,
                    Start = start,
                    End = end,
                    Delay = Mathf.Lerp(0f, 0.11f, Noise01(i, 71f)),
                    Spin = Mathf.Lerp(-26f, 26f, Noise01(i, 83f))
                };
            }
        }

        private IEnumerator Animate()
        {
            RectTransform rect = (RectTransform)transform;
            float elapsed = 0f;
            while (elapsed < Duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / Duration);
                float outFade = 1f - Smooth01(Mathf.InverseLerp(0.45f, 1f, u));

                if (_flash != null)
                {
                    float flash = 1f - Smooth01(Mathf.InverseLerp(0f, 0.24f, u));
                    _flash.transform.localScale = new Vector3(Mathf.Lerp(0.45f, 1.7f, Smooth01(u)), Mathf.Lerp(0.6f, 1.25f, u), 1f);
                    SetAlpha(_flash, 0.62f * flash);
                }

                if (_ring != null)
                {
                    _ring.transform.localScale = new Vector3(Mathf.Lerp(0.34f, 1.34f, Smooth01(u)), Mathf.Lerp(0.55f, 1.12f, u), 1f);
                    SetAlpha(_ring, 0.72f * (1f - Smooth01(u)));
                }

                if (_cracks != null)
                {
                    float crackIn = Smooth01(Mathf.InverseLerp(0f, 0.14f, u));
                    _cracks.transform.localScale = new Vector3(Mathf.Lerp(0.72f, 1f, crackIn), Mathf.Lerp(0.78f, 1f, crackIn), 1f);
                    SetAlpha(_cracks, 0.95f * crackIn * outFade);
                }

                for (int i = 0; i < _dust.Length; i++)
                {
                    DustPuff puff = _dust[i];
                    float du = Mathf.Clamp01((elapsed - puff.Delay) / 0.52f);
                    float rise = Smooth01(du);
                    puff.Rect.anchoredPosition = Vector2.Lerp(puff.Start, puff.End, rise);
                    puff.Rect.localRotation = Quaternion.Euler(0f, 0f, puff.Spin * du);
                    puff.Rect.localScale = Vector3.one * Mathf.Lerp(0.42f, 1.18f, Mathf.Sin(du * Mathf.PI));
                    SetAlpha(puff.Image, Mathf.Sin(du * Mathf.PI) * 0.68f * outFade);
                }

                rect.localScale = Vector3.one * Mathf.Lerp(0.96f, 1.02f, Mathf.Sin(Mathf.Clamp01(u * 2f) * Mathf.PI));
                yield return null;
            }

            Destroy(gameObject);
        }

        private Image SpawnImage(string name, Sprite sprite, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            GameObject go = new(name, typeof(RectTransform));
            RectTransform child = go.GetComponent<RectTransform>();
            child.SetParent(transform, false);
            child.anchorMin = child.anchorMax = new Vector2(0.5f, 0.5f);
            child.pivot = new Vector2(0.5f, 0.5f);
            child.anchoredPosition = anchoredPosition;
            child.sizeDelta = size;

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            image.preserveAspect = false;
            return image;
        }

        private static void SetAlpha(Image image, float alpha)
        {
            if (image == null) return;
            Color c = image.color;
            c.a = Mathf.Clamp01(alpha);
            image.color = c;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Noise01(int index, float salt)
        {
            float v = Mathf.Sin(index * 12.9898f + salt * 78.233f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        private static Sprite _crackSprite;
        private static Sprite GetCrackSprite()
        {
            if (_crackSprite != null) return _crackSprite;

            const int width = 256;
            const int height = 128;
            Color[] pixels = NewClear(width, height);
            Vector2 c = new(width * 0.5f, height * 0.52f);

            DrawCrack(pixels, width, height, c, new Vector2(24f, 58f), 4.8f);
            DrawCrack(pixels, width, height, c + new Vector2(-10f, -2f), new Vector2(70f, 34f), 3.8f);
            DrawCrack(pixels, width, height, c + new Vector2(6f, 3f), new Vector2(196f, 40f), 4.2f);
            DrawCrack(pixels, width, height, c + new Vector2(2f, -1f), new Vector2(232f, 70f), 3.4f);
            DrawCrack(pixels, width, height, c + new Vector2(-3f, 5f), new Vector2(122f, 106f), 3.0f);
            DrawCrack(pixels, width, height, new Vector2(76f, 44f), new Vector2(48f, 24f), 2.4f);
            DrawCrack(pixels, width, height, new Vector2(186f, 43f), new Vector2(210f, 20f), 2.6f);
            DrawCrack(pixels, width, height, new Vector2(193f, 70f), new Vector2(218f, 92f), 2.2f);

            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(pixels);
            tex.Apply();
            _crackSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return _crackSprite;
        }

        private static Sprite _ringSprite;
        private static Sprite GetRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;

            const int width = 256;
            const int height = 96;
            Color[] pixels = NewClear(width, height);
            Vector2 center = new(width * 0.5f, height * 0.5f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float nx = (x + 0.5f - center.x) / (width * 0.5f);
                    float ny = (y + 0.5f - center.y) / (height * 0.5f);
                    float d = Mathf.Sqrt(nx * nx + ny * ny);
                    float ring = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.78f) / 0.06f);
                    float dust = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.62f) / 0.18f) * 0.28f;
                    pixels[y * width + x] = new Color(1f, 1f, 1f, d <= 1f ? Mathf.Clamp01(ring + dust) : 0f);
                }
            }

            Texture2D tex = new(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(pixels);
            tex.Apply();
            _ringSprite = Sprite.Create(tex, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), 100f);
            return _ringSprite;
        }

        private static Sprite _dustSprite;
        private static Sprite GetDustSprite()
        {
            if (_dustSprite != null) return _dustSprite;

            const int res = 64;
            Color[] pixels = NewClear(res, res);
            Vector2 center = new(res * 0.5f, res * 0.5f);
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / (res * 0.5f);
                    float alpha = Mathf.SmoothStep(1f, 0f, d);
                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha * alpha);
                }
            }

            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(pixels);
            tex.Apply();
            _dustSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _dustSprite;
        }

        private static Color[] NewClear(int width, int height)
        {
            Color[] pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.clear;
            return pixels;
        }

        private static void DrawCrack(Color[] pixels, int width, int height, Vector2 a, Vector2 b, float thickness)
        {
            Vector2 mid = (a + b) * 0.5f + new Vector2((b.y - a.y) * 0.08f, (a.x - b.x) * 0.04f);
            DrawSeg(pixels, width, height, a, mid, thickness);
            DrawSeg(pixels, width, height, mid, b, thickness * 0.72f);
        }

        private static void DrawSeg(Color[] pixels, int width, int height, Vector2 a, Vector2 b, float radius)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b));
            int r = Mathf.CeilToInt(radius);
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / Mathf.Max(1f, steps));
                for (int oy = -r; oy <= r; oy++)
                {
                    for (int ox = -r; ox <= r; ox++)
                    {
                        int gx = Mathf.RoundToInt(p.x) + ox;
                        int gy = Mathf.RoundToInt(p.y) + oy;
                        if (gx < 0 || gx >= width || gy < 0 || gy >= height) continue;
                        float d = Mathf.Sqrt(ox * ox + oy * oy);
                        if (d > radius) continue;
                        float alpha = Mathf.SmoothStep(1f, 0f, d / radius);
                        int idx = gy * width + gx;
                        if (alpha > pixels[idx].a) pixels[idx] = new Color(1f, 1f, 1f, alpha);
                    }
                }
            }
        }

        private struct DustPuff
        {
            public RectTransform Rect;
            public Image Image;
            public Vector2 Start;
            public Vector2 End;
            public float Delay;
            public float Spin;
        }
    }
}
