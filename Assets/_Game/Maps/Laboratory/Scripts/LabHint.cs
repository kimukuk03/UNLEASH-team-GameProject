using TMPro;
using UnityEngine;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 바닥/벽에 떠 있는 월드 공간 안내 문구 (튜토리얼 힌트, 구역 표지판).
    /// - Proximity : 플레이어가 showRadius 안에 있을 때만 보인다.
    /// - Manual    : Show() 가 호출될 때 보인다 (이벤트 연출용).
    /// - Always    : 항상 보인다.
    /// </summary>
    public class LabHint : MonoBehaviour
    {
        public enum Mode
        {
            Proximity,
            Manual,
            Always,
        }

        [SerializeField] private TMP_Text label;
        [SerializeField] private Mode mode = Mode.Proximity;
        [SerializeField] private float showRadius = 4f;
        [SerializeField] private float fadeSpeed = 3f;
        [Tooltip("Manual 모드에서 Show() 후 유지 시간. 0 이하면 계속 표시.")]
        [SerializeField] private float manualDuration = 0f;
        [SerializeField] private bool pulse = false;

        private float alpha;
        private float manualTimer;
        private bool manualVisible;
        private Color baseColor = Color.white;

        public TMP_Text Label { get { return label; } }

        private void Awake()
        {
            if (label == null)
            {
                label = GetComponentInChildren<TMP_Text>();
            }

            if (label != null)
            {
                baseColor = label.color;
            }

            alpha = mode == Mode.Always ? 1f : 0f;
            Apply();
        }

        private void Update()
        {
            float target = 0f;
            switch (mode)
            {
                case Mode.Always:
                    target = 1f;
                    break;
                case Mode.Proximity:
                    Transform player = LabMobUtil.FindPlayer();
                    if (player != null)
                    {
                        float d = Vector2.Distance(player.position, transform.position);
                        target = d <= showRadius ? 1f : 0f;
                    }
                    break;
                case Mode.Manual:
                    if (manualVisible && manualDuration > 0f)
                    {
                        manualTimer -= Time.deltaTime;
                        if (manualTimer <= 0f)
                        {
                            manualVisible = false;
                        }
                    }
                    target = manualVisible ? 1f : 0f;
                    break;
            }

            alpha = Mathf.MoveTowards(alpha, target, fadeSpeed * Time.deltaTime);
            Apply();
        }

        public void Show()
        {
            manualVisible = true;
            manualTimer = manualDuration;
        }

        public void Show(string text)
        {
            SetText(text);
            Show();
        }

        public void Hide()
        {
            manualVisible = false;
        }

        public void SetText(string text)
        {
            if (label != null)
            {
                label.text = text;
            }
        }

        private void Apply()
        {
            if (label == null)
            {
                return;
            }

            float a = alpha;
            if (pulse && a > 0f)
            {
                a *= 0.75f + 0.25f * Mathf.Sin(Time.time * 5f);
            }

            Color c = baseColor;
            c.a = baseColor.a * a;
            label.color = c;
            label.enabled = a > 0.001f;
        }
    }
}
