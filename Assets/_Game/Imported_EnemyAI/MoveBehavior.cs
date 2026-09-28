using System.Collections;
using System.Collections.Generic;
using UnityEngine;



public class Monster : MonoBehaviour
{
    public float speed = 7f;

    private Vector3 direction;
    private Rigidbody2D rb;
    public void MoveTo(Vector3 targetPosition)
    {
        direction = (targetPosition - transform.position).normalized;
    }

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    void Update()
    {
        rb.MovePosition(rb.position + (Vector2)direction * speed * Time.fixedDeltaTime);

        //Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        //mousePosition.z = 0;
        //MoveTo(mousePosition);
    }
}
