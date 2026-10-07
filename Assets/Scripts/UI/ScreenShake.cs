using UnityEngine;

namespace StackSurge.UI
{
    /// <summary>
    /// Shakes <see cref="Target"/> around the position it had when the shake started, and only writes the
    /// transform while a shake is running. (Shaking a root Canvas does nothing: the canvas resets its own transform.)
    /// </summary>
    public class ScreenShake : MonoBehaviour
    {
        public RectTransform Target;
        public float Remaining;
        public float Intensity;

        Vector2 _basePosition;
        bool _shaking;

        void LateUpdate()
        {
            if (!_shaking || Target == null) return;

            // Unscaled: with the clock frozen (pause, panels) a scaled timer would never run out and the board
            // would jitter behind the pause menu.
            Remaining -= Time.unscaledDeltaTime;
            if (Remaining <= 0f)
            {
                StopShake();
                return;
            }

            Target.anchoredPosition = _basePosition + new Vector2(
                (Random.value - 0.5f) * 2f * Intensity,
                (Random.value - 0.5f) * 2f * Intensity);
        }

        public void AddShake(float amount, float duration)
        {
            if (Target == null) return;
            if (!_shaking)
            {
                _basePosition = Target.anchoredPosition;
                _shaking = true;
            }
            Intensity = Mathf.Max(Intensity, amount);
            Remaining = Mathf.Max(Remaining, duration);
        }

        /// <summary>Ends any running shake and puts the target back where it started.</summary>
        public void StopShake()
        {
            if (_shaking && Target != null) Target.anchoredPosition = _basePosition;
            _shaking = false;
            Remaining = 0f;
            Intensity = 0f;
        }

        void OnDisable() => StopShake();
    }
}
