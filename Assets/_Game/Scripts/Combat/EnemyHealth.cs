using UnityEngine;

public class EnemyHealth : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float shadowArmor = 100f;

    private float currentHealth;

    public bool HasShadow
    {
        get { return shadowArmor > 0f; }
    }

    private void Start()
    {
        currentHealth = maxHealth;
    }

    public void TakeBulletDamage(float damage)
    {
        if (HasShadow)
        {
            Debug.Log(gameObject.name + " : 그림자가 남아 있어서 총알 무효");
            return;
        }

        currentHealth -= damage;

        Debug.Log(
            gameObject.name +
            " HP: " +
            currentHealth +
            " / " +
            maxHealth
        );

        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    public void TakeLightDamage(float amount)
    {
        if (shadowArmor <= 0f)
            return;

        shadowArmor -= amount;

        if (shadowArmor < 0f)
            shadowArmor = 0f;

        Debug.Log(
            gameObject.name +
            " 그림자: " +
            shadowArmor
        );

        if (shadowArmor <= 0f)
        {
            Debug.Log(gameObject.name + " 그림자 제거!");
        }
    }

    private void Die()
    {
        Debug.Log(gameObject.name + " 처치!");
        Destroy(gameObject);
    }
}