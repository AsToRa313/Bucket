using UnityEngine;

// بيرسم الدهان على نسيج اللوحة
public class CanvasPainter : MonoBehaviour
{
    [Header("=== إعدادات اللوحة ===")]
    public int textureWidth = 1024;
    public int textureHeight = 1024;
    public Color backgroundColor = Color.white;

    [Header("=== إعدادات الرسم ===")]
    [Range(0f, 1f)]
    public float paintOpacity = 0.9f;
    public bool enableBlending = true;

    private Texture2D canvasTexture;
    private Color[] pixels;
    private bool isDirty = false;

    void Start()
    {
        InitializeCanvas();
    }

    void InitializeCanvas()
    {
        // اصنع النسيج
        canvasTexture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGBA32, false);
        canvasTexture.filterMode = FilterMode.Bilinear;
        canvasTexture.wrapMode = TextureWrapMode.Clamp;

        // ملّي بالخلفية
        pixels = new Color[textureWidth * textureHeight];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = backgroundColor;

        canvasTexture.SetPixels(pixels);
        canvasTexture.Apply();

        // حط النسيج على اللوحة
        var renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.mainTexture = canvasTexture;
        }
    }

    // ارسم نقطة دهان
    public void Paint(Vector2 texCoord, Color color, float radius)
    {
        int centerX = Mathf.RoundToInt(texCoord.x * textureWidth);
        int centerY = Mathf.RoundToInt(texCoord.y * textureHeight);
        int radiusPixels = Mathf.RoundToInt(radius * textureWidth);

        // ارسم دائرة
        for (int x = -radiusPixels; x <= radiusPixels; x++)
        {
            for (int y = -radiusPixels; y <= radiusPixels; y++)
            {
                // تحقق إذا داخل الدائرة
                float dist = Mathf.Sqrt(x * x + y * y);
                if (dist > radiusPixels) continue;

                int px = centerX + x;
                int py = centerY + y;

                // تحقق الحدود
                if (px < 0 || px >= textureWidth || py < 0 || py >= textureHeight) continue;

                // احسب الشفافية حسب المسافة من المركز
                float alpha = (1f - dist / radiusPixels) * paintOpacity;
                int index = py * textureWidth + px;

                if (enableBlending)
                {
                    // امزج اللون
                    pixels[index] = Color.Lerp(pixels[index], color, alpha);
                }
                else
                {
                    pixels[index] = color;
                }
            }
        }

        isDirty = true;
    }

    void LateUpdate()
    {
        // حدّث النسيج بس لما في تغيير
        if (isDirty)
        {
            canvasTexture.SetPixels(pixels);
            canvasTexture.Apply();
            isDirty = false;
        }
    }

    // احفظ الصورة
    public void SaveCanvas(string filename = "painting")
    {
        byte[] bytes = canvasTexture.EncodeToPNG();
        string path = Application.dataPath + "/" + filename + ".png";
        System.IO.File.WriteAllBytes(path, bytes);
        Debug.Log("حُفظت اللوحة في: " + path);
    }

    // مسح اللوحة
    public void ClearCanvas()
    {
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = backgroundColor;
        isDirty = true;
    }
}