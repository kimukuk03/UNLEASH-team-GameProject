using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Script_AudioManager : MonoBehaviour
{
    
    [SerializeField] public AudioClip audioAttack;
    [SerializeField] public AudioClip audioDead;
    private float testTime = 0;
    private AudioSource audioSource;

    // Start is called before the first frame update
    void Start()
    {
        audioSource = GetComponent<AudioSource>();
    }

    // Update is called once per frame
    void Update()
    {
    }
    public void PlaySound(AudioClip targetAudio)
    {
        if (targetAudio)
        {
            audioSource.PlayOneShot(targetAudio);
        }
    }
}
