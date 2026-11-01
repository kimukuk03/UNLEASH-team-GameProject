using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Script_MonsterAttack_AttackAbleRange : MonoBehaviour
{
    private Script_Monster parent;
    private bool isPlayerInAttackAbleRange = false;
    // Start is called before the first frame update
    void Start()
    {
        parent = GetComponentInParent<Script_Monster>();
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInAttackAbleRange = true;
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInAttackAbleRange = false;
        }
    }

    public bool GetIsPlayerInDetectRange()
    {
        return isPlayerInAttackAbleRange;
    }
}
