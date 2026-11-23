using UnityEngine;

namespace Unleash.Laboratory
{
    /// <summary>
    /// [비주얼 테스트] LabMob 외형을 정적 Front/Side/Back 이미지로 표시한다.
    ///
    /// - 기존 Script_TestMob_Sprite 는 그대로 동작한다. Monster 의 공격 타이밍이
    ///   그 스크립트의 프레임 카운터(GetCurrentFrame / m_stateChanged)에 의존하기 때문.
    /// - 기존 SpriteRenderer(Sprite 오브젝트)는 프리팹에서 꺼 두고, 이 컴포넌트가
    ///   별도 자식 렌더러(visual)에만 그린다. 두 스크립트가 같은 렌더러를 건드리지 않으므로
    ///   실행 순서와 무관하게 TestMob 프레임이 보이지 않는다.
    /// - 원복: 이 오브젝트(M2Visual)를 끄고 기존 SpriteRenderer 를 다시 켜면 된다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class LabMobDirectionalSprite : MonoBehaviour
    {
        [SerializeField] private Sprite front;
        [SerializeField] private Sprite side;
        [SerializeField] private Sprite back;

        [Tooltip("Side 원본 이미지가 왼쪽을 보고 있으면 true (오른쪽 이동 시 flipX).")]
        [SerializeField] private bool sideFacesLeft = true;

        [Tooltip("대각선 근처에서 방향이 깜빡이지 않도록 하는 여유 비율.")]
        [SerializeField, Range(0f, 0.5f)] private float hysteresis = 0.15f;

        private enum Facing { Front, Side, Back }

        private SpriteRenderer visual;
        private Monster monster;
        private Facing facing = Facing.Front;
        private bool faceRight;

        private void Awake()
        {
            visual = GetComponent<SpriteRenderer>();
            monster = GetComponentInParent<Monster>();
        }

        private void LateUpdate()
        {
            // AI 가 꺼진 오브젝트(VisualTest 등)는 배치된 스프라이트를 그대로 둔다.
            if (monster == null || !monster.isActiveAndEnabled)
            {
                return;
            }

            if (UpdateFacing(monster.m_direction))
            {
                Apply();
            }
        }

        private bool UpdateFacing(Vector3 dir)
        {
            float ax = Mathf.Abs(dir.x);
            float ay = Mathf.Abs(dir.y);
            if (ax < 0.0001f && ay < 0.0001f)
            {
                return false; // 아직 방향 정보 없음 → 이전 방향 유지
            }

            bool currentlySide = facing == Facing.Side;
            bool useSide = currentlySide ? ax * (1f + hysteresis) >= ay : ax > ay * (1f + hysteresis);

            if (useSide)
            {
                facing = Facing.Side;
                faceRight = dir.x > 0f;
            }
            else
            {
                // m_direction 은 "적 → 플레이어" 방향. 플레이어가 아래에 있으면 Front.
                facing = dir.y < 0f ? Facing.Front : Facing.Back;
            }

            return true;
        }

        private void Apply()
        {
            Sprite target = facing == Facing.Front ? front : facing == Facing.Back ? back : side;
            if (target != null && visual.sprite != target)
            {
                visual.sprite = target;
            }

            visual.flipX = facing == Facing.Side && (sideFacesLeft ? faceRight : !faceRight);
        }
    }
}
