using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float speed = 3f;

    void LateUpdate()
    {
        Vector3 targetPos = new Vector3(target.position.x, target.position.y, -10f);

        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * speed);
    }
}
