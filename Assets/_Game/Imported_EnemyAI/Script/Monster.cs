using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;



public class Monster : MonoBehaviour
{
    public float speed = 3f;
    public Vector3 m_direction;
    //public Vector3 m_playerLocation;
    private Rigidbody2D m_rigdollBody2D;
    private MONSTERSTATE m_state;

    private bool m_isFacedBack = false;
    void Awake()
    {
        m_state = MONSTERSTATE.IDLE;
        //m_rigdollBody2D = GetComponent<Rigidbody2D>();
    }
    void Start()
    {
        m_rigdollBody2D = GetComponent<Rigidbody2D>();

        MobController mobController = FindFirstObjectByType<MobController>();
        mobController.AddMonster(this);
    }

    void Update()
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
            case (MONSTERSTATE.DEAD):
                {
                    StateDead();
                    break;
                }
        }
    }

    //public void HandlePlayerLocation(Vector3 targetPosition)
    //{
    //    m_playerLocation = targetPosition;

    //    SetDirection();
    //}
    public void HandlePlayerLocation(Vector3 targetPosition)
    {
        Vector3 playerLocation = targetPosition;

        SetDirectionToPlayer(playerLocation);
    }

    // 그냥 평상시
    void StateIDLE()
    {

    }
    // 플레이어 감지, 추격.
    void StateChase()
    {
        SetIsFacedBack();

        Move();
    }
    void StateDead()
    {
        Destroy(gameObject);    // 컴포넌트가 터져서 죽습니다. 터져서 없어짐 수고요.
    }

    void SetIsFacedBack()
    {
        if (0 > m_direction.x)
        {
            m_isFacedBack = true;
        }
        else
        {
            m_isFacedBack = false;
        }
    }
    // 플레이어 위치를 받으면 그 쪽으로 방향을 설정하는 메서드?
    void SetDirectionToPlayer(Vector3 playerLocation)
    {
        m_direction = (playerLocation - transform.position).normalized;
    }
    void Move()
    {
        m_rigdollBody2D.MovePosition(m_rigdollBody2D.position + (Vector2)m_direction * speed * Time.fixedDeltaTime);
    }

   public void SetStateToIDLE()
    {
        m_state = MONSTERSTATE.IDLE;
    }
    public void SetStateToChase()
    {
        m_state = MONSTERSTATE.CHASE;
    }
    public void SetStateToDead()
    {
        m_state = MONSTERSTATE.DEAD;
    }
}
