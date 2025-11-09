using UnityEngine;
using UnityEngine.UI;

namespace GPGPUCPU
{
    /// <summary>
    /// RenderTextureをUIで表示するためのヘルパー
    /// </summary>
    public class FilterPaletteViewer : MonoBehaviour
    {
        public FilterPaletteDemo demo;
        public RawImage displayImage;

        void Start()
        {
            if (demo == null)
            {
                demo = FindObjectOfType<FilterPaletteDemo>();
            }
        }

        void Update()
        {
            if (demo != null && displayImage != null)
            {
                // FilterPaletteDemoの出力RenderTextureを取得して表示
                RenderTexture outRT = demo.GetOutputTexture();
                if (outRT != null)
                {
                    displayImage.texture = outRT;
                }
            }
        }
    }
}
