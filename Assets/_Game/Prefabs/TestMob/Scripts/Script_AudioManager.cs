using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Script_AudioManager : MonoBehaviour
{
    
    [SerializeField] public AudioClip m_audio;
    private AudioSource audioSource;
    private bool m_isPlaying = false;
    private int playCount = 0;
    // Start is called before the first frame update
    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    // Update is called once per frame
    void Update()
    {
        if (!m_isPlaying && 0 == playCount)
        {
            PlaySound();
            m_isPlaying = true;
        }

        
    }
    public void PlaySound()
    {
        if (m_audio)
        {
            audioSource.PlayOneShot(m_audio);
            ++playCount;
        }
    }
}
