using UnityEngine;

public class Bullet : MonoBehaviour
{
    [SerializeField] private float speed = 12f;
    [SerializeField] private float lifeTime = 2f;
    [SerializeField] private float damage = 7f;

    private void Start()
    {
        Destroy(gameObject, lifeTime);
    }


    /* 충돌한 오브젝트 코드가 "Monster"면 로그 출력하고 삭제되는 코드 */
    //private void OnCollisionEnter2D(Collision2D collision)
    //{
    //    if (collision.gameObject.CompareTag("Monster"))
    //    {
    //        Debug.Log("Hit!");      // 디버그용 로그 출력
    //        Destroy(gameObject);    // 오브젝트 삭제
    //    }
    //}
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (collision.gameObject.CompareTag("Monster"))
        {
            Debug.Log("Hit!");      // 디버그용 로그 출력
            Destroy(gameObject);    // 오브젝트 삭제
        }
        // 벽 / 닫힌 문 / 큰 고정 구조물(Wall 레이어)에 맞으면 총알 제거 (총알 벽 관통 수정)
        else if (IsBlocker(collision))
        {
            Destroy(gameObject);
        }
    }

    // Wall 레이어의 (트리거가 아닌) 콜라이더 = 총알을 막는 오브젝트
    private static int wallLayer = -2;
    private static bool IsBlocker(Collider2D col)
    {
        if (wallLayer == -2) wallLayer = LayerMask.NameToLayer("Wall");
        return wallLayer >= 0 && !col.isTrigger && col.gameObject.layer == wallLayer;
    }

    private void Update()
    {
        transform.Translate(Vector2.right * speed * Time.deltaTime);
    }
    public float GetDamage()
    {
        return damage;
    }
}