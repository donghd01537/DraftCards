using System.Collections;
using System.Collections.Generic;
using DraftCards.Cards;
using DraftCards.Units;
using UnityEngine;
using UnityEngine.UI;

namespace DraftCards.UI
{
    public class UnitGroupView : MonoBehaviour
    {
        [SerializeField] private RectTransform _spriteContainer;
        [SerializeField] private RectTransform _shadow;
        // Uniform scale applied to every sprite's *native* pixel size. Units keep their
        // original art proportions — a bigger source sprite stays bigger on screen — and
        // this only brings the overall size down to something the battlefield can hold.
        // Set to 1 for true native size.
        [SerializeField] private float _spriteScale = 0.47f;
        [SerializeField] private Color _previewTint = Color.white;
        // Hit flash: how white the sprite blinks (1 = fully white) and how long the
        // blink lasts before fading back to normal. Peak is the maximum lerp toward
        // white at the start of the blink — kept low so the flash reads as a subtle
        // tint rather than washing the sprite out.
        [SerializeField] private Color _hitFlashColor = Color.white;
        [SerializeField] private float _hitFlashDuration = 0.12f;
        [SerializeField, Range(0f, 1f)] private float _hitFlashPeak = 0.2f;

        public RectTransform SpriteContainer => _spriteContainer;

        // Rendered height of the unit's sprite in this view's local space (native pixel
        // height * _spriteScale). Used to place a thrown projectile at the unit's hands.
        public float SpriteRenderedHeight { get; private set; }

        // Shared art-scale factor applied to every sprite's native size. A thrown
        // projectile uses this so it renders at the same pixel density as the units.
        public float SpriteScale => _spriteScale;

        private readonly List<SpriteFrameAnimator> _frameAnimators = new();
        private readonly List<GameObject> _spriteInstances = new();
        // Per-Image material instances that drive the white hit flash (one per slot).
        private readonly List<Material> _flashMaterials = new();
        private Coroutine _flashRoutine;
        private MoveBounceAnimator _moveAnimator;

        // Fortify shield bubble: a soft, pulsing dome drawn over the unit while the
        // Fortify immunity window is active. Built lazily the first time it's shown.
        [SerializeField] private Color _shieldColor = new(0.45f, 0.8f, 1f, 0.45f);
        private GameObject _shieldVisual;
        private Image _shieldImage;
        private Coroutine _shieldPulseRoutine;

        private static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");
        private static readonly int FlashColorId = Shader.PropertyToID("_FlashColor");
        private static Shader _flashShader;
        // Per-unit multiplier on the ground shadow size (1 = default). Bigger units like
        // the Cyclop set this above 1 so their shadow reads at their scale.
        private float _shadowScale = 1f;

        public void BindPreview(PendingUnitBuild build, bool isPlayerSide)
        {
            _shadowScale = build.shadowScale;
            RebuildSprites(1, build.idleSprite, build.attackFrames, isPreview: true);
        }

        public void BindReal(UnitGroup unit)
        {
            _shadowScale = unit.ShadowScale;
            RebuildSprites(1, unit.IdleSprite, unit.AttackFrames, isPreview: false);
        }

        public void RefreshFromBuild(PendingUnitBuild build)
        {
            _shadowScale = build.shadowScale;
            RebuildSprites(1, build.idleSprite, build.attackFrames, isPreview: true);
        }

        public void RefreshFromUnit(UnitGroup unit)
        {
            _shadowScale = unit.ShadowScale;
            RebuildSprites(1, unit.IdleSprite, unit.AttackFrames, isPreview: false);
        }

        public void PlayAttack(float duration)
        {
            foreach (SpriteFrameAnimator anim in _frameAnimators)
            {
                if (anim != null) anim.PlayAttack(duration);
            }
        }

        public void PlayThrow(float duration)
        {
            foreach (SpriteFrameAnimator anim in _frameAnimators)
            {
                if (anim != null) anim.PlayThrow(duration);
            }
        }

        // Blinks the unit's sprite white, then fades back — used as a hit reaction when
        // the unit takes damage. No-op if the flash shader/material isn't available.
        public void PlayHitFlash()
        {
            if (_flashMaterials.Count == 0) return;
            if (_flashRoutine != null) StopCoroutine(_flashRoutine);
            _flashRoutine = StartCoroutine(HitFlashRoutine());
        }

