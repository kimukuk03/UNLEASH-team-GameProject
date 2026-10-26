using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Script_Testmob_AttackRange : MonoBehaviour
{
    private Monster parent;
    private bool isPlayerInAttackRange = false;
    // Start is called before the first frame update
    void Start()
    {
        parent = GetComponentInParent<Monster>();
    }
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInAttackRange = true;
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInAttackRange = false;
        }
    }

    public bool GetIsPlayerInAttackRange()
    {
        return isPlayerInAttackRange;
    }
}
