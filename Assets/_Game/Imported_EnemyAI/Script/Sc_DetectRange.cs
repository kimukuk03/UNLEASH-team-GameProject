using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DetectRange : MonoBehaviour
{
    private Monster parent = null;  //부모 = 없음 엌ㅋㅋㅋ; 저도 하나 없어요, 그러니 오해하지 말아주세요.
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
