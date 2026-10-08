using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class MobController : MonoBehaviour
{
    private GameObject player;
    private List<Monster> monsters = new();

    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player");
        // player = FindFirstObjectByType<GameObject>();
    }
    void Update()
    {

        Vector3 targetPosition = GetPlayerPosition();

        SetMonstersDirection(targetPosition);
    }


    public void AddMonster(Monster monster)
    {
        monsters.Add(monster);
    }
    private Vector3 GetPlayerPosition()
    {
        Vector3 playerPosition = player.transform.position;
        playerPosition.z = 0;
        return playerPosition;
    }
    private void SetMonstersDirection(Vector3 targetPosition)
    {
        foreach (Monster monster in monsters)
        {
            monster.SetDirection(targetPosition);
        }
    }
}