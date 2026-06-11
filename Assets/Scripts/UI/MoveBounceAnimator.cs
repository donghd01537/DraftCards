using UnityEngine;

namespace DraftCards.UI
{
    public class MoveBounceAnimator : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private float _fps = 12f;
        [SerializeField] private float _amplitude = 0.5f;
        [SerializeField] private float _moveShakeAngle = 2f;
        [SerializeField] private float _moveShakeSpeed = 11f;
        [SerializeField] private float _moveStepHeight = 1.5f;
        [SerializeField] private float[] _yOffsets = { 0f, 1f, 3f, 1f, 0f, -1f, 0f, 0f };

        private float _timer;
        private float _moveShakeTimer;
        private Vector3 _baseLocalPosition;
        private Quaternion _baseLocalRotation;
        private bool _captured;
        private bool _moving;

        public void SetMoving(bool moving)
        {
            _moving = moving;
            if (!moving)
            {
                _moveShakeTimer = 0f;
                if (_captured && _target != null)
                {
                    _target.localRotation = _baseLocalRotation;
                }
            }
        }

        private void Awake()
        {
            if (_target == null) _target = transform;
            float loop = LoopDuration();
            _timer = loop > 0f ? Random.Range(0f, loop) : 0f;
            _amplitude *= Random.Range(0.85f, 1.25f);
            _moveStepHeight *= Random.Range(0.85f, 1.20f);
            _moveShakeSpeed *= Random.Range(0.9f, 1.15f);
            _moveShakeAngle *= Random.Range(0.85f, 1.20f);
        }

        private void Update()
        {
            if (_target == null) return;
            if (!_captured)
            {
                _baseLocalPosition = _target.localPosition;
                _baseLocalRotation = _target.localRotation;
                _captured = true;
            }

            if (_yOffsets == null || _yOffsets.Length == 0 || _fps <= 0f) return;

            _timer += Time.deltaTime;
            if (_moving) _moveShakeTimer += Time.deltaTime;
            float loop = LoopDuration();
            float t = _timer % loop;
            int frame = Mathf.FloorToInt(t * _fps);
            if (frame >= _yOffsets.Length) frame = _yOffsets.Length - 1;

            float angle = 0f;
            float y = _yOffsets[frame] * _amplitude;
            if (_moving)
            {
                int moveFrame = Mathf.FloorToInt(_moveShakeTimer * _moveShakeSpeed) % 2;
                bool upFrame = moveFrame == 0;
                y = upFrame ? _moveStepHeight : -_moveStepHeight;
            }
            _target.localPosition = _baseLocalPosition + new Vector3(0f, y, 0f);
            _target.localRotation = _baseLocalRotation * Quaternion.Euler(0f, 0f, angle);
        }

        private float LoopDuration() => _yOffsets.Length / _fps;
    }
}
