using System.Collections;
using UnityEngine;

public class GunController : MonoBehaviour
{
    [SerializeField] private GameObject muzzlePoint;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private GameObject muzzleFlashPrefab;

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Fire();
        }
    }

    private void Fire()
    {
        Instantiate(
            bulletPrefab,
            muzzlePoint.transform.TransformPoint(0.05f, 0.2f, 0f),
            muzzlePoint.transform.rotation
        );

        Instantiate(
            muzzleFlashPrefab,
            muzzlePoint.transform.TransformPoint(0.05f, 0.2f, 0f),
            Quaternion.FromToRotation(-Vector3.right, muzzlePoint.transform.up),
            muzzlePoint.transform
        );

    }
}