using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Script_TestMob_Sprite : MonoBehaviour
{
    public Sprite[] spriteIdle;
    public Sprite[] spriteMove;
    public Sprite[] spriteAttack;
    public Sprite[] spriteDead;
    public float updateDelay = 0.1f;

    private MONSTERSTATE currentAnimation = MONSTERSTATE.IDLE;
    private int currentFrame = 0;

    private SpriteRenderer spriteRenderer;
    private Monster parent = null;

    // Start is called before the first frame update
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        parent = GetComponentInParent<Monster>();

        StartCoroutine(SpriteUpdate());
    }

    // Update is called once per frame
    void Update()
    {
    }
    IEnumerator SpriteUpdate()
    {
        while (true)
        {
            SpriteChangeByState();

            yield return new WaitForSeconds(updateDelay);
        }
    }
    
    private void SpriteChange(Sprite newSprite)
    {
        spriteRenderer.sprite = newSprite;
    }
    public void SpriteChangeByState()
    {
        switch (parent.m_state)
        {
            case (MONSTERSTATE.IDLE):
                {
                    SetAnimation(MONSTERSTATE.IDLE,spriteIdle);
                    break;
                }
            case (MONSTERSTATE.CHASE):
                {
                    SetAnimation(MONSTERSTATE.CHASE,spriteMove);
                    break;
                }
            case (MONSTERSTATE.ATTACK):
                {
                    SetAnimation(MONSTERSTATE.ATTACK,spriteAttack);
                    break;
                }
            default:
                {
                    break;
                }
        }
    }
    private void SetAnimation(MONSTERSTATE targetState, Sprite[] targetAnimation)
    {
        if(currentAnimation != targetState)
        {
            currentAnimation = targetState;
            currentFrame = 0;
        }
        else
        {
            currentFrame = (currentFrame + 1) % targetAnimation.Length;
        }

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
        else if ( DirectionX < 0)
        {
            spriteRenderer.flipX = true;
        }
    }
    public int GetCurrentFrame()
    {
        return currentFrame;
    }
}