        private IEnumerator HitFlashRoutine()
        {
            float peak = Mathf.Clamp01(_hitFlashPeak);
            SetFlashAmount(peak);
            float t = 0f;
            float dur = Mathf.Max(0.001f, _hitFlashDuration);
            while (t < dur)
            {
                t += Time.deltaTime;
                SetFlashAmount(peak * (1f - (t / dur)));
                yield return null;
            }
            SetFlashAmount(0f);
            _flashRoutine = null;
        }

        private void SetFlashAmount(float amount)
        {
            foreach (Material mat in _flashMaterials)
            {
                if (mat != null) mat.SetFloat(FlashAmountId, amount);
            }
        }

        private void OnDestroy()
        {
            foreach (Material mat in _flashMaterials)
            {
                if (mat != null) Destroy(mat);
            }
            _flashMaterials.Clear();

            if (_shieldPulseRoutine != null)
            {
                StopCoroutine(_shieldPulseRoutine);
                _shieldPulseRoutine = null;
            }
        }

        public void SetHighlight(bool highlighted)
        {
            Color tint = highlighted ? new Color(1f, 0.9f, 0.35f, 1f) : Color.white;
            foreach (GameObject slot in _spriteInstances)
            {
                if (slot == null) continue;
                Image img = slot.GetComponent<Image>();
                if (img != null) img.color = tint;
            }
        }

        public void SetMoving(bool moving)
        {
            if (_moveAnimator == null && _spriteContainer != null)
            {
                _moveAnimator = _spriteContainer.GetComponent<MoveBounceAnimator>();
            }
            if (_moveAnimator != null) _moveAnimator.SetMoving(moving);
        }

        // Shows/hides the Fortify shield dome over the unit. Sized from the sprite's
        // rendered height so it scales with the unit, and gently pulses while active.
        public void SetShield(bool active)
        {
            if (active)
            {
                EnsureShieldVisual();
                if (_shieldVisual == null) return;
                _shieldVisual.SetActive(true);
                if (_shieldPulseRoutine == null && isActiveAndEnabled)
                {
                    _shieldPulseRoutine = StartCoroutine(ShieldPulse());
                }
                return;
            }

            if (_shieldPulseRoutine != null)
            {
                StopCoroutine(_shieldPulseRoutine);
                _shieldPulseRoutine = null;
            }
            if (_shieldVisual != null) _shieldVisual.SetActive(false);
        }

