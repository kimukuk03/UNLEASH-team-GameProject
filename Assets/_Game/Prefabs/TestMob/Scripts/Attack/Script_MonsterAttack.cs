using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Script_MonsterAttack : MonoBehaviour
{

    [SerializeReference] private bool isAttackAble = true;
    [SerializeReference] private float updateDelay = 0.00f;
    [SerializeReference] protected float m_damage = 7f;
    
    // 기타 멤버변수
    private int m_attackPhase = 0;
    private bool m_isAttacking = false;
    private bool m_isTargetInrange = false;
    // 부모
    private Script_Monster m_parent;
    // 자식
    private Script_MonsterAttack_Effect m_effect;
    private Script_MonsterAttack_AttackAbleRange m_attackRange;


    // Start is called before the first frame update
    void Start()
    {
        m_parent = GetComponentInParent<Script_Monster>();
        
        m_effect = GetComponentInChildren<Script_MonsterAttack_Effect>(); ;
        m_attackRange = GetComponentInChildren<Script_MonsterAttack_AttackAbleRange>();
    }
    public void Attack()
    { 
        if(m_parent.m_state==MONSTERSTATE.ATTACK && isAttackAble && !m_isAttacking)
        {
           AttackByPhase();
        }
    }

    IEnumerator AttackByPhase()
    {
        while (true) 
        {


            ++m_attackPhase;
            yield return new WaitForSeconds(updateDelay);
        } 
    }
    public bool GetIsTargetInrange()
    {
        return m_isTargetInrange;
    }

}
