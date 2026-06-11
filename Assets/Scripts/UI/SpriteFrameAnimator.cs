using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DraftCards.UI
{
    public class SpriteFrameAnimator : MonoBehaviour
    {
        [SerializeField] private Image _image;
        // Extra time the final attack frame (the strike pose) is held before snapping
        // back to idle, so the hit reads as a deliberate beat instead of a flicker.
        [SerializeField] private float _attackHoldSeconds = 0.2f;

        private Sprite _idleSprite;
        private List<Sprite> _attackFrames;
        private Coroutine _routine;

        public void Initialize(Image image, Sprite idleSprite, List<Sprite> attackFrames)
        {
            _image = image;
            _idleSprite = idleSprite;
            _attackFrames = attackFrames;
            if (_image != null && _idleSprite != null)
            {
                _image.sprite = _idleSprite;
                _image.enabled = true;
                _image.SetNativeSize();
            }
        }

        public void PlayAttack(float duration)
        {
            if (_image == null) return;
            if (_attackFrames == null || _attackFrames.Count == 0) return;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(AttackRoutine(duration));
        }

        // Throw animation: hold the release frame (last attack frame) for the duration,
        // then snap back to idle. Used by ranged throwers (e.g. Cyclop): idle shows the
        // rock-overhead pose, this shows the empty-handed throw pose, then back to idle.
        public void PlayThrow(float duration)
        {
            if (_image == null) return;
            if (_attackFrames == null || _attackFrames.Count == 0) return;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(ThrowRoutine(duration));
        }

        private IEnumerator ThrowRoutine(float duration)
        {
            _image.sprite = _attackFrames[_attackFrames.Count - 1];
            _image.SetNativeSize();
            yield return new WaitForSeconds(duration);
            _image.sprite = _idleSprite;
            _image.SetNativeSize();
            _routine = null;
        }

        public void ReturnToIdle()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            if (_image != null && _idleSprite != null)
            {
                _image.sprite = _idleSprite;
                _image.SetNativeSize();
            }
        }

        private IEnumerator AttackRoutine(float duration)
        {
            int count = _attackFrames.Count;
            float hold = Mathf.Max(0f, _attackHoldSeconds);
            // Spread the lead-up frames across the duration, then hold the final strike
            // frame the extra beat so frame 2 lingers before snapping back to frame 1.
            float perFrame = duration / count;
            for (int i = 0; i < count; i++)
            {
                _image.sprite = _attackFrames[i];
                _image.SetNativeSize();
                float wait = perFrame + (i == count - 1 ? hold : 0f);
                yield return new WaitForSeconds(wait);
            }
            _image.sprite = _idleSprite;
            _image.SetNativeSize();
            _routine = null;
        }
    }
}
