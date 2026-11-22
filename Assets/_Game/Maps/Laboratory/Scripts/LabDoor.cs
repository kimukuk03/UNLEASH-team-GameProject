using System;
using UnityEngine;

namespace Unleash.Laboratory
{
    public enum LabDoorState
    {
        Closed,
        Opening,
        Open,
        Closing,
    }

    /// <summary>
    /// 연구소 문. Animator(Closed / Opening / Open / Closing 4상태)로 스프라이트 애니메이션을 재생하고,
    /// 닫혀 있는 동안에는 Wall 레이어의 BoxCollider2D 로 플레이어/적/총알을 막는다.
    ///
    /// Animator 파라미터
    ///   IsOpen    (bool)  : true → Opening → Open, false → Closing → Closed
    ///   AnimSpeed (float) : Opening/Closing 클립 재생 속도 배율 (openDuration 으로 자동 계산)
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class LabDoor : MonoBehaviour
    {
        private static readonly int IsOpenHash = Animator.StringToHash("IsOpen");
        private static readonly int AnimSpeedHash = Animator.StringToHash("AnimSpeed");

        [Header("상태")]
        [SerializeField] private bool startOpen = false;
        [Tooltip("잠겨 있으면 플레이어가 다가가도 열리지 않는다. 스크립트(Unlock/ForceOpen)로만 연다.")]
        [SerializeField] private bool locked = false;
        [Tooltip("플레이어가 센서 범위에 들어오면 자동으로 연다.")]
        [SerializeField] private bool autoOpenOnPlayer = true;
        [Tooltip("플레이어가 센서 밖으로 나가면 자동으로 닫는다.")]
        [SerializeField] private bool autoCloseOnPlayerExit = false;

        [Header("연출")]
        [Tooltip("열리는 데 걸리는 시간(초). 보스방 셔터처럼 천천히 열리게 하려면 크게.")]
        [SerializeField] private float openDuration = 0.6f;
        [SerializeField] private float closeDuration = 0.6f;
        [Tooltip("Opening 진행도가 이 값을 넘으면 충돌이 꺼진다(통과 가능).")]
        [Range(0f, 1f)] [SerializeField] private float passableAt = 0.75f;
        [Tooltip("Opening/Closing 클립의 원래 길이(초). 빌더가 자동으로 채운다.")]
        [SerializeField] private float clipLength = 1f;

        [Header("참조")]
        [SerializeField] private Collider2D blockingCollider;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip openSound;
        [SerializeField] private AudioClip closeSound;

        public LabDoorState State { get; private set; }
        public bool IsLocked { get { return locked; } }
        public float Progress { get; private set; }

        /// <summary>문 상태가 바뀔 때마다 호출된다 (확장용).</summary>
        public event Action<LabDoor, LabDoorState> StateChanged;

        private Animator animator;
        private float timer;
        private int playersInside;
        private bool pendingClose;
        private Vector2 doorwayCenter;
        private Vector2 doorwaySize = Vector2.one;

        private void Awake()
        {
            animator = GetComponent<Animator>();
            if (blockingCollider == null)
            {
                blockingCollider = GetComponent<Collider2D>();
            }

            // 비활성 콜라이더는 bounds 가 비어 있으므로 문 영역을 미리 계산해 둔다.
            BoxCollider2D box = blockingCollider as BoxCollider2D;
            if (box != null)
            {
                doorwayCenter = box.transform.TransformPoint(box.offset);
                Vector3 s = box.transform.lossyScale;
                doorwaySize = new Vector2(Mathf.Abs(box.size.x * s.x), Mathf.Abs(box.size.y * s.y));
            }
            else
            {
                doorwayCenter = transform.position;
            }

            if (startOpen)
            {
                SetState(LabDoorState.Open);
                Progress = 1f;
                animator.SetBool(IsOpenHash, true);
                animator.Play("Open", 0, 0f);
            }
            else
            {
                SetState(LabDoorState.Closed);
                Progress = 0f;
            }

            ApplyCollider();
        }

        private void Update()
        {
            switch (State)
            {
                case LabDoorState.Opening:
                    timer += Time.deltaTime;
                    Progress = openDuration <= 0f ? 1f : Mathf.Clamp01(timer / openDuration);
                    if (Progress >= 1f)
                    {
                        SetState(LabDoorState.Open);
                    }
                    break;

                case LabDoorState.Closing:
                    timer += Time.deltaTime;
                    float t = closeDuration <= 0f ? 1f : Mathf.Clamp01(timer / closeDuration);
                    Progress = 1f - t;
                    if (t >= 1f)
                    {
                        SetState(LabDoorState.Closed);
                    }
                    break;

                case LabDoorState.Open:
                    if (pendingClose && !IsDoorwayOccupied())
                    {
                        pendingClose = false;
                        BeginClose();
                    }
                    break;
            }

            ApplyCollider();
        }

        // ───────────── 외부 API ─────────────

        /// <summary>잠겨 있지 않으면 연다.</summary>
        public void Open()
        {
            if (locked)
            {
                return;
            }

            ForceOpen();
        }

        /// <summary>잠금 여부와 상관없이 연다 (이벤트/스크립트 전용).</summary>
        public void ForceOpen()
        {
            pendingClose = false;
            if (State == LabDoorState.Open || State == LabDoorState.Opening)
            {
                return;
            }

            // Closing 도중이면 현재 진행도에서 이어서 연다.
            timer = Progress * openDuration;
            SetAnimSpeed(openDuration);
            animator.SetBool(IsOpenHash, true);
            SetState(LabDoorState.Opening);
            PlaySound(openSound);
        }

        /// <summary>
        /// 열리는 시간을 지정해서 연다. (디펜스 구간 보스 셔터처럼 아주 천천히 열 때)
        /// </summary>
        public void ForceOpen(float duration)
        {
            openDuration = Mathf.Max(0.05f, duration);
            ForceOpen();
        }

        /// <summary>닫는다. 문 안에 플레이어가 서 있으면 빠져나갈 때까지 기다렸다가 닫는다.</summary>
        public void Close()
        {
            if (State == LabDoorState.Closed || State == LabDoorState.Closing)
            {
                return;
            }

            if (IsDoorwayOccupied())
            {
                pendingClose = true;
                return;
            }

            BeginClose();
        }

        public void Lock()
        {
            locked = true;
        }

        public void Unlock()
        {
            locked = false;
            if (autoOpenOnPlayer && playersInside > 0)
            {
                Open();
            }
        }

        // ───────────── 센서 (LabDoorSensor 가 호출) ─────────────

        public void OnSensorEnter()
        {
            playersInside++;
            pendingClose = false;
            if (autoOpenOnPlayer)
            {
                Open();
            }
        }

        public void OnSensorExit()
        {
            playersInside = Mathf.Max(0, playersInside - 1);
            if (autoCloseOnPlayerExit && playersInside == 0)
            {
                Close();
            }
        }

        // ───────────── 내부 ─────────────

        private void BeginClose()
        {
            timer = (1f - Progress) * closeDuration;
            SetAnimSpeed(closeDuration);
            animator.SetBool(IsOpenHash, false);
            SetState(LabDoorState.Closing);
            PlaySound(closeSound);
        }

        private void SetAnimSpeed(float duration)
        {
            float speed = duration <= 0f ? 10f : clipLength / duration;
            animator.SetFloat(AnimSpeedHash, speed);
        }

        private void SetState(LabDoorState next)
        {
            State = next;
            if (next == LabDoorState.Open || next == LabDoorState.Closed)
            {
                timer = 0f;
                Progress = next == LabDoorState.Open ? 1f : 0f;
            }

            if (StateChanged != null)
            {
                StateChanged(this, next);
            }
        }

        private void ApplyCollider()
        {
            if (blockingCollider == null)
            {
                return;
            }

            bool block;
            switch (State)
            {
                case LabDoorState.Open:
                    block = false;
                    break;
                case LabDoorState.Opening:
                    block = Progress < passableAt;
                    break;
                case LabDoorState.Closing:
                    block = Progress < passableAt;
                    break;
                default:
                    block = true;
                    break;
            }

            if (blockingCollider.enabled != block)
            {
                blockingCollider.enabled = block;
            }
        }

        private bool IsDoorwayOccupied()
        {
            Collider2D[] hits = Physics2D.OverlapBoxAll(doorwayCenter, doorwaySize, 0f);
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i] != null && hits[i].CompareTag("Player"))
                {
                    return true;
                }
            }

            return false;
        }

        private void PlaySound(AudioClip clip)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip);
            }
        }
    }
}
