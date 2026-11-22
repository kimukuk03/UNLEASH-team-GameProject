using UnityEngine;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 방 조명 스위치. E 키로 연결된 LabLightZone(조명 그룹)을 켜고, 비상등 등 환경 오브젝트를 끈다.
    /// 순수 환경 연출용이며 적에게는 아무 영향도 주지 않는다.
    /// </summary>
    public class LabLightSwitch : LabInteractable
    {
        [SerializeField] private LabLightZone[] zones;
        [Tooltip("조명이 켜지면 함께 끌 오브젝트 (어두운 분위기용 비상등 등)")]
        [SerializeField] private GameObject[] disableOnLightsOn;
        [SerializeField] private LabHint afterHint;

        protected override void Interact()
        {
            if (zones != null)
            {
                for (int i = 0; i < zones.Length; i++)
                {
                    if (zones[i] != null)
                    {
                        zones[i].TurnOn();
                    }
                }
            }

            if (disableOnLightsOn != null)
            {
                for (int i = 0; i < disableOnLightsOn.Length; i++)
                {
                    if (disableOnLightsOn[i] != null)
                    {
                        disableOnLightsOn[i].SetActive(false);
                    }
                }
            }

            if (afterHint != null)
            {
                afterHint.Show();
            }
        }
    }
}
