using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 분위기용 조명 효과. (고장난 형광등 깜빡임 / 경고등 점멸 / 은은한 맥동)
    /// 손전등·등명기와는 별개의 Light2D 에만 붙는다.
    /// </summary>
    [RequireComponent(typeof(Light2D))]
    public class LabFlickerLight : MonoBehaviour
    {
        public enum Pattern
        {
            BrokenFlicker,
            AlarmPulse,
            SoftBreath,
        }

        [SerializeField] private Pattern pattern = Pattern.BrokenFlicker;
        [SerializeField] private float speed = 1f;
        [SerializeField] [Range(0f, 1f)] private float minFactor = 0.1f;
        [Tooltip("시작 위상 랜덤화 (여러 개가 동시에 깜빡이지 않게)")]
        [SerializeField] private bool randomPhase = true;

        private Light2D light2D;
        private float baseIntensity;
        private float phase;
        private float nextFlickerTime;
        private float flickerTarget = 1f;

        private void Awake()
        {
            light2D = GetComponent<Light2D>();
            baseIntensity = light2D.intensity;
            phase = randomPhase ? Random.Range(0f, 10f) : 0f;
        }

        /// <summary>외부(이벤트)에서 기준 밝기를 바꿀 때 사용.</summary>
        public void SetBaseIntensity(float value)
        {
            baseIntensity = value;
        }

        private void Update()
        {
            float t = Time.time * speed + phase;
            float factor = 1f;

            switch (pattern)
            {
                case Pattern.BrokenFlicker:
                    if (Time.time >= nextFlickerTime)
                    {
                        bool longOff = Random.value < 0.12f;
                        flickerTarget = longOff ? minFactor : Random.Range(0.6f, 1f);
                        nextFlickerTime = Time.time + (longOff ? Random.Range(0.15f, 0.6f) : Random.Range(0.03f, 0.18f)) / Mathf.Max(0.1f, speed);
                    }
                    factor = flickerTarget;
                    break;

                case Pattern.AlarmPulse:
                    factor = Mathf.Lerp(minFactor, 1f, Mathf.Pow(0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f), 3f));
                    break;

                case Pattern.SoftBreath:
                    factor = Mathf.Lerp(minFactor, 1f, 0.5f + 0.5f * Mathf.Sin(t * 1.5f));
                    break;
            }

            light2D.intensity = baseIntensity * factor;
        }
    }
}
