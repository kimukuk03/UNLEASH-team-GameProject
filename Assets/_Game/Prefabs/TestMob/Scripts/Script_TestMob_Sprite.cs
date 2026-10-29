using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class Script_TestMob_Sprite : MonoBehaviour
{
    public float delay = 0.1f;

    private SpriteRenderer spriteRenderer;

    private Monster parent = null;


    // Start is called before the first frame update
    void Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();

        parent = GetComponentInParent<Monster>(); 

        StartCoroutine("SpriteUpdate");
    }

    // Update is called once per frame
    void Update()
    {
    }
    IEnumerator SpriteUpdate()
    {
        while (true)
        {
            yield return new WaitForSeconds(delay);
        }
    }
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



    //public void SpriteFlipX()
    //{
    //    isFacingBack = !isFacingBack;
    //    spriteRenderer.flipX = isFacingBack;
    //}
}
