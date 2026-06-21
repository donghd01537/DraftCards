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
        // Manual fine-tune for the ground shadow's vertical offset (local units, added to the
        // computed feet position). Tweak in the Inspector if the shadow sits above/below the
        // feet for the current art. Positive = shadow lower.
        [SerializeField] private float _shadowExtraDrop = 0f;
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
        private Coroutine _levelUpPulseRoutine;
        private Coroutine _healPulseRoutine;
        private readonly List<GameObject> _healPlusInstances = new();
        // The unit's resting transform scale, captured before a scale pulse starts so the pulse
        // always returns to it. Captured only when no pulse is running, so overlapping pulses
        // (e.g. a healer hitting the same unit again mid-pulse) can't read a mid-pulse, enlarged
        // scale as their base and ratchet the unit permanently bigger.
        private bool _hasPulseBaseScale;
        private Vector3 _pulseBaseScale = Vector3.one;
        // Green tint flashed over a unit when it's healed by a support healer (Cleric / Shaman).
        private static readonly Color HealFlashColor = new(0.4f, 1f, 0.5f, 1f);
        private static readonly Color HealPlusColor = new(0.78f, 1f, 0.76f, 0.95f);
        private MoveBounceAnimator _moveAnimator;
        // Every unit (player and enemy) walks with the toy-march UnitWalkAnimator — it's the
        // standard now that all unit art has a feet pivot. Built lazily on the sprite container
        // at bind time; the legacy MoveBounceAnimator is disabled so the two never both drive
        // the container transform.
        private UnitWalkAnimator _walkAnimator;
        private bool _highlighted;
        private bool _slowTintActive;
        private Color _baseSpriteTint = Color.white;

        // Shared walk tuning, fed to the UnitWalkAnimator at bind time.
        [Header("Walk (toy-march)")]
        // Fixed forward lean (deg) the body holds into the charge.
        [SerializeField] private float _walkForwardLean = 5f;
        // Alternating step wobble (deg) layered on the lean.
        [SerializeField] private float _walkSwayAngle = 9f;
        // Marching cadence (rad/s); the bounce runs at twice this. Higher = faster, snappier
        // step rotation/sway (the effective "frame rate" of the procedural march).
        [SerializeField] private float _walkSwaySpeed = 34f;
        // Peak per-step hop height. Smaller now — the wobble carries the motion, not the hop.
        [SerializeField] private float _walkBobHeight = 2f;

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
        // The normalized pivot actually applied to the sprite slots in RebuildSprites. The
        // shadow placement reuses this exact value (not a fresh sprite.pivot read) so the
        // shadow can never disagree with where the sprite is parked.
        private Vector2 _appliedPivot = new(0.5f, 0.5f);

        public void BindPreview(PendingUnitBuild build, bool isPlayerSide)
        {
            _shadowScale = build.shadowScale;
            RebuildSprites(1, build.idleSprite, build.attackFrames, isPreview: true);
        }

        public void BindReal(UnitGroup unit)
        {
            _shadowScale = unit.ShadowScale;
            RebuildSprites(1, unit.IdleSprite, unit.AttackFrames, isPreview: false);
            ConfigureWalk();
        }

        // Sets up the standard toy-march walk for this unit. Every unit uses UnitWalkAnimator;
        // the legacy MoveBounceAnimator is disabled so the two can't both drive the sprite
        // container's transform.
        private void ConfigureWalk()
        {
            if (_spriteContainer == null) return;

            if (_moveAnimator == null)
            {
                _moveAnimator = _spriteContainer.GetComponent<MoveBounceAnimator>();
            }
            if (_moveAnimator != null) _moveAnimator.enabled = false;

            if (_walkAnimator == null)
            {
                _walkAnimator = _spriteContainer.GetComponent<UnitWalkAnimator>();
                if (_walkAnimator == null)
                {
                    _walkAnimator = _spriteContainer.gameObject.AddComponent<UnitWalkAnimator>();
                }
            }
            _walkAnimator.Configure(_walkForwardLean, _walkSwayAngle, _walkSwaySpeed, _walkBobHeight);
        }

        public void RefreshFromBuild(PendingUnitBuild build)
        {
            _shadowScale = build.shadowScale;
            RebuildSprites(1, build.idleSprite, build.attackFrames, isPreview: true);
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

        public void PlayLevelUpPulse()
        {
            PlayHitFlash();
            if (_levelUpPulseRoutine != null) StopCoroutine(_levelUpPulseRoutine);
            _levelUpPulseRoutine = StartCoroutine(LevelUpPulseRoutine());
        }

        // Soft green flash, gentle scale pulse, and floating plus signs shown when a healer restores HP.
        // Per-unit secondary cue (see BattleField.md VFX conventions) — kept subtle so a whole
        // line healing at once doesn't wash out the battlefield.
        public void PlayHealPulse()
        {
            if (_healPulseRoutine != null) StopCoroutine(_healPulseRoutine);
            ClearHealPlusInstances();
            _healPulseRoutine = StartCoroutine(HealPulseRoutine());
        }

        // Captures the unit's resting scale the first time any scale pulse begins, and snaps the
        // transform back to it for every later pulse. Prevents overlapping pulses from compounding
        // a permanent size increase (they all share one true base, never a mid-pulse value).
        private Vector3 BeginScalePulse()
        {
            if (!_hasPulseBaseScale)
            {
                _pulseBaseScale = transform.localScale;
                _hasPulseBaseScale = true;
            }
            transform.localScale = _pulseBaseScale;
            return _pulseBaseScale;
        }

        private IEnumerator HealPulseRoutine()
        {
            foreach (Material mat in _flashMaterials)
            {
                if (mat != null) mat.SetColor(FlashColorId, HealFlashColor);
            }

            Transform target = transform;
            Vector3 baseScale = BeginScalePulse();
            Vector3 peakScale = baseScale * 1.08f;
            const float duration = 0.4f;
            const float peakFlash = 0.32f;
            List<HealPlusFx> plusFx = SpawnHealPlusFx();

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float pulse = Mathf.Sin(u * Mathf.PI);
                target.localScale = Vector3.Lerp(baseScale, peakScale, pulse);
                SetFlashAmount(peakFlash * pulse);
                UpdateHealPlusFx(plusFx, u);
                yield return null;
            }

            target.localScale = baseScale;
            SetFlashAmount(0f);
            ClearHealPlusInstances();
            // Restore the normal white hit-flash color so later damage flashes read correctly.
            foreach (Material mat in _flashMaterials)
            {
                if (mat != null) mat.SetColor(FlashColorId, _hitFlashColor);
            }
            _healPulseRoutine = null;
        }

        private List<HealPlusFx> SpawnHealPlusFx()
        {
            List<HealPlusFx> result = new();
            RectTransform parent = transform as RectTransform;
            if (parent == null) return result;

            float height = Mathf.Max(70f, SpriteRenderedHeight);
            Vector2[] starts =
            {
                new(-height * 0.26f, height * 0.08f),
                new(height * 0.24f, height * 0.18f),
                new(0f, height * 0.34f)
            };
            Vector2[] drifts =
            {
                new(-10f, 25f),
                new(9f, 29f),
                new(2f, 36f)
            };

            for (int i = 0; i < starts.Length; i++)
            {
                GameObject go = new($"HealPlus_{i}", typeof(RectTransform));
                RectTransform rect = go.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = starts[i];
                float size = Mathf.Clamp(height * 0.18f, 13f, 22f);
                rect.sizeDelta = new Vector2(size, size);
                rect.localScale = Vector3.one * (i == 2 ? 1.08f : 0.92f);

                Image image = go.AddComponent<Image>();
                image.sprite = GetHealPlusSprite();
                image.color = HealPlusColor;
                image.raycastTarget = false;
                image.preserveAspect = true;

                _healPlusInstances.Add(go);
                result.Add(new HealPlusFx(rect, image, starts[i], starts[i] + drifts[i], i * 0.07f));
            }

            return result;
        }

        private static void UpdateHealPlusFx(List<HealPlusFx> plusFx, float normalizedTime)
        {
            if (plusFx == null) return;

            for (int i = 0; i < plusFx.Count; i++)
            {
                HealPlusFx fx = plusFx[i];
                if (fx.Rect == null || fx.Image == null) continue;

                if (normalizedTime < fx.Delay)
                {
                    Color hidden = HealPlusColor;
                    hidden.a = 0f;
                    fx.Image.color = hidden;
                    continue;
                }

                float u = Mathf.Clamp01((normalizedTime - fx.Delay) / 0.82f);
                float ease = Smooth01(u);
                fx.Rect.anchoredPosition = Vector2.Lerp(fx.Start, fx.End, ease);
                float scale = Mathf.Lerp(0.76f, 1.08f, Mathf.Sin(u * Mathf.PI));
                fx.Rect.localScale = Vector3.one * scale;

                Color c = HealPlusColor;
                c.a = HealPlusColor.a * (1f - Smooth01(Mathf.InverseLerp(0.45f, 1f, u)));
                fx.Image.color = c;
            }
        }

        private void ClearHealPlusInstances()
        {
            foreach (GameObject go in _healPlusInstances)
            {
                if (go != null) Destroy(go);
            }
            _healPlusInstances.Clear();
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

        private IEnumerator LevelUpPulseRoutine()
        {
            Transform target = transform;
            Vector3 baseScale = BeginScalePulse();
            Vector3 peakScale = baseScale * 1.14f;
            const float duration = 0.34f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float u = Mathf.Clamp01(elapsed / duration);
                float pulse = Mathf.Sin(u * Mathf.PI);
                target.localScale = Vector3.Lerp(baseScale, peakScale, pulse);
                yield return null;
            }

            target.localScale = baseScale;
            _levelUpPulseRoutine = null;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
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
            if (_levelUpPulseRoutine != null)
            {
                StopCoroutine(_levelUpPulseRoutine);
                _levelUpPulseRoutine = null;
            }
            ClearHealPlusInstances();
        }

        public void SetHighlight(bool highlighted)
        {
            _highlighted = highlighted;
            ApplySpriteTint();
        }

        public void SetSlowTint(bool active)
        {
            _slowTintActive = active;
            ApplySpriteTint();
        }

        private void ApplySpriteTint()
        {
            Color tint = _slowTintActive ? new Color(0.46f, 0.78f, 1f, 1f) : _baseSpriteTint;
            if (_highlighted)
            {
                tint = Color.Lerp(tint, new Color(1f, 0.9f, 0.35f, 1f), 0.55f);
                tint.a = 1f;
            }

            foreach (GameObject slot in _spriteInstances)
            {
                if (slot == null) continue;
                Image img = slot.GetComponent<Image>();
                if (img != null) img.color = tint;
            }
        }

        public void SetMoving(bool moving)
        {
            // Lazily resolve the walk animator in case SetMoving fires before BindReal's
            // ConfigureWalk has run (e.g. preview units that never bind a real unit).
            if (_walkAnimator == null && _spriteContainer != null)
            {
                _walkAnimator = _spriteContainer.GetComponent<UnitWalkAnimator>();
            }
            if (_walkAnimator != null) _walkAnimator.SetMoving(moving);
        }

        // Faces the unit left or right by flipping the SPRITE container's X scale (not the unit
        // root). The source art is drawn facing right, so faceRight => positive X. We flip the
        // container rather than the root so it never fights the scale pulses (heal / level-up,
        // which drive the root scale) or the move bounce (which drives container position/rotation
        // only). Sized off the container's current |x| so any art scale is preserved.
        public void SetFacing(bool faceRight)
        {
            if (_spriteContainer == null) return;
            Vector3 scale = _spriteContainer.localScale;
            float magnitude = Mathf.Abs(scale.x);
            if (magnitude <= 0.0001f) magnitude = 1f;
            float signed = faceRight ? magnitude : -magnitude;
            if (Mathf.Approximately(scale.x, signed)) return;
            scale.x = signed;
            _spriteContainer.localScale = scale;
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

        private static Sprite _healPlusSprite;
        private static Sprite GetHealPlusSprite()
        {
            if (_healPlusSprite != null) return _healPlusSprite;

            const int res = 32;
            const int center = res / 2;
            const int armHalfWidth = 4;
            const int armHalfLength = 12;
            Color[] pixels = new Color[res * res];
            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    int dx = Mathf.Abs(x - center);
                    int dy = Mathf.Abs(y - center);
                    bool horizontal = dy <= armHalfWidth && dx <= armHalfLength;
                    bool vertical = dx <= armHalfWidth && dy <= armHalfLength;
                    if (!horizontal && !vertical) continue;

                    int edgeDistance = Mathf.Min(
                        horizontal ? armHalfWidth - dy : armHalfWidth - dx,
                        vertical ? armHalfWidth - dx : armHalfWidth - dy);
                    float alpha = Mathf.Clamp01((edgeDistance + 1f) / 2f);
                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            Texture2D tex = new(res, res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels(pixels);
            tex.Apply();
            _healPlusSprite = Sprite.Create(tex, new Rect(0, 0, res, res), new Vector2(0.5f, 0.5f), 100f);
            return _healPlusSprite;
        }

        private readonly struct HealPlusFx
        {
            public readonly RectTransform Rect;
            public readonly Image Image;
            public readonly Vector2 Start;
            public readonly Vector2 End;
            public readonly float Delay;

            public HealPlusFx(RectTransform rect, Image image, Vector2 start, Vector2 end, float delay)
            {
                Rect = rect;
                Image = image;
                Start = start;
                End = end;
                Delay = delay;
            }
        }

        // The sprite's authored pivot as a 0..1 fraction of its rect (Unity's Sprite.pivot is
        // in pixels from the bottom-left). Falls back to center for a null/degenerate sprite.
        private static Vector2 SpritePivotNormalized(Sprite sprite)
        {
            if (sprite == null) return new Vector2(0.5f, 0.5f);
            Rect r = sprite.rect;
            if (r.width <= 0f || r.height <= 0f) return new Vector2(0.5f, 0.5f);
            return new Vector2(sprite.pivot.x / r.width, sprite.pivot.y / r.height);
        }

        private void RebuildSprites(int count, Sprite idleSprite, List<Sprite> attackFrames, bool isPreview)
        {
            if (_spriteContainer == null) return;

            if (_flashRoutine != null)
            {
                StopCoroutine(_flashRoutine);
                _flashRoutine = null;
            }
            if (_healPulseRoutine != null)
            {
                StopCoroutine(_healPulseRoutine);
                _healPulseRoutine = null;
            }
            ClearHealPlusInstances();
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
            _baseSpriteTint = isPreview ? _previewTint : Color.white;

            if (_flashShader == null) _flashShader = Shader.Find("DraftCards/UIHitFlash");

            // Use the sprite's authored pivot as the RectTransform pivot (UI Images ignore a
            // sprite's import pivot otherwise). With anchoredPosition zero, this parks the
            // sprite's pivot at the container origin — so art authored with a feet pivot
            // (e.g. 0.5, 0.1) sits and rotates on its feet. Sprites still authored at center
            // (0.5, 0.5) behave exactly as before. This is the standard for all units.
            Vector2 pivot = SpritePivotNormalized(idleSprite);
            _appliedPivot = pivot;

            int clamped = Mathf.Max(0, count);
            for (int i = 0; i < clamped; i++)
            {
                GameObject slot = new($"Sprite_{i}", typeof(RectTransform));
                RectTransform rect = slot.GetComponent<RectTransform>();
                rect.SetParent(_spriteContainer, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = pivot;
                rect.anchoredPosition = Vector2.zero;

                Image image = slot.AddComponent<Image>();
                image.preserveAspect = true;
                image.raycastTarget = false;

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

            ApplySpriteTint();
            PositionShadowUnder(idleSprite);
        }

        // Places the ground shadow under the sprite's feet. Each sprite's authored pivot is
        // parked at the container origin (see RebuildSprites). The shadow belongs at the feet,
        // which we derive from where the pivot is in the art:
        //   - Feet-pivot art (pivotY≈0.1) puts the pivot AT the feet → shadow sits at the origin
        //     (just a tiny nudge down so it doesn't z-fight the soles).
        //   - Legacy center-pivot art (pivotY=0.5) puts the pivot at the body center → the feet
        //     are ~half the rendered height below the origin → drop the shadow by that.
        // The pivot is parked at the container origin (see RebuildSprites). The shadow goes at
        // the feet. We model the feet as sitting a small fixed fraction (FeetFracFromBottom)
        // above the art's bottom edge, so the drop from the pivot down to the feet is
        //     (pivotY - FeetFracFromBottom) * h.
        // Feet-pivot art (pivotY set to its real feet fraction ≈ FeetFracFromBottom) → ~0 drop,
        // shadow sits right at the origin where the feet already are. Legacy center-pivot art
        // (pivotY=0.5) → ~0.4*h drop, recovering the old behaviour. Sized from rendered HEIGHT.
        private const float FeetFracFromBottom = 0.08f;
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

            // Reuse the pivot actually applied to the sprite slots (not a fresh sprite.pivot
            // read, which can lag behind a meta change until Unity re-imports). The feet are at
            // the pivot for feet-pivot art and ~FeetFracFromBottom above the bottom for legacy
            // center art; drop the shadow by the gap between the pivot and the feet.
            float pivotY = _appliedPivot.y;
            // Small upward nudge so the shadow kisses the soles rather than sitting under them.
            float feetDrop = Mathf.Max(0f, (pivotY - FeetFracFromBottom) * h) - 3f + _shadowExtraDrop;
            _shadow.anchoredPosition = new Vector2(0f, -feetDrop);
        }
    }
}
