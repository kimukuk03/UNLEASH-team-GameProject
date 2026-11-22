using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 디펜스 구간. 제어 콘솔에서 E → 보스방 셔터가 천천히 열리기 시작하고,
    /// 셔터가 다 열릴 때까지 WavePoint 들(사방)에서 적이 웨이브로 몰려온다.
    ///
    /// - 적 스폰: enemyPrefab(LabMob) 을 waveGroup(MobController) 의 자식으로 Instantiate
    ///   → Monster.Start 가 부모 MobController 에 정상 등록된다.
    /// - 진행은 "시간 버티기" 방식 (처치 수 조건 없음 → 소프트락 방지).
    /// </summary>
    public class LabDefenseEvent : LabInteractable
    {
        [System.Serializable]
        public class Wave
        {
            public float startTime = 0f;
            public int count = 3;
            [Tooltip("이 웨이브에서 사용할 WavePoint 인덱스. 비우면 전체에서 랜덤.")]
            public int[] pointIndices;
        }

        [Header("문")]
        [SerializeField] private LabDoor[] gates;
        [SerializeField] private float gateOpenDuration = 45f;
        [Tooltip("이벤트 시작 시 닫고 잠글 문 (들어온 입구)")]
        [SerializeField] private LabDoor[] lockBehind;
        [Tooltip("셔터가 다 열리면 다시 열어줄 문")]
        [SerializeField] private LabDoor[] unlockOnComplete;

        [Header("적")]
        [SerializeField] private GameObject enemyPrefab;
        [SerializeField] private Transform waveGroup;
        [SerializeField] private Transform[] wavePoints;
        [SerializeField] private Wave[] waves;
        [SerializeField] private int maxAlive = 10;
        [SerializeField] private float aggroRadius = 30f;
        [SerializeField] private float spawnSpacing = 0.35f;
        [SerializeField] private GameObject spawnEffectPrefab;

        [Header("연출")]
        [SerializeField] private Light2D[] alarmLights;
        [SerializeField] private LabHint statusHint;
        [SerializeField] private LabHint completeHint;

        private readonly List<GameObject> alive = new List<GameObject>();
        private bool running;
        private bool complete;
        private float elapsed;

        public bool IsRunning { get { return running; } }
        public bool IsComplete { get { return complete; } }

        protected override void Interact()
        {
            if (running || complete)
            {
                return;
            }

            StartCoroutine(Run());
        }

        protected override void Update()
        {
            base.Update();

            if (running && statusHint != null)
            {
                float remain = Mathf.Max(0f, gateOpenDuration - elapsed);
                statusHint.SetText("GATE OPENING  " + Mathf.CeilToInt(remain) + "s\nHOLD THE LINE");
            }
        }

        private IEnumerator Run()
        {
            running = true;
            elapsed = 0f;

            SetAlarm(true);
            if (statusHint != null)
            {
                statusHint.Show();
            }

            if (lockBehind != null)
            {
                for (int i = 0; i < lockBehind.Length; i++)
                {
                    if (lockBehind[i] != null)
                    {
                        lockBehind[i].Close();
                        lockBehind[i].Lock();
                    }
                }
            }

            if (gates != null)
            {
                for (int i = 0; i < gates.Length; i++)
                {
                    if (gates[i] != null)
                    {
                        gates[i].ForceOpen(gateOpenDuration);
                    }
                }
            }

            int nextWave = 0;
            while (elapsed < gateOpenDuration)
            {
                while (waves != null && nextWave < waves.Length && elapsed >= waves[nextWave].startTime)
                {
                    StartCoroutine(SpawnWave(waves[nextWave]));
                    nextWave++;
                }

                elapsed += Time.deltaTime;
                yield return null;
            }

            running = false;
            complete = true;
            SetAlarm(false);

            if (statusHint != null)
            {
                statusHint.Hide();
            }

            if (completeHint != null)
            {
                completeHint.Show();
            }

            if (unlockOnComplete != null)
            {
                for (int i = 0; i < unlockOnComplete.Length; i++)
                {
                    if (unlockOnComplete[i] != null)
                    {
                        unlockOnComplete[i].Unlock();
                        unlockOnComplete[i].ForceOpen();
                    }
                }
            }
        }

        private IEnumerator SpawnWave(Wave wave)
        {
            if (enemyPrefab == null || wavePoints == null || wavePoints.Length == 0)
            {
                yield break;
            }

            for (int n = 0; n < wave.count; n++)
            {
                alive.RemoveAll(go => go == null);
                if (alive.Count >= maxAlive)
                {
                    yield break;
                }

                Transform point = PickPoint(wave, n);
                if (point != null)
                {
                    Spawn(point.position);
                }

                yield return new WaitForSeconds(spawnSpacing);
            }
        }

        private Transform PickPoint(Wave wave, int n)
        {
            if (wave.pointIndices != null && wave.pointIndices.Length > 0)
            {
                int idx = wave.pointIndices[n % wave.pointIndices.Length];
                if (idx >= 0 && idx < wavePoints.Length)
                {
                    return wavePoints[idx];
                }
            }

            return wavePoints[Random.Range(0, wavePoints.Length)];
        }

        private void Spawn(Vector3 position)
        {
            Vector3 jitter = (Vector3)(Random.insideUnitCircle * 0.4f);
            GameObject mob = Instantiate(enemyPrefab, position + jitter, Quaternion.identity, waveGroup);
            mob.name = enemyPrefab.name + "_Wave";
            LabMobUtil.SetAggroRadius(mob, aggroRadius);
            alive.Add(mob);

            if (spawnEffectPrefab != null)
            {
                GameObject fx = Instantiate(spawnEffectPrefab, position, Quaternion.identity);
                fx.SetActive(true);
            }
        }

        private void SetAlarm(bool on)
        {
            if (alarmLights == null)
            {
                return;
            }

            for (int i = 0; i < alarmLights.Length; i++)
            {
                if (alarmLights[i] != null)
                {
                    alarmLights[i].enabled = on;
                }
            }
        }
    }
}
