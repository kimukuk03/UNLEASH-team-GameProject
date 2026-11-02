using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Script_Effect : MonoBehaviour
{
    public Sprite[] spriteEffect;
    public float updateDelay = 0.1f;

    private int currentFrame = 0;

    private SpriteRenderer spriteRenderer;

    // Start is called before the first frame update
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        StartCoroutine(SpriteUpdate());


    }

    IEnumerator SpriteUpdate()
    {
        while (true)
        {
            SetAnimation(spriteEffect); 

            yield return new WaitForSeconds(updateDelay);
        }
    }

    private void SpriteChange(Sprite newSprite)
    {
        spriteRenderer.sprite = newSprite;
    }
 
    private void SetAnimation(Sprite[] targetAnimation)
    {
        currentFrame = (currentFrame + 1) % targetAnimation.Length;
        
        SpriteChange(targetAnimation[currentFrame]);
    }

    // 스프라이트 방향을 정합니다. 기본이 오른쪽이고 반대 방향이 왼쪽이라 생각하고 만들었습니다.
    // 여담: 방향 전환 기준에 0을 포함하면 너무 빠르게 바뀌어서 완성도가 떨어져 보이더라요.
    public void SetSpriteDirection(float DirectionX)
    {
        if (DirectionX > 0)
        {
            spriteRenderer.flipX = false;
        }
        else if (DirectionX < 0)
        {
            spriteRenderer.flipX = true;
        }
    }
}
