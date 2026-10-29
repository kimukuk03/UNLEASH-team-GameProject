using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Script_TestMob_Rigdbody : MonoBehaviour
{
    [SerializeField] private Rigidbody2D m_rigdollBody2D;
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

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Bullet"))
        {
            Bullet bullet = other.GetComponent<Bullet>();

            parent.WhenHit(bullet.GetDamage());
        }
    }

}
