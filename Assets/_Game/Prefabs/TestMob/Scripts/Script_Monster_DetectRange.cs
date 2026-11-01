using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Script_Monster_DetectRange : MonoBehaviour
{
    private Script_Monster parent; 
    private bool isPlayerInDetectRange = false;
    // Start is called before the first frame update
    void Start()
    {
        parent = GetComponentInParent<Script_Monster>();
    }

    //// Update is called once per frame
    //void Update()
    //{
        
    //}

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInDetectRange = true;
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            isPlayerInDetectRange = false;
        }
    }
    public bool GetIsPlayerInDetectRange()
    {
        return isPlayerInDetectRange;
    }

}
