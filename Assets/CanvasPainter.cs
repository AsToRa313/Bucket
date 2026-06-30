using UnityEngine;

/// <summary>
/// يستقبل قطرات SPH ويرسمها على تكستشر اللوحة
/// اربطه مع PaintSPH ليشتغلا مع بعض
/// </summary>
[RequireComponent(typeof(Renderer))]
public class CanvasPainter : MonoBehaviour
{
    [Header("Canvas Settings")]
    [Tooltip("دقة تكستشر اللوحة (512 أو 1024)")]
    public int textureResolution = 512;

    [Tooltip("لون خلفية اللوحة")]
    public Color backgroundColor = Color.white;

    [Header("Paint Brush")]
    [Tooltip("حجم البقعة عند سقوط القطرة")]
    public float splashRadius = 0.05f;

    [Tooltip("قوة تأثير الطلاء (0-1)")]
    [Range(0f, 1f)]
    public float paintOpacity = 0.85f;

    [Tooltip("مسافة إطالة المسار بناءً على السرعة")]
    public float velocityStretch = 2f;

    [Header("Canvas Mapping")]
    [Tooltip("نصف الحجم الأفقي للوحة (يطابق Canvas Half Size في SPHSimulation)")]
    public Vector2 canvasHalfSize = new Vector2(1f, 1f);

    [Header("Wet/Dry Paint (رطب/ناشف)")]
    [Tooltip("مدة جفاف الدهان بالثواني (بعدها لا يتمازج)")]
    public float dryTime = 5f;
    [Tooltip("قوة تغميق الدهان الرطب المتراكم (0.1-0.5)")]
    [Range(0f, 1f)]
    public float wetBuildup = 0.25f;

    [Header("References")]
    public PaintSPH sphSystem;

    // ============================================================
    //  داخلية
    // ============================================================
    private Texture2D canvasTexture;
    private Color[] pixels;
    private float[] wetness;        // رطوبة كل بكسل (0=ناشف، 1=رطب طازج)
    private bool texturesDirty = false;

    // تتبع الجسيمات التي لامست اللوحة مسبقاً لتجنب التكرار
    private System.Collections.Generic.HashSet<int> paintedIds
        = new System.Collections.Generic.HashSet<int>();

    void Start()
    {
        InitCanvas();
    }

    void Update()
    {
        CheckDroplets();

        // الجفاف التدريجي: الرطوبة تنقص مع الوقت
        if (wetness != null && dryTime > 0f)
        {
            float dryRate = Time.deltaTime / dryTime;
            for (int i = 0; i < wetness.Length; i++)
                if (wetness[i] > 0f)
                    wetness[i] = Mathf.Max(0f, wetness[i] - dryRate);
        }

        if (texturesDirty)
        {
            canvasTexture.SetPixels(pixels);
            canvasTexture.Apply();
            texturesDirty = false;
        }
    }

    // ============================================================

    void InitCanvas()
    {
        canvasTexture = new Texture2D(textureResolution, textureResolution,
            TextureFormat.RGBA32, false);
        canvasTexture.filterMode = FilterMode.Bilinear;

        // ملء الخلفية
        pixels = new Color[textureResolution * textureResolution];
        wetness = new float[textureResolution * textureResolution];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = backgroundColor;
            wetness[i] = 0f;
        }

        canvasTexture.SetPixels(pixels);
        canvasTexture.Apply();

