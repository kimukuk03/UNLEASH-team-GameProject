using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

public class MobController : MonoBehaviour
{
    //public float firstRepeatDelay = 0.0f;
    public float updateDelay = 0.1f;
    
    private GameObject player;
    private List<Monster> monsters = new();



    void Start()
    {
        player = GameObject.FindGameObjectWithTag("Player");
        //InvokeRepeating("SetMonstersDirection", firstRepeatDelay, updateDelay);
        
        // player = FindFirstObjectByType<GameObject>();
        StartCoroutine("MobControllerUpdate");
    }
    void Update()
    {
    }

    IEnumerator MobControllerUpdate()
    {
        while (true)
        {
            SetMonstersDirection();

            yield return new WaitForSeconds(updateDelay);
        }
    }
    public void AddMonster(Monster monster)
    {
        monsters.Add(monster);
    }
    public void RemoveMonster(Monster monster)
    {
        monsters.Remove(monster);
    }
    private Vector3 GetPlayerPosition()
    {
        Vector3 playerPosition = player.transform.position;
        playerPosition.z = 0;
        return playerPosition;
    }
    private void SetMonstersDirection()
    {
        Vector3 targetPosition = GetPlayerPosition();

        foreach (Monster monster in monsters)
        {
            if (monster.m_state != MONSTERSTATE.IDLE)
            {
                monster.HandlePlayerLocation(targetPosition);
            }
        }
    }
}