using UnityEngine;

namespace Unleash.Laboratory
{
    /// <summary>
    /// 소품용 단순 프레임 애니메이션 (경고등, 스파크, 콘솔 화면 등).
    /// 문은 Animator 를 쓰고, 상태가 필요 없는 장식 소품만 이 컴포넌트를 쓴다.
    /// </summary>
    [RequireComponent(typeof(SpriteRenderer))]
    public class LabSpriteLoop : MonoBehaviour
    {
        [SerializeField] private Sprite[] frames;
        [SerializeField] private float fps = 10f;
        [SerializeField] private bool randomStartFrame = true;
        [Tooltip("true 면 한 번만 재생하고 오브젝트를 파괴한다 (스폰 이펙트 등).")]
        [SerializeField] private bool playOnceAndDestroy = false;

        private SpriteRenderer spriteRenderer;
        private float timer;
        private int index;

        public void Setup(Sprite[] newFrames, float newFps, bool once)
        {
            frames = newFrames;
            fps = newFps;
            playOnceAndDestroy = once;
            randomStartFrame = !once;
        }

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            if (frames == null || frames.Length == 0)
            {
                return;
            }

            index = randomStartFrame ? Random.Range(0, frames.Length) : 0;
            timer = 0f;
            spriteRenderer.sprite = frames[index];
        }

        private void Update()
        {
            if (frames == null || frames.Length == 0 || fps <= 0f)
            {
                return;
            }

            timer += Time.deltaTime;
            float frameTime = 1f / fps;
            while (timer >= frameTime)
            {
                timer -= frameTime;
                index++;
                if (index >= frames.Length)
                {
                    if (playOnceAndDestroy)
                    {
                        Destroy(gameObject);
                        return;
                    }

                    index = 0;
                }

                spriteRenderer.sprite = frames[index];
            }
        }
    }
}
