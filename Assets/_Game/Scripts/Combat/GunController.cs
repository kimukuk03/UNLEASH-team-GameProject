using UnityEngine;

public class GunController : MonoBehaviour
{
    [SerializeField] private Transform muzzlePoint;
    [SerializeField] private GameObject bulletPrefab;

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
            muzzlePoint.position,
            muzzlePoint.rotation
        );
    }
}