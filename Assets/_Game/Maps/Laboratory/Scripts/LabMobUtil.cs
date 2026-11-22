using UnityEngine;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 기존 적 시스템(Monster / MobController / DetectRange)을 수정하지 않고 맵 쪽에서 다루기 위한 헬퍼.
    /// </summary>
    public static class LabMobUtil
    {
        /// <summary>
        /// root 아래 모든 적의 감지 범위(DetectRange 트리거 반경)를 worldRadius 로 바꾼다.
        /// 기본 프리팹은 반경 4 → 매복/웨이브 적은 멀리서도 플레이어를 쫓게 하려고 키운다.
        /// </summary>
        public static void SetAggroRadius(GameObject root, float worldRadius)
        {
            if (root == null || worldRadius <= 0f)
            {
                return;
            }

            DetectRange[] ranges = root.GetComponentsInChildren<DetectRange>(true);
            for (int i = 0; i < ranges.Length; i++)
            {
                SetAggroRadius(ranges[i], worldRadius);
            }
        }

        public static void SetAggroRadius(DetectRange range, float worldRadius)
        {
            if (range == null)
            {
                return;
            }

            CircleCollider2D circle = range.GetComponent<CircleCollider2D>();
            if (circle == null || circle.radius <= 0f)
            {
                return;
            }

            Transform parent = range.transform.parent;
            float parentScale = parent != null ? Mathf.Abs(parent.lossyScale.x) : 1f;
            if (parentScale <= 0f)
            {
                parentScale = 1f;
            }

            float local = worldRadius / (circle.radius * parentScale);
            range.transform.localScale = new Vector3(local, local, local);
        }

        private static Transform cachedPlayer;

        /// <summary>씬의 플레이어 루트 Transform (PlayerMover 기준).</summary>
        public static Transform FindPlayer()
        {
            if (cachedPlayer == null)
            {
                PlayerMover mover = Object.FindObjectOfType<PlayerMover>();
                if (mover != null)
                {
                    cachedPlayer = mover.transform;
                }
            }

            return cachedPlayer;
        }
    }
}
