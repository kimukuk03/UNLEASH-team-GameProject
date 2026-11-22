using System.Collections;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 환경 조명 그룹. 한 구역의 Light2D 들을 묶어서 켜고/끄는 용도로만 사용한다.
    /// (영역 size/radius 는 에디터에서 구역을 표시하기 위한 Gizmo 데이터)
    ///
    /// ※ 적에게는 어떤 영향도 주지 않는다 (피해/밀어내기/상태 변경 없음).
    /// </summary>
    public class LabLightZone : MonoBehaviour
    {
        [Header("영역 (로컬, Gizmo 표시용)")]
        [SerializeField] private Vector2 size = new Vector2(8f, 8f);
        [Tooltip("0 보다 크면 사각형 대신 원형으로 표시")]
        [SerializeField] private float radius = 0f;

        [Header("조명")]
        [SerializeField] private bool startLit = false;
        [SerializeField] private Light2D[] lights;

        [Header("켜질 때 연출")]
        [SerializeField] private float powerOnTime = 1.2f;

        private float[] targetIntensities;

        public bool IsLit { get; private set; }

        private void Awake()
        {
            if (lights == null)
            {
                lights = new Light2D[0];
            }

            targetIntensities = new float[lights.Length];
            for (int i = 0; i < lights.Length; i++)
            {
                targetIntensities[i] = lights[i] != null ? lights[i].intensity : 1f;
            }

            IsLit = startLit;
            SetLightsEnabled(startLit);
        }

        /// <summary>조명 켜기 (깜빡이며 켜지는 연출 포함).</summary>
        public void TurnOn()
        {
            if (IsLit)
            {
                return;
            }

            StartCoroutine(PowerOnRoutine());
        }

        public void TurnOff()
        {
            StopAllCoroutines();
            IsLit = false;
            SetLightsEnabled(false);
        }

        private IEnumerator PowerOnRoutine()
        {
            float t = 0f;
            while (t < powerOnTime)
            {
                SetLightsEnabled(Random.value > 0.45f);
                float wait = Random.Range(0.05f, 0.15f);
                t += wait;
                yield return new WaitForSeconds(wait);
            }

            SetLightsEnabled(true);
            IsLit = true;
        }

        private void SetLightsEnabled(bool on)
        {
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] == null)
                {
                    continue;
                }

                lights[i].enabled = on;
                lights[i].intensity = targetIntensities[i];
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.95f, 0.4f, startLit ? 0.35f : 0.15f);
            if (radius > 0f)
            {
                Gizmos.DrawWireSphere(transform.position, radius);
            }
            else
            {
                Gizmos.DrawWireCube(transform.position, new Vector3(size.x, size.y, 0f));
            }
        }
    }
}
