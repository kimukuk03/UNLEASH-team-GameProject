using System.Collections;
using UnityEngine;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 트리거형 조우 이벤트. 플레이어가 영역에 처음 들어오면:
    ///   1) (delay 후) 문 열기 → 2) 숨겨둔 적 그룹 활성화 + 감지범위 확장 → 3) 힌트/경고등 켜기
    ///   4) (unlockDelay 후) 잠긴 문 해제
    ///
    /// 사용처: 첫 습격, 빛 약점 발견, 방에서 튀어나오는 적, 좁은 골목 양쪽 협공.
    /// ※ 적 처치 수로 진행을 막지 않는다 (현재 적은 쉴드 때문에 총으로 잘 안 죽으므로 소프트락 방지).
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class LabEncounter : MonoBehaviour
    {
        [Header("발동")]
        [SerializeField] private float delay = 0f;

        [Header("적")]
        [Tooltip("발동 시 SetActive(true) 할 적 그룹 (MobController 프리팹 인스턴스, 자식에 LabMob)")]
        [SerializeField] private GameObject[] enemyGroups;
        [Tooltip("발동 시 적 감지 반경 (월드 단위). 기본 프리팹은 4.")]
        [SerializeField] private float aggroRadius = 12f;
        [Tooltip("그룹 안 적을 한 번에 다 꺼내지 않고 이 간격으로 하나씩 꺼낸다.")]
        [SerializeField] private float staggerInterval = 0.25f;

        [Header("문")]
        [SerializeField] private LabDoor[] doorsToOpen;
        [SerializeField] private LabDoor[] doorsToClose;
        [SerializeField] private LabDoor[] doorsToUnlock;
        [SerializeField] private float unlockDelay = 0f;

        [Header("연출")]
        [SerializeField] private GameObject[] activateObjects;
        [SerializeField] private LabHint[] hints;

        private bool fired;

        public bool Fired { get { return fired; } }

        private void Awake()
        {
            // 매복 적은 발동 전까지 꺼둔다 (Monster.Start 가 MobController 에 등록되는 시점도 이때가 된다).
            if (enemyGroups != null)
            {
                for (int i = 0; i < enemyGroups.Length; i++)
                {
                    if (enemyGroups[i] == null)
                    {
                        continue;
                    }

                    for (int c = 0; c < enemyGroups[i].transform.childCount; c++)
                    {
                        enemyGroups[i].transform.GetChild(c).gameObject.SetActive(false);
                    }
                }
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (fired || !other.CompareTag("Player"))
            {
                return;
            }

            Fire();
        }

        public void Fire()
        {
            if (fired)
            {
                return;
            }

            fired = true;
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            if (delay > 0f)
            {
                yield return new WaitForSeconds(delay);
            }

            if (doorsToClose != null)
            {
                for (int i = 0; i < doorsToClose.Length; i++)
                {
                    if (doorsToClose[i] != null)
                    {
                        doorsToClose[i].Close();
                        doorsToClose[i].Lock();
                    }
                }
            }

            if (doorsToOpen != null)
            {
                for (int i = 0; i < doorsToOpen.Length; i++)
                {
                    if (doorsToOpen[i] != null)
                    {
                        doorsToOpen[i].ForceOpen();
                    }
                }
            }

            if (activateObjects != null)
            {
                for (int i = 0; i < activateObjects.Length; i++)
                {
                    if (activateObjects[i] != null)
                    {
                        activateObjects[i].SetActive(true);
                    }
                }
            }

            if (hints != null)
            {
                for (int i = 0; i < hints.Length; i++)
                {
                    if (hints[i] != null)
                    {
                        hints[i].Show();
                    }
                }
            }

            if (unlockDelay > 0f || (doorsToUnlock != null && doorsToUnlock.Length > 0))
            {
                StartCoroutine(UnlockLater());
            }

            if (enemyGroups != null)
            {
                for (int g = 0; g < enemyGroups.Length; g++)
                {
                    GameObject group = enemyGroups[g];
                    if (group == null)
                    {
                        continue;
                    }

                    group.SetActive(true);
                    for (int c = 0; c < group.transform.childCount; c++)
                    {
                        GameObject mob = group.transform.GetChild(c).gameObject;
                        LabMobUtil.SetAggroRadius(mob, aggroRadius);
                        mob.SetActive(true);
                        if (staggerInterval > 0f)
                        {
                            yield return new WaitForSeconds(staggerInterval);
                        }
                    }
                }
            }
        }

        private IEnumerator UnlockLater()
        {
            if (unlockDelay > 0f)
            {
                yield return new WaitForSeconds(unlockDelay);
            }

            if (doorsToUnlock == null)
            {
                yield break;
            }

            for (int i = 0; i < doorsToUnlock.Length; i++)
            {
                if (doorsToUnlock[i] != null)
                {
                    doorsToUnlock[i].Unlock();
                    doorsToUnlock[i].ForceOpen();
                }
            }
        }
    }
}
