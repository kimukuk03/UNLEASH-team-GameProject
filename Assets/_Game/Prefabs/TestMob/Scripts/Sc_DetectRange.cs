using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DetectRange : MonoBehaviour
{
    private Monster parent; 
    // Start is called before the first frame update
    void Start()
    {
        parent = GetComponentInParent<Monster>();
    }

    //// Update is called once per frame
    //void Update()
    //{
        
    //}

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            parent.SetStateToChase();
        }
    }
    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            parent.SetStateToIDLE();

        }
    }
}
