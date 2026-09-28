using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMover : MonoBehaviour
{
    //이동 관련 변수 선언
    public Vector2 input;
    public float speed;
    [SerializeField] private float dashMultiplier = 3f;
    [SerializeField] private float dashDuration = 0.2f;
    [SerializeField] private float dashCoolDown = 1.5f;
    [SerializeField] private SpriteRenderer playerSprite;
    private bool isDashing = false;
    private bool canDash = true;

    //좌우반전 관련 변수 선언
    private Camera mainCam;
    private Vector3 cursorPos;
    Rigidbody2D rb;

    // Start is called before the first frame update
    void Start()
    {
        //리지드바디2D 찾기
        rb = GetComponent<Rigidbody2D>();

        //좌우반전
        mainCam = Camera.main;
    }

    // Update is called once per frame
    void Update()
    {
        input.x = Input.GetAxisRaw("Horizontal");    //수평 입력
        input.y = Input.GetAxisRaw("Vertical");      //수직 입력

        cursorPos = mainCam.ScreenToWorldPoint(Input.mousePosition); //마우스커서 위치



        if (cursorPos.x < transform.position.x)
        {
            playerSprite.flipX = false;
        }
        else if (cursorPos.x > transform.position.x)
        {
            playerSprite.flipX = true;
        }


        //구르기
        if (Input.GetKeyDown(KeyCode.Space) && !isDashing && canDash && input != Vector2.zero)
        {
            StartCoroutine(Dash());
        }
    }

    private void FixedUpdate()
    {
        //걷기
        if (!isDashing)
        {
            rb.velocity = input.normalized * speed;
        }
    }

    private IEnumerator Dash()
    {
        isDashing = true;
        canDash = false;

        //이동 방향으로 대시 속도 부여
        Vector2 dashDir = input.normalized;
        rb.velocity = dashDir * speed * dashMultiplier;
        yield return new WaitForSeconds(dashDuration);

        isDashing = false;
        StartCoroutine(DashCoolDown());
    }

    private IEnumerator DashCoolDown()
    {
        yield return new WaitForSeconds(dashCoolDown);
        canDash = true;
    }
}