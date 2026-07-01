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
    [Tooltip("حجم البقعة عند سقوط القطرة (صغير = بحجم القطرة)")]
    public float splashRadius = 0.012f;

    [Tooltip("قوة تأثير الطلاء (0-1)")]
    [Range(0f, 1f)]
    public float paintOpacity = 0.85f;

    [Tooltip("مسافة إطالة المسار بناءً على السرعة")]
    public float velocityStretch = 2f;

    [Header("Canvas Mapping")]
    [Tooltip("احسب الحجم تلقائياً من الـ Plane (نصف الحجم = 5×scale)")]
    public bool autoSize = true;
    [Tooltip("نصف الحجم الأفقي للوحة (يطابق Canvas Half Size في SPHSimulation)")]
    public Vector2 canvasHalfSize = new Vector2(1f, 1f);

    [Header("Wet/Dry Paint (رطب/ناشف)")]
    [Tooltip("مدة جفاف الدهان بالثواني (بعدها لا يتمازج)")]
    public float dryTime = 5f;
    [Tooltip("قوة تغميق الدهان الرطب المتراكم (0.1-0.5)")]
    [Range(0f, 1f)]
    public float wetBuildup = 0.25f;
    [Tooltip("قوة انتشار/فرش الدهان الرطب مع جيرانه (0=بلا انتشار)")]
    [Range(0f, 0.5f)]
    public float wetDiffusion = 0.15f;
    [Tooltip("كل كم فريم يطبّق الانتشار (أعلى = أداء أفضل)")]
    public int diffusionInterval = 3;

    [Header("Pooling (تجمّع وتوسّع البركة)")]
    [Tooltip("تفعيل توسّع البقعة مع تراكم الدهان (بركة تكبر)")]
    public bool enablePooling = true;
    [Tooltip("كمية الدهان التي يضيفها كل قطرة عند نفس البكسل")]
    public float poolAddPerDrop = 0.35f;
    [Tooltip("حد التشبّع الذي يبدأ بعده الدهان بالفيض للجوانب")]
    public float poolSaturation = 1f;
    [Tooltip("قوة الفيض للبكسلات المجاورة (توسّع البركة)")]
    [Range(0f, 1f)]
    public float poolSpread = 0.25f;

    [Header("References")]
    public SPHSimulation1 sphSystem;

    // ============================================================
    //  داخلية
    // ============================================================
    private Texture2D canvasTexture;
    private Color[] pixels;
    private float[] wetness;        // رطوبة كل بكسل (0=ناشف، 1=رطب طازج)
    private float[] accumulation;   // كمية الدهان المتراكمة (للفيض والتوسّع)
    private Color paintColorCache = Color.red;  // آخر لون دهان (للفيض)
    private bool texturesDirty = false;
    private bool useGPUTexture = false;   // لو مفعّل، الرسم يتم على GPU (لا CPU)

    /// <summary>
    /// يستقبل تكستشر RenderTexture من GPU ويعرضه على اللوحة.
    /// عند استخدامه، يتوقف نظام الرسم على CPU (الرسم يصير على GPU مباشرة).
    /// </summary>
    public void SetGPUTexture(RenderTexture rt)
    {
        useGPUTexture = true;
        GetComponent<Renderer>().material.mainTexture = rt;
        Debug.Log("[CanvasPainter] وضع الرسم على GPU مفعّل - صفر تأخير");
    }

    // تتبع الجسيمات التي لامست اللوحة مسبقاً لتجنب التكرار
    private System.Collections.Generic.HashSet<int> paintedIds
        = new System.Collections.Generic.HashSet<int>();

    void Start()
    {
        // Unity Plane حجمه 10×10 عند scale=1، فنصف الحجم = 5×scale
        if (autoSize)
        {
            Vector3 sc = transform.lossyScale;
            canvasHalfSize = new Vector2(5f * sc.x, 5f * sc.z);
        }
        // ملاحظة: لو المدير فعّل وضع GPU عبر SetGPUTexture، نظام CPU يبقى خامل
        // نهيّئ تكستشر CPU احتياطياً (يُستبدل لو GPU فُعّل)
        if (!useGPUTexture)
            InitCanvas();
    }

    void Update()
    {
        // لو الرسم على GPU، لا حاجة لأي معالجة CPU
        if (useGPUTexture) return;

        CheckDroplets();

        // انتشار/فرش الدهان الرطب (دورياً للأداء)
        if (wetDiffusion > 0f && Time.frameCount % Mathf.Max(1, diffusionInterval) == 0)
            DiffuseWetPaint();

        // فيض وتوسّع البركة (البكسلات المتشبّعة تصبغ جيرانها)
        if (enablePooling && Time.frameCount % Mathf.Max(1, diffusionInterval) == 0)
            SpreadPooling();

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

    // انتشار الدهان الرطب: كل بكسل رطب يتمازج قليلاً مع جيرانه الرطاب
    void DiffuseWetPaint()
    {
        int res = textureResolution;
        bool changed = false;

        // نمر على البكسلات ونمزج الرطبة مع جيرانها الأربعة
        for (int y = 1; y < res - 1; y++)
        {
            for (int x = 1; x < res - 1; x++)
            {
                int idx = y * res + x;
                float w = wetness[idx];
                if (w < 0.05f) continue;   // ناشف => لا ينتشر

                // متوسط لون الجيران الأربعة الرطاب
                Color sum = pixels[idx];
                float totalW = 1f;
                int[] nb = { idx - 1, idx + 1, idx - res, idx + res };
                foreach (int n in nb)
                {
                    float wn = wetness[n];
                    if (wn > 0.05f)
                    {
                        sum += pixels[n];
                        totalW += 1f;
                    }
                }
                Color avg = sum / totalW;

                // امزج نحو المتوسط بقوة الانتشار المعدّلة برطوبة البكسل
                float k = wetDiffusion * w;
                pixels[idx] = Color.Lerp(pixels[idx], avg, k);
                changed = true;
            }
        }
        if (changed) texturesDirty = true;
    }

    // فيض وتوسّع البركة: البكسل المتشبّع يوزّع فائض الدهان على جيرانه فيصبغهم
    void SpreadPooling()
    {
        int res = textureResolution;
        bool changed = false;

        for (int y = 1; y < res - 1; y++)
        {
            for (int x = 1; x < res - 1; x++)
            {
                int idx = y * res + x;
                float acc = accumulation[idx];
                if (acc <= poolSaturation) continue;   // ما وصل التشبّع => لا فيض

                // الفائض فوق حد التشبّع
                float excess = acc - poolSaturation;
                float give = excess * poolSpread;
                accumulation[idx] -= give;

                // وزّع الفائض على الجيران الأربعة + اصبغهم بلون الدهان
                int[] nb = { idx - 1, idx + 1, idx - res, idx + res };
                float share = give / 4f;
                foreach (int n in nb)
                {
                    accumulation[n] += share;
                    // اصبغ الجار بلون الدهان بنسبة كمية الفيض (توسّع مرئي)
                    float paintAmount = Mathf.Clamp01(share * 2f);
                    pixels[n] = Color.Lerp(pixels[n], paintColorCache, paintAmount);
                    wetness[n] = Mathf.Min(1f, wetness[n] + paintAmount);
                }
                changed = true;
            }
        }
        if (changed) texturesDirty = true;
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
        accumulation = new float[textureResolution * textureResolution];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = backgroundColor;
            wetness[i] = 0f;
            accumulation[i] = 0f;
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

        paintColorCache = color;   // احفظ اللون للفيض

        // حجم البقعة بالمتر (splashRadius = نصف قطر البقعة بالمتر)
        // نحوّل من متر إلى بكسل عبر حجم اللوحة الفعلي
        float speed = velocity.magnitude;
        float radiusMeters = splashRadius * (1f + speed * 0.1f);
        // عرض اللوحة الكامل بالمتر = canvasHalfSize.x * 2 ، يقابله textureResolution بكسل
        float metersToPixels = textureResolution / (canvasHalfSize.x * 2f);
        int pixR = Mathf.RoundToInt(radiusMeters * metersToPixels);
        // حد أدنى 1 بكسل
        pixR = Mathf.Clamp(pixR, 1, textureResolution / 4);

        int cx = Mathf.RoundToInt(uv.x * textureResolution);
        int cy = Mathf.RoundToInt(uv.y * textureResolution);

        // زِد كمية الدهان المتراكمة عند المركز (يغذّي الفيض والتوسّع)
        if (enablePooling)
        {
            int cIdx = cy * textureResolution + cx;
            if (cIdx >= 0 && cIdx < accumulation.Length)
                accumulation[cIdx] += poolAddPerDrop;
        }

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
            if (accumulation != null) accumulation[i] = 0f;
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