using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;



public class Monster : MonoBehaviour
{
    [SerializeField] protected int maxHp = 100;
    [SerializeField] private int hp;
    // 각종 기하 관련 변수입니다.
    public Vector3 m_direction;
    public float standardSpeed = 3f;   
    private float speed;
    // 현재 상태에 따라 각종 행동이 제어됩니다.
    public MONSTERSTATE m_state;
    // update 간격이라고 생각하시면 됩니다.
    public float updateDelay = 0.1f;




    //public Vector3 m_playerLocation;
    //private bool m_isFacedBack = false;
    //      자식들 변수입니다.
    [SerializeField] private Rigidbody2D m_rigdboby;
    [SerializeField] MobController mobController;
    [SerializeField] private DetectRange detectRange;
    [SerializeField] private Script_Testmob_AttackRange attackRange;
    [SerializeField] private Script_TestMob_Sprite sprite;
    //      핵심 기능들입니다.
    void Awake()
    {
        m_state = MONSTERSTATE.IDLE;
        //m_rigdboby = GetComponent<Rigidbody2D>();
    }
    void Start()
    {
        hp = maxHp;
        speed = standardSpeed;

        mobController = GetComponentInParent<MobController>();
        m_rigdboby = GetComponent<Rigidbody2D>();
        detectRange = GetComponentInChildren<DetectRange>();
        attackRange = GetComponentInChildren<Script_Testmob_AttackRange>();
        sprite = GetComponentInChildren<Script_TestMob_Sprite>();

        mobController.AddMonster(this);

        StartCoroutine(MonsterUpdate());
    }
    void Update()
    {
    }
    // update 대용으로 쓰는 느낌의 함수?
    // while을 반복하지만 명령 실행에 updateDelay만큼 대기하고 실행합니다.
    // sleep과는 다르게 대기 중에도 다른 기능은 정상적으로 작동합니다.
    IEnumerator MonsterUpdate()
    {
        while (true)
        {
            // 어떤 상태로 할 지 결정.
            DecideState();
            // 어떤 상태인가에 따라 행동.
            ActionByState();

            yield return new WaitForSeconds(updateDelay);
        }
    }

    // =====
    //      현재 상태와 관련된 함수들.
    private void DecideState()
    {
        // 안죽었을 경우에만 상태 재정의.
        // 1.공격범위 내라면 공격
        // 2. 아니고 감지범위 내라면 추적.
        // 3. 그것도 아니면 대기.
        if(m_state != MONSTERSTATE.DEAD)
        {
            if (attackRange.GetIsPlayerInAttackRange())
            {
                this.SetStateToAttack();
            }
            else if (detectRange.GetIsPlayerInDetectRange())
            {
                this.SetStateToChase();
            }
            else
            {
                this.SetStateToIDLE();
            }
        }
    }
    private void ActionByState()
    {
        switch (m_state)
        {
            case (MONSTERSTATE.IDLE):
                {
                    StateIDLE();
                    break;
                }
            case (MONSTERSTATE.CHASE):
                {
                    StateChase();
                    break;
                }
            case (MONSTERSTATE.ATTACK):
                {
                    StateAttack();
                    break;
                }
            case (MONSTERSTATE.DEAD):
                {
                    StateDead();
                    break;
                }
        }
    }
    // 대기상태
    void StateIDLE()
    {
        ResetVelocity();
    }
    // 플레이어 감지, 추격,이동 상태.
    void StateChase()
    {
        //SetIsFacedBack();

        Move();
    }
    void StateAttack()
    {
        ResetVelocity();
    }
    void StateDead()
    {
        mobController.RemoveMonster(this);
        Destroy(gameObject);    // 컴포넌트가 터져서 죽습니다. 터져서 없어짐 수고요.
    }
     private void SetStateToIDLE()
    {
        m_state = MONSTERSTATE.IDLE;
    }
    private void SetStateToChase()
    {
        m_state = MONSTERSTATE.CHASE;
    }
    private void SetStateToAttack()
    {
        m_state = MONSTERSTATE.ATTACK;
    }
    private void SetStateToDead()
    {
        m_state = MONSTERSTATE.DEAD;
    }  
    //      기하와 관련된 함수.
    private void Move()
    {
        m_rigdboby.MovePosition(m_rigdboby.position + (Vector2)m_direction * speed * Time.fixedDeltaTime);
    }
    private void ResetVelocity()
    {
        m_rigdboby.velocity = Vector3.zero;
    }
    // 플레이어 위치를 받으면 그 쪽으로 방향을 설정하는 메서드?
    void SetDirectionToPlayer(Vector3 playerLocation)
    {
        Vector3 gap = playerLocation - transform.position;

        m_direction = (gap).normalized;

        sprite.SetSpriteDirection(gap.x);
    }
    //      체력, 속도 등의 수치를 제어하는 함수.
    // '공격당했을 때'
    // 사망 여부를 반환합니다.

    private float WhenDamaged(float damage)
    {

    }

    //      이 개체와 그 자식에서는 쓰이지 않는 함수.
    public void HandlePlayerLocation(Vector3 targetPosition)
    {
        SetDirectionToPlayer(targetPosition);
    }
    public bool WhenHit(float damage)
    {
        // 나중에 그림자 같은 이유로 피격되지 않을 수 있으니...
        if (true)
        {
            WhenDamaged(damage);
        }
    }
}
