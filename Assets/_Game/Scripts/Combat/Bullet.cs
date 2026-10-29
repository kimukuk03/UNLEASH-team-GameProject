using UnityEngine;

public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 12f;
    [SerializeField] private float lifeTime = 2f;

    private void Start()
    {
        Destroy(gameObject, lifeTime);
    }


    /* 충돌한 오브젝트 코드가 "Monster"면 로그 출력하고 삭제되는 코드 */
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Monster"))
        {
            Debug.Log("Hit!");      // 디버그용 로그 출력
            Destroy(gameObject);    // 오브젝트 삭제
        }
    }


    private void Update()
    {
        transform.Translate(Vector2.right * speed * Time.deltaTime);
    }
}