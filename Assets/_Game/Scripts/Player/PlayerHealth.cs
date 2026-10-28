
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    // 최대 체력
    public float maxHealth = 100f;

    // 현재 체력
    public float currentHealth;

    // 체력바 이미지
    public Image healthBarFill;

    void Start()
    {
        // 게임 시작 시 체력을 최대로 설정
        currentHealth = maxHealth;

        // 체력바 갱신
        UpdateHealthBar();
    }

    void Update()
    {
        // 테스트용: K 키를 누르면 체력 10 감소
        if (Input.GetKeyDown(KeyCode.K))
        {
            TakeDamage(10f);
        }
    }

    // 체력 감소
    public void TakeDamage(float damage)
    {
        currentHealth -= damage;

        // 체력이 0 아래로 내려가지 않도록 제한
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        UpdateHealthBar();
    }

    // 체력 회복
    public void Heal(float amount)
    {
        currentHealth += amount;

        // 최대 체력을 넘지 않도록 제한
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        UpdateHealthBar();
    }

    // 체력바 갱신
    void UpdateHealthBar()
    {
        if (healthBarFill != null)
        {
            healthBarFill.fillAmount = currentHealth / maxHealth;
        }
    }
}
