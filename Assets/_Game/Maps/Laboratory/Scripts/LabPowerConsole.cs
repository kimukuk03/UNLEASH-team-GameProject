using UnityEngine;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 구역 전력 패널. E 키로:
    ///   1) 연결된 LabLightZone(조명 그룹)을 켜고
    ///   2) 잠긴 문을 해제(+열기)하고
    ///   3) 비상등 등 오브젝트를 끄거나 켠다.
    ///
    /// 예) 어두운 화학실 맨 안쪽 패널 → 화학실 조명 ON + 옆 저장실 잠금 해제 (짧은 우회로 보상).
    /// 환경/동선 연출 전용이며 적에게는 아무 영향도 주지 않는다.
    /// </summary>
    public class LabPowerConsole : LabInteractable
    {
        [SerializeField] private LabLightZone[] zones;
        [SerializeField] private LabDoor[] doorsToUnlock;
        [Tooltip("true 면 해제한 문을 바로 연다. false 면 잠금만 풀고 플레이어가 다가가면 열린다.")]
        [SerializeField] private bool openUnlockedDoors = true;
        [SerializeField] private GameObject[] disableOnPower;
        [SerializeField] private GameObject[] enableOnPower;
        [SerializeField] private LabHint afterHint;

        private bool powered;

        public bool Powered { get { return powered; } }

        protected override void Interact()
        {
            if (powered)
            {
                return;
            }

            powered = true;

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

            if (doorsToUnlock != null)
            {
                for (int i = 0; i < doorsToUnlock.Length; i++)
                {
                    if (doorsToUnlock[i] == null)
                    {
                        continue;
                    }

                    doorsToUnlock[i].Unlock();
                    if (openUnlockedDoors)
                    {
                        doorsToUnlock[i].ForceOpen();
                    }
                }
            }

            SetAll(disableOnPower, false);
            SetAll(enableOnPower, true);

            if (afterHint != null)
            {
                afterHint.Show();
            }
        }

        private static void SetAll(GameObject[] objects, bool active)
        {
            if (objects == null)
            {
                return;
            }

            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null)
                {
                    objects[i].SetActive(active);
                }
            }
        }
    }
}