        private void EnsureShieldVisual()
        {
            if (_shieldVisual != null || _spriteContainer == null) return;

            GameObject go = new("ShieldBubble", typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            // Parent under the unit root (sibling of the sprite container) so the dome
            // doesn't inherit the sprite's flip/bounce, and centers on the unit body.
            rect.SetParent(transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);

            float size = Mathf.Max(70f, SpriteRenderedHeight * 1.15f);
            rect.sizeDelta = new Vector2(size, size);
            rect.anchoredPosition = new Vector2(0f, -SpriteRenderedHeight * 0.1f);

            _shieldImage = go.AddComponent<Image>();
            _shieldImage.sprite = GetShieldSprite();
            _shieldImage.color = _shieldColor;
            _shieldImage.raycastTarget = false;
            _shieldImage.preserveAspect = true;
            // Draw behind the unit sprite so the unit stays readable; the dome reads as a
            // halo around them. The sprite container is the last sibling, so index 0 is behind.
            rect.SetSiblingIndex(0);

            _shieldVisual = go;
        }

        // Soft radial dome sprite, generated once and shared by every unit's shield bubble.
        // Bright rim, translucent center — reads as an energy shield without an art asset.
        private static Sprite _shieldSprite;

        private static Sprite GetShieldSprite()
        {
            if (_shieldSprite != null) return _shieldSprite;

            const int res = 128;
            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            Vector2 center = new(res * 0.5f, res * 0.5f);
            float radius = res * 0.5f;
            Color[] pixels = new Color[res * res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), center) / radius;
                    float alpha;
                    if (dist > 1f)
                    {
                        alpha = 0f;
                    }
                    else
                    {
                        // Faint fill that brightens toward a crisp rim near the edge.
                        float fill = Mathf.Lerp(0.18f, 0.4f, dist);
                        float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.82f, 0.99f, dist));
                        alpha = Mathf.Clamp01(fill + rim) * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.97f, 1f, dist));
                    }
                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            _shieldSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _shieldSprite;
        }

        private IEnumerator ShieldPulse()
        {
            float t = 0f;
            while (true)
            {
                t += Time.deltaTime;
                if (_shieldImage != null)
                {
                    float pulse = 0.5f + 0.5f * Mathf.Sin(t * 4f);
                    Color c = _shieldColor;
                    c.a = Mathf.Lerp(_shieldColor.a * 0.55f, _shieldColor.a, pulse);
                    _shieldImage.color = c;
                    float s = Mathf.Lerp(0.97f, 1.05f, pulse);
                    _shieldVisual.transform.localScale = new Vector3(s, s, 1f);
                }
                yield return null;
            }
        }

        private void RebuildSprites(int count, Sprite idleSprite, List<Sprite> attackFrames, bool isPreview)
        {
            if (_spriteContainer == null) return;

            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
                _flashRoutine = null;
            }
            foreach (GameObject go in _spriteInstances)
            {
                if (go != null) Destroy(go);
            }
            foreach (Material mat in _flashMaterials)
            {
                if (mat != null) Destroy(mat);
            }
            _spriteInstances.Clear();
            _frameAnimators.Clear();
            _flashMaterials.Clear();

            if (_flashShader == null) _flashShader = Shader.Find("DraftCards/UIHitFlash");

            int clamped = Mathf.Max(0, count);
            for (int i = 0; i < clamped; i++)
            {
                GameObject slot = new($"Sprite_{i}", typeof(RectTransform));
                RectTransform rect = slot.GetComponent<RectTransform>();
                rect.SetParent(_spriteContainer, false);
                // Center the sprite on the container (no layout group does this now).
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;

                Image image = slot.AddComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;
                image.color = isPreview ? _previewTint : Color.white;

                // Give each Image its own flash material so the white blink can be driven
                // per unit without touching the shared default UI material.
                if (_flashShader != null)
                {
                    Material flashMat = new(_flashShader);
                    flashMat.SetColor(FlashColorId, _hitFlashColor);
                    flashMat.SetFloat(FlashAmountId, 0f);
                    image.material = flashMat;
                    _flashMaterials.Add(flashMat);
                }
                else
                {
                    _flashMaterials.Add(null);
                }

                SpriteFrameAnimator anim = slot.AddComponent<SpriteFrameAnimator>();
                anim.Initialize(image, idleSprite, attackFrames);
                _frameAnimators.Add(anim);

                // Original proportions: scale the native-sized sprite by a shared factor.
                slot.transform.localScale = new Vector3(_spriteScale, _spriteScale, 1f);

                _spriteInstances.Add(slot);
            }

            PositionShadowUnder(idleSprite);
        }

        // Places the ground shadow under the sprite's feet. The sprite is centered on the
        // unit root, so its feet sit a little above -renderedHeight/2 (preserveAspect leaves
        // transparent padding). The shadow is sized from the rendered HEIGHT (not width) so
        // it stays consistent no matter how tightly each sprite was trimmed on import, and
        // its center is parked just under the feet so the unit reads as standing on it.
        private void PositionShadowUnder(Sprite reference)
        {
            if (reference == null) return;
            SpriteRenderedHeight = reference.rect.height * _spriteScale;

            if (_shadow == null) return;

            float h = SpriteRenderedHeight;
            float scale = _shadowScale > 0f ? _shadowScale : 1f;

            float shadowWidth = Mathf.Max(56f, h * 1.4f * scale);
            float shadowHeight = shadowWidth * 0.25f;
            _shadow.sizeDelta = new Vector2(shadowWidth, shadowHeight);
            // The sprite is centered with preserveAspect, so its art (and feet) sit above the
            // rect's bottom edge by some transparent padding. The bigger a unit's shadowScale,
            // the larger the sprite and the less drop it needs to look grounded — the Cyclop
            // (scale 1.6) sits right at 0.42, while scale-1 units need ~0.50 to not float.
            float drop = Mathf.Lerp(0.50f, 0.42f, Mathf.InverseLerp(1f, 1.6f, scale));
            _shadow.anchoredPosition = new Vector2(0f, -h * drop);
        }
    }
}
