using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameDirector : MonoBehaviour
{
    private void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player");
        monsters = GameObject.FindGameObjectsWithTag("Monster");
    }
    void Update()
    {
        targetPosition = player.transform.position;
        targetPosition.z = 0;

        foreach (GameObject monster in monsters)
        {
            Monster monsterComponent = monster.GetComponent<Monster>();
            monsterComponent.SetDirection(targetPosition);
        }
        
    }
    private GameObject player;
    private Vector3 targetPosition;
    private GameObject[] monsters;
}