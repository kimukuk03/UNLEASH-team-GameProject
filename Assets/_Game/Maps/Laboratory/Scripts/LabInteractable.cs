using UnityEngine;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 플레이어가 트리거 범위 안에서 E 키를 누르면 Interact() 가 호출되는 베이스 클래스.
    /// (등명기 SearchLightController 도 E 키를 쓰므로, 연구소 맵에서는 등명기 근처에 두지 않는다.)
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public abstract class LabInteractable : MonoBehaviour
    {
        [SerializeField] protected KeyCode interactKey = KeyCode.E;
        [SerializeField] protected LabHint prompt;
        [SerializeField] protected bool singleUse = true;

        protected bool playerInRange;
        protected bool used;

        protected virtual void Update()
        {
            if (playerInRange && !used && Input.GetKeyDown(interactKey))
            {
                if (singleUse)
                {
                    used = true;
                    if (prompt != null)
                    {
                        prompt.Hide();
                    }
                }

                Interact();
            }
        }

        protected abstract void Interact();

        protected virtual void OnTriggerEnter2D(Collider2D other)
        {
            if (!other.CompareTag("Player"))
            {
                return;
            }

            playerInRange = true;
            if (prompt != null && !used)
            {
                prompt.Show();
            }
        }

        protected virtual void OnTriggerExit2D(Collider2D other)
        {
            if (!other.CompareTag("Player"))
            {
                return;
            }

            playerInRange = false;
            if (prompt != null)
            {
                prompt.Hide();
            }
        }
    }
}
