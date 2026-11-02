using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerAim : MonoBehaviour
{
    [SerializeField] private Transform weaponPivot;
    [SerializeField] private SpriteRenderer weaponSprite;
    [SerializeField] private GameObject muzzlePoint;

    private void Update()
    {
        Vector3 mousePosition = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        mousePosition.z = 0f;

        Vector2 direction = mousePosition - weaponPivot.position;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;

        weaponPivot.rotation = Quaternion.Euler(0f, 0f, angle);

        if (direction.x < 0f)
        {
            weaponSprite.flipY = true;
            muzzlePoint.transform.localScale = new Vector3(1, -1, 1);
        }
        else
        {
            weaponSprite.flipY = false;
            muzzlePoint.transform.localScale = new Vector3(1, 1, 1);

        }
    }
}
