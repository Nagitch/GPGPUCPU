using UnityEngine;

/// <summary>
/// OnRenderImageを使わずにRenderTextureを直接表示
/// </summary>
public class FilterPaletteDisplay : MonoBehaviour
{
    public FilterPaletteDemo demo;
    
    void OnGUI()
    {
        if (demo == null)
        {
            demo = FindAnyObjectByType<FilterPaletteDemo>();
        }

        if (demo != null)
        {
            RenderTexture rt = demo.GetOutputTexture();
            if (rt != null)
            {
                // 画面全体にRenderTextureを表示
                GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), rt);
            }
            else
            {
                GUI.Label(new Rect(10, 10, 500, 30), "RenderTexture is null");
            }
        }
        else
        {
            GUI.Label(new Rect(10, 10, 500, 30), "FilterPaletteDemo not found");
        }
    }
}