        GetComponent<Renderer>().material.mainTexture = canvasTexture;
    }

    void CheckDroplets()
    {
        // الرسم بيصير من PaintSPH مباشرة عبر Splat()
    }

    // ============================================================
    //  API عام: يُستدعى من PaintSPH أو أي سكريبت آخر
    // ============================================================

    /// <summary>
    /// ارسم بقعة طلاء على اللوحة بناءً على موقع العالم وسرعة القطرة
    /// </summary>
    public void Splat(Vector3 worldPos, Vector3 velocity, Color color)
    {
        // حوّل الموقع إلى UV
        Vector2 uv = WorldToUV(worldPos);
        if (uv.x < 0f || uv.x > 1f || uv.y < 0f || uv.y > 1f) return;

        // حجم البقعة يتناسب مع سرعة القطرة
        float speed = velocity.magnitude;
        float radius = splashRadius * (1f + speed * 0.1f);
        int pixR = Mathf.RoundToInt(radius * textureResolution);
        // حد أدنى 1 بكسل (نقطة صغيرة بحجم القطرة)
        pixR = Mathf.Clamp(pixR, 1, textureResolution / 4);

        int cx = Mathf.RoundToInt(uv.x * textureResolution);
        int cy = Mathf.RoundToInt(uv.y * textureResolution);

        // رسم دائرة ناعمة (soft brush)
        for (int dx = -pixR; dx <= pixR; dx++)
            for (int dy = -pixR; dy <= pixR; dy++)
            {
                int px = cx + dx;
                int py = cy + dy;
                if (px < 0 || px >= textureResolution) continue;
                if (py < 0 || py >= textureResolution) continue;

                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float falloff = 1f - Mathf.Clamp01(dist / pixR);
                falloff = falloff * falloff; // تربيع للحواف الناعمة

                float alpha = paintOpacity * falloff;
                int idx = py * textureResolution + px;

                float wet = wetness[idx];
                if (wet > 0.01f)
                {
                    // البكسل رطب: الدهان الجديد يتراكم ويغمق (wet-on-wet)
                    // أولاً امزج نحو لون الدهان، ثم غمّق حسب التراكم والرطوبة
                    Color blended = Color.Lerp(pixels[idx], color, alpha);
                    float darken = 1f - (wetBuildup * falloff * wet);
                    blended.r *= darken;
                    blended.g *= darken;
                    blended.b *= darken;
                    pixels[idx] = blended;
                }
                else
                {
                    // البكسل ناشف: طبقة جديدة تترسّب فوق بشكل عادي
                    pixels[idx] = Color.Lerp(pixels[idx], color, alpha);
                }

                // رطّب البكسل (دهان طازج)
                wetness[idx] = Mathf.Min(1f, wetness[idx] + falloff);
            }

        // مسار إضافي باتجاه السرعة (velocity stretch)
        if (speed > 0.5f)
        {
            Vector2 dir = new Vector2(velocity.x, velocity.z).normalized;
            int steps = Mathf.RoundToInt(speed * velocityStretch);
            steps = Mathf.Clamp(steps, 0, 30);

            for (int s = 1; s <= steps; s++)
            {
                float t = (float)s / steps;
                Vector2 p = new Vector2(cx, cy) + dir * s * pixR * 0.4f;
                int spxR = Mathf.RoundToInt(pixR * (1f - t * 0.5f));

                for (int dx = -spxR; dx <= spxR; dx++)
                    for (int dy = -spxR; dy <= spxR; dy++)
                    {
                        int px = Mathf.RoundToInt(p.x) + dx;
                        int py = Mathf.RoundToInt(p.y) + dy;
                        if (px < 0 || px >= textureResolution) continue;
                        if (py < 0 || py >= textureResolution) continue;

                        float dist = Mathf.Sqrt(dx * dx + dy * dy);
                        float falloff = (1f - Mathf.Clamp01(dist / spxR)) * (1f - t);
                        falloff = falloff * falloff;

                        int idx = py * textureResolution + px;
                        pixels[idx] = Color.Lerp(pixels[idx], color, paintOpacity * falloff * 0.6f);
                    }
            }
        }

        texturesDirty = true;
    }

    /// <summary>مسح اللوحة والبدء من جديد</summary>
    public void ClearCanvas()
    {
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = backgroundColor;
            if (wetness != null) wetness[i] = 0f;
        }
        texturesDirty = true;
        paintedIds.Clear();
    }

    /// <summary>حفظ صورة اللوحة كـ PNG</summary>
    public void SaveCanvas(string path = "Assets/PaintResult.png")
    {
        byte[] bytes = canvasTexture.EncodeToPNG();
        System.IO.File.WriteAllBytes(path, bytes);
        Debug.Log($"[CanvasPainter] تم حفظ اللوحة في: {path}");
    }

    // ============================================================
    //  مساعدات
    // ============================================================
    Vector2 WorldToUV(Vector3 worldPos)
    {
        // إحداثيات نسبية لمركز اللوحة بالعالم (بدون تأثير scale)
        Vector3 rel = worldPos - transform.position;

        // نحوّل من النطاق [-half, +half] إلى [0, 1]
        float u = (rel.x / (canvasHalfSize.x * 2f)) + 0.5f;
        float v = (rel.z / (canvasHalfSize.y * 2f)) + 0.5f;

        return new Vector2(u, v);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, transform.localScale);
    }
}