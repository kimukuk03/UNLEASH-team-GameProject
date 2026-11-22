using UnityEngine;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 보스방 입구 직전 종료 지점. 지금은 보스맵이 없으므로 안내 문구만 띄운다.
    /// 보스맵이 생기면 여기서 씬 전환(SceneManager.LoadScene) 등을 연결하면 된다.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class LabLevelEnd : MonoBehaviour
    {
        [SerializeField] private LabHint endHint;

        private bool reached;

        public bool Reached { get { return reached; } }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (reached || !other.CompareTag("Player"))
            {
                return;
            }

            reached = true;
            if (endHint != null)
            {
                endHint.Show();
            }

            Debug.Log("[Laboratory] 보스방 입구 도착 - 프로토타입 구간 종료");
        }
    }
}
