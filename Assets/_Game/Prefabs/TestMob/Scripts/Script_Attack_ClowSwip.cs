using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Script_Attack_ClowSwip : MonoBehaviour
{
    private Script_Effect m_sprite;

    // Start is called before the first frame update
    void Awake()
    {
        m_sprite = GetComponentInChildren<Script_Effect>();
    }

    // Update is called once per frame
    void Update()
    {
        if (m_sprite == null)
        {
            Destroy(this.gameObject);
        }
    }
}
