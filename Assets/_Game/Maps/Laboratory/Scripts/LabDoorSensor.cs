using UnityEngine;

namespace Unleash.Laboratory
{
    /// <summary>문 앞 트리거 영역. 플레이어(태그 "Player")가 들어오고 나가는 것을 LabDoor 에 알린다.</summary>
    [RequireComponent(typeof(Collider2D))]
    public class LabDoorSensor : MonoBehaviour
    {
        [SerializeField] private LabDoor door;

        private void Awake()
        {
            if (door == null)
            {
                door = GetComponentInParent<LabDoor>();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (door != null && other.CompareTag("Player"))
            {
                door.OnSensorEnter();
            }
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            if (door != null && other.CompareTag("Player"))
            {
                door.OnSensorExit();
            }
        }
    }
}
