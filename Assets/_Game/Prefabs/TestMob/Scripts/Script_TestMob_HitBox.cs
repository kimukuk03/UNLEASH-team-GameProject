using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Script_TestMob_Rigdbody : MonoBehaviour
{
    private Rigidbody2D m_rigdollBody2D;
    private Monster parent;
    // Start is called before the first frame update
    void Start()
    {
        parent = GetComponentInParent<Monster>();

        m_rigdollBody2D = GetComponent<Rigidbody2D>();

    }

    // Update is called once per frame
    void Update()
    {
        
    }

}
