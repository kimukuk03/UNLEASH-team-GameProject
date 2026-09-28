using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public class Flashlight : MonoBehaviour
{
    private Light2D flash;

    //플래시 회전
    private Camera mainCam;
    private Vector3 cursorPos;
    float zRotation;

    //private bool buffer = false;

    void Start()
    {
        flash = GetComponent<Light2D>();

        if (flash != null)
        {
            flash.enabled = false;
        }
        else
        {
            Debug.Log("플래시를 찾을 수 없습니다");
        }

        //플래시 회전
        mainCam = Camera.main;
    }

    private void ToggleFlash()
    {
        if (flash == null)
        {
            Debug.Log("플래시를 조작했지만 플래시가 없습니다");
            return;
        }

        Debug.Log("Toggle Flash");
        flash.enabled = !flash.enabled;
    }

    // Update is called once per frame
    void Update()
    {
        //E키로 플래시 토글
        if (Input.GetKeyDown(KeyCode.F))
        {
            ToggleFlash();
        }

        //플래시 회전
        cursorPos = Input.mousePosition;
        cursorPos.z = transform.position.z - mainCam.transform.position.z;
        cursorPos = mainCam.ScreenToWorldPoint(cursorPos);
        Vector2 gunPoint = cursorPos - transform.position;
        zRotation = Mathf.Atan2(gunPoint.y, gunPoint.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, zRotation - 90f);
    }
}