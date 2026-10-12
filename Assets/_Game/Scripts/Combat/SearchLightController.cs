using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class SearchLightController : MonoBehaviour
{
    [SerializeField] private Transform lightPivot;
    [SerializeField] private Light2D searchLight;
    [SerializeField] private Transform interactPoint;

    private bool playerInRange = false;
    private bool isControlling = false;

    private GameObject player;
    private PlayerMover playerMover;
    private Camera mainCam;

    private void Start()
    {
        mainCam = Camera.main;

        if (searchLight != null)
        {
            searchLight.enabled = false;
        }
    }

    private void Update()
    {
        if (playerInRange && Input.GetKeyDown(KeyCode.E))
        {
            if (!isControlling)
            {
                StartControl();
            }
            else
            {
                StopControl();
            }
        }

        if (isControlling)
        {
            RotateLightToMouse();
        }
    }

    private void StartControl()
    {
        isControlling = true;

        if (searchLight != null)
            searchLight.enabled = true;

        if (player != null)
        {
            player.transform.position = interactPoint.position;

            playerMover = player.GetComponent<PlayerMover>();

            if (playerMover != null)
                playerMover.enabled = false;
        }
    }

    private void StopControl()
    {
        isControlling = false;

        Debug.Log("등명기 조작 종료");

        if (playerMover != null)
            playerMover.enabled = true;
    }

    private void RotateLightToMouse()
    {
        Vector3 mousePos = mainCam.ScreenToWorldPoint(Input.mousePosition);
        mousePos.z = 0f;

        Vector2 direction = mousePos - lightPivot.position;

        float angle =
            Mathf.Atan2(direction.y, direction.x)
            * Mathf.Rad2Deg;

        lightPivot.rotation =
            Quaternion.Euler(0f, 0f, angle - 90f);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = true;
            player = other.gameObject;
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.CompareTag("Player"))
        {
            playerInRange = false;
            player = null;
        }
    }
}