using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Script_Muzzle : MonoBehaviour
{
    public float m_armLength;
    private Monster parent = null;

    // Start is called before the first frame update
    void Start()
    {
        parent = GetComponentInParent<Monster>();

    }

    // Update is called once per frame
    void Update()
    {
        transform.localPosition= parent.m_direction;
        transform.rotation = parent.transform.rotation;
    }
}
