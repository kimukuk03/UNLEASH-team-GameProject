using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MuzzleFlash : MonoBehaviour
{
    private void Start()
    {
        Destroy(gameObject, 0.07f);
    }
}