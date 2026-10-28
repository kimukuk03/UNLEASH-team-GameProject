using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;



public class Monster : MonoBehaviour
{
    protected int maxHp = 100;
    protected int hp = 100;

    public float updateDelay = 0.3f;
    public float standardSpeed = 3f;   
    public Vector3 m_direction;

    private float speed;




    //public Vector3 m_playerLocation;
    private Rigidbody2D m_rigdboby;
    public MONSTERSTATE m_state;
    //private bool m_isFacedBack = false;

    MobController mobController;
    private DetectRange detectRange;
    private Script_Testmob_AttackRange attackRange;
    private Script_TestMob_Sprite sprite;
    void Awake()
    {
        m_state = MONSTERSTATE.IDLE;
        //m_rigdboby = GetComponent<Rigidbody2D>();
    }
    void Start()
    {
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



    // mob controller에서 작동하면 플레이어의 위치를 가져옵니다.
    // 모든 몬스터 개체에서 플레이어 위치를 구하는 것 보다는 효율적일 것이라 생각했었습니다.
    // 아무튼 위치를 가져오면 다음 이동 방향을 플레이어 방향으로 재설정 합니다.

    // 그냥 평상시
    void StateIDLE()
    {
        ResetVelocity();
    }
    // 플레이어 감지, 추격.
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
    public void Move()
    {
        m_rigdboby.MovePosition(m_rigdboby.position + (Vector2)m_direction * speed * Time.fixedDeltaTime);
    }
    private void ResetVelocity()
    {
        m_rigdboby.velocity = Vector3.zero;
    }
    //void SetIsFacedBack()
    //{
    //    if (0 > m_direction.x)
    //    {
    //        m_isFacedBack = true;
    //    }
    //    else
    //    {
    //        m_isFacedBack = false;
    //    }
    //}
    // 플레이어 위치를 받으면 그 쪽으로 방향을 설정하는 메서드?
    void SetDirectionToPlayer(Vector3 playerLocation)
    {
        Vector3 gap = playerLocation - transform.position;

        m_direction = (gap).normalized;

        sprite.SetSpriteDirection(gap.x);
        
    }


   public void SetStateToIDLE()
    {
       // Debug.Log("State : IDLE");
        m_state = MONSTERSTATE.IDLE;
    }
    public void SetStateToChase()
    {
      //  Debug.Log("State : Chase");

        m_state = MONSTERSTATE.CHASE;
    }
    public void SetStateToAttack()
    {

       // Debug.Log("State : Attack");

        m_state = MONSTERSTATE.ATTACK;
    }
    public void SetStateToDead()
    {
       // Debug.Log("State : Dead");

        m_state = MONSTERSTATE.DEAD;
    }  
    public void HandlePlayerLocation(Vector3 targetPosition)
    {
        SetDirectionToPlayer(targetPosition);
    }

}
