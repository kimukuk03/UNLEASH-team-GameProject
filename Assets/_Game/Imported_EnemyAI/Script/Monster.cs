using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;



public class Monster : MonoBehaviour
{
    public float speed = 3f;

    private Vector3 direction;
    private Rigidbody2D rb;

    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        //GameObject go_mobController = GameObject.FindGameObjectWithTag("MobController");
        //MobController mobController = go_mobController.GetComponent<MobController>();
        MobController mobController = FindFirstObjectByType<MobController>();
        mobController.AddMonster(this);
    }

    void Update()
    {
        rb.MovePosition(rb.position + (Vector2)direction * speed * Time.fixedDeltaTime);
    }
    public void SetDirection(Vector3 targetPosition)
    {
        direction = (targetPosition - transform.position).normalized;
    }
}
