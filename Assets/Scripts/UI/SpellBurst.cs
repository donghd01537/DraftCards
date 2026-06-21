using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DraftCards.UI
{
    // Text-free spell feedback. Replaces the old floating "SHIELD" / "+50% ATK" / "ZAP 40"
    // labels with a procedural icon burst: an expanding glow ring plus a recognizable shape
    // (shield, up-arrow, down-arrow, crosshair, snowflake, plus, bolt, swirl), so the player
    // reads *which* spell fired by silhouette and color instead of by reading words.
    //
    // Visual-only (see Docs/BattleField.md "Battlefield VFX conventions"): parents under the
    // battlefield root, disables raycasts, and destroys itself after a short pop. All sprites
    // are generated in code and cached, so no authored art is required.
    public class SpellBurst : MonoBehaviour
    {
        public enum Icon
        {
            Shield,     // shields / barrier / fortify
            ArrowUp,    // attack / speed / offensive buffs
            ArrowDown,  // damage reduction / debuff on self side
            Crosshair,  // mark / target
            Snowflake,  // slow
            Plus,       // summon / reinforcement / revive
            Bolt,       // lightning storm
            Swirl       // duplicate / generic arcane
        }

        public static SpellBurst Play(RectTransform parent, Vector2 anchoredPos, Icon icon, Color color, float size = 130f)
        {
            if (parent == null) return null;

            GameObject go = new("SpellBurst", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPos;
            rect.sizeDelta = new Vector2(size, size);

            SpellBurst burst = go.AddComponent<SpellBurst>();
            burst._icon = icon;
            burst._color = color;
            burst._size = size;
            return burst;
        }

        private Icon _icon;
        private Color _color;
        private float _size;

        private void Start()
        {
            StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            // Expanding glow ring behind the icon.
            Image ring = MakeChild("Ring", GetRingSprite(), _size, _color);
            // The icon silhouette: bright core that pops in then fades.
            Image glyph = MakeChild("Glyph", GetIconSprite(_icon), _size * 0.62f, _color);

            const float duration = 0.6f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);

                // Ring: expands outward and fades.
                float ringScale = Mathf.Lerp(0.4f, 1.6f, EaseOut(u));
                ring.transform.localScale = new Vector3(ringScale, ringScale, 1f);
                SetAlpha(ring, Mathf.Lerp(0.85f, 0f, u));

                // Glyph: overshoot pop in the first third, hold, then fade and rise slightly.
                float pop = u < 0.3f
                    ? Mathf.Lerp(0.3f, 1.15f, EaseOut(u / 0.3f))
                    : Mathf.Lerp(1.15f, 1f, (u - 0.3f) / 0.7f);
                glyph.transform.localScale = new Vector3(pop, pop, 1f);
                RectTransform gr = (RectTransform)glyph.transform;
                gr.anchoredPosition = new Vector2(0f, Mathf.Lerp(0f, 22f, u * u));
                SetAlpha(glyph, u < 0.6f ? 1f : Mathf.Lerp(1f, 0f, (u - 0.6f) / 0.4f));

                yield return null;
            }

            Destroy(gameObject);
        }

        private Image MakeChild(string name, Sprite sprite, float size, Color color)
        {
            GameObject go = new(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(size, size);

            Image image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            image.preserveAspect = true;
            image.color = color;
            return image;
        }

        private static void SetAlpha(Image image, float a)
        {
            if (image == null) return;
            Color c = image.color;
            c.a = Mathf.Clamp01(a);
            image.color = c;
        }

        private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

        // ---- Procedural sprites (cached) -------------------------------------------------

        private static Sprite _ringSprite;
        private static Sprite GetRingSprite()
        {
            if (_ringSprite != null) return _ringSprite;
            const int res = 128;
            Color[] px = NewClear(res);
            Vector2 c = new(res * 0.5f, res * 0.5f);
            for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / (res * 0.5f);
                float ring = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.62f) / 0.12f);
                float inner = Mathf.SmoothStep(1f, 0f, d / 0.30f) * 0.35f;
                px[y * res + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(ring + inner));
            }
            _ringSprite = Bake(px, res);
            return _ringSprite;
        }

        private static Sprite _shield, _arrowUp, _arrowDown, _crosshair, _snowflake, _plus, _bolt, _swirl;

        private static Sprite GetIconSprite(Icon icon)
        {
            switch (icon)
            {
                case Icon.Shield: return _shield != null ? _shield : (_shield = MakeShield());
                case Icon.ArrowUp: return _arrowUp != null ? _arrowUp : (_arrowUp = MakeArrow(true));
                case Icon.ArrowDown: return _arrowDown != null ? _arrowDown : (_arrowDown = MakeArrow(false));
                case Icon.Crosshair: return _crosshair != null ? _crosshair : (_crosshair = MakeCrosshair());
                case Icon.Snowflake: return _snowflake != null ? _snowflake : (_snowflake = MakeSnowflake());
                case Icon.Plus: return _plus != null ? _plus : (_plus = MakePlus());
                case Icon.Bolt: return _bolt != null ? _bolt : (_bolt = MakeBolt());
                default: return _swirl != null ? _swirl : (_swirl = MakeSwirl());
            }
        }

        private const int Res = 96;

        private static Sprite MakeShield()
        {
            Color[] px = NewClear(Res);
            float cx = Res * 0.5f;
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                float nx = (x + 0.5f - cx) / (Res * 0.42f);
                float ny = (y + 0.5f) / Res; // 0 bottom .. 1 top
                // Shield outline: rounded top, tapered point at the bottom.
                float halfWidth = ny > 0.32f ? Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((ny - 0.78f) / 0.42f, 2f)))
                                             : Mathf.Lerp(0f, 0.86f, ny / 0.32f);
                float edge = halfWidth - Mathf.Abs(nx);
                if (edge <= 0f) continue;
                float fill = Mathf.SmoothStep(0f, 1f, edge / 0.18f);
                // Brighter rim.
                float rim = Mathf.SmoothStep(1f, 0f, edge / 0.16f) * 0.6f;
                px[y * Res + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(fill * 0.85f + rim));
            }
            return Bake(px, Res);
        }

        private static Sprite MakeArrow(bool up)
        {
            Color[] px = NewClear(Res);
            float cx = Res * 0.5f;
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                float ny = up ? (y + 0.5f) / Res : 1f - (y + 0.5f) / Res; // tip at high ny
                float nx = Mathf.Abs(x + 0.5f - cx) / (Res * 0.5f);
                float a = 0f;
                // Head: triangle in the top 45%.
                if (ny > 0.55f)
                {
                    float headHalf = (1f - ny) / 0.45f; // shrinks to the tip
                    if (nx < headHalf) a = 1f;
                }
                // Shaft: vertical bar in the lower portion.
                if (ny < 0.62f && nx < 0.18f) a = 1f;
                if (a > 0f) px[y * Res + x] = new Color(1f, 1f, 1f, a);
            }
            return Bake(px, Res);
        }

        private static Sprite MakeCrosshair()
        {
            Color[] px = NewClear(Res);
            Vector2 c = new(Res * 0.5f, Res * 0.5f);
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) / (Res * 0.5f);
                float ring = Mathf.SmoothStep(1f, 0f, Mathf.Abs(d - 0.62f) / 0.10f);
                // Cross ticks.
                float dx = Mathf.Abs(x + 0.5f - c.x);
                float dy = Mathf.Abs(y + 0.5f - c.y);
                float tick = 0f;
                if (dy < 3f && dx < Res * 0.5f) tick = 1f;
                if (dx < 3f && dy < Res * 0.5f) tick = 1f;
                float dot = Mathf.SmoothStep(1f, 0f, d / 0.10f);
                px[y * Res + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(ring + tick + dot));
            }
            return Bake(px, Res);
        }

        private static Sprite MakeSnowflake()
        {
            Color[] px = NewClear(Res);
            Vector2 c = new(Res * 0.5f, Res * 0.5f);
            for (int arm = 0; arm < 6; arm++)
            {
                float ang = arm * 60f * Mathf.Deg2Rad;
                Vector2 dir = new(Mathf.Cos(ang), Mathf.Sin(ang));
                Vector2 tip = c + dir * (Res * 0.46f);
                DrawSeg(px, Res, c, tip, 2.4f);
                // Two small branches near the tip.
                Vector2 b = c + dir * (Res * 0.30f);
                Vector2 perp = new(-dir.y, dir.x);
                DrawSeg(px, Res, b, b + (dir + perp).normalized * (Res * 0.12f), 1.8f);
                DrawSeg(px, Res, b, b + (dir - perp).normalized * (Res * 0.12f), 1.8f);
            }
            return Bake(px, Res);
        }

        private static Sprite MakePlus()
        {
            Color[] px = NewClear(Res);
            float c = Res * 0.5f;
            float arm = Res * 0.40f;
            float thick = Res * 0.15f;
            for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - c);
                float dy = Mathf.Abs(y + 0.5f - c);
                bool h = dy < thick && dx < arm;
                bool v = dx < thick && dy < arm;
                if (h || v) px[y * Res + x] = new Color(1f, 1f, 1f, 1f);
            }
            return Bake(px, Res);
        }

        private static Sprite MakeBolt()
        {
            Color[] px = NewClear(Res);
            Vector2[] pts =
            {
                new(Res * 0.58f, Res * 0.96f),
                new(Res * 0.34f, Res * 0.52f),
                new(Res * 0.52f, Res * 0.52f),
                new(Res * 0.40f, Res * 0.04f),
            };
            for (int i = 0; i < pts.Length - 1; i++)
                DrawSeg(px, Res, pts[i], pts[i + 1], 3.2f);
            return Bake(px, Res);
        }

        private static Sprite MakeSwirl()
        {
            Color[] px = NewClear(Res);
            Vector2 c = new(Res * 0.5f, Res * 0.5f);
            Vector2 prev = c;
            for (int i = 0; i <= 90; i++)
            {
                float t = i / 90f;
                float ang = t * Mathf.PI * 3.2f;
                float r = Mathf.Lerp(2f, Res * 0.44f, t);
                Vector2 p = c + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * r;
                if (i > 0) DrawSeg(px, Res, prev, p, 2.6f);
                prev = p;
            }
            return Bake(px, Res);
        }

        // ---- low-level raster helpers ----------------------------------------------------

        private static Color[] NewClear(int res)
        {
            Color[] px = new Color[res * res];
            for (int i = 0; i < px.Length; i++) px[i] = Color.clear;
            return px;
        }

        private static void DrawSeg(Color[] px, int res, Vector2 a, Vector2 b, float radius)
        {
            int steps = Mathf.CeilToInt(Vector2.Distance(a, b));
            int r = Mathf.CeilToInt(radius);
            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / Mathf.Max(1f, steps));
                for (int oy = -r; oy <= r; oy++)
                for (int ox = -r; ox <= r; ox++)
                {
                    int gx = Mathf.RoundToInt(p.x) + ox;
                    int gy = Mathf.RoundToInt(p.y) + oy;
                    if (gx < 0 || gx >= res || gy < 0 || gy >= res) continue;
                    float dd = Mathf.Sqrt(ox * ox + oy * oy);
                    if (dd > radius) continue;
                    float a2 = Mathf.SmoothStep(1f, 0f, dd / radius);
                    int idx = gy * res + gx;
                    if (a2 > px[idx].a) px[idx] = new Color(1f, 1f, 1f, a2);
                }
            }
        }

        private static Sprite Bake(Color[] px, int res)
        {
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
