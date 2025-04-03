using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UVScroller : MonoBehaviour
{
    Vector2 uvSpeed = new Vector2(0, 0.01f);
    Vector2 uvOffset = Vector2.zero;
    Renderer rend;

    void Start()
    {
        rend = this.GetComponent<Renderer>();
    }

    void LateUpdate()
    {
        if (rend == null)
        {
            rend = this.GetComponent<Renderer>();
            return;
        }

        uvOffset += uvSpeed * Time.deltaTime;
        if (uvOffset.x > 0.0625f) uvOffset = new Vector2(0, uvOffset.y);
        if (uvOffset.y > 0.0625f) uvOffset = new Vector2(uvOffset.x, 0);

        rend.materials[0].SetTextureOffset("_MainTex", uvOffset);
    }
}
