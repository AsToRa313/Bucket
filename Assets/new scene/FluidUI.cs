using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// واجهة تحكم بالسائل: أزرار (دفع/جذب/Reset) + منزلقات (قوة/نصف قطر/جاذبية).
/// تبني كل عناصر UI تلقائياً بالكود - فقط اربط FluidClavetSim وشغّل.
///
/// الإعداد:
/// 1. اعمل Canvas (GameObject > UI > Canvas) - أو سيُنشأ تلقائياً.
/// 2. أضف هذا السكربت لأي GameObject.
/// 3. اربط حقل Sim بكائن FluidClavetSim.
/// </summary>
public class FluidUI : MonoBehaviour
{
    [Header("المرجع")]
    public FluidClavetSim sim;

    [Header("نطاقات المنزلقات")]
    public float minStrength = 0f;
    public float maxStrength = 300f;
    public float minRadius = 0.3f;
    public float maxRadius = 5f;
    public float minGravity = 0f;
    public float maxGravity = 20f;

    Text modeLabel;
    Canvas canvas;

    void Start()
    {
        if (sim == null)
        {
            sim = FindObjectOfType<FluidClavetSim>();
            if (sim == null) { Debug.LogError("[FluidUI] لم يُربط FluidClavetSim!"); return; }
        }
        BuildUI();
    }

    void BuildUI()
    {
        // Canvas
        canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            var canvasGO = new GameObject("FluidCanvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }
        // نتأكد من وجود EventSystem (ضروري لتفاعل UI)
        if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<UnityEngine.EventSystems.EventSystem>();
            es.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }

        // لوحة خلفية (يسار الشاشة)
        var panel = CreatePanel(canvas.transform, new Vector2(20, -20), new Vector2(240, 360));

        float y = -20f;
        float step = 0f;

        // عنوان
        CreateLabel(panel, "fluid control", new Vector2(0, y), 20, TextAnchor.MiddleCenter, 240);
        y -= 40f;

        // زر دفع
        CreateButton(panel, "Push", new Vector2(20, y), new Vector2(200, 36),
            new Color(0.85f, 0.3f, 0.3f), () => { sim.SetPushMode(); SetMode("الوضع: دفع"); });
        y -= 44f;

        // زر جذب
        CreateButton(panel, "Pull", new Vector2(20, y), new Vector2(200, 36),
            new Color(0.3f, 0.7f, 0.4f), () => { sim.SetPullMode(); SetMode("الوضع: جذب"); });
        y -= 44f;

        // زر Reset
        CreateButton(panel, "Reset", new Vector2(20, y), new Vector2(200, 36),
            new Color(0.35f, 0.45f, 0.85f), () => sim.ResetFluid());
        y -= 50f;

        // منزلق القوة
        CreateLabel(panel, "click power", new Vector2(20, y), 14, TextAnchor.MiddleLeft, 200);
        y -= 24f;
        CreateSlider(panel, new Vector2(20, y), 200, minStrength, maxStrength, sim.mouseStrength,
            v => sim.SetMouseStrength(v));
        y -= 40f;

        // منزلق نصف القطر
        CreateLabel(panel, "raduis fpr click", new Vector2(20, y), 14, TextAnchor.MiddleLeft, 200);
        y -= 24f;
        CreateSlider(panel, new Vector2(20, y), 200, minRadius, maxRadius, sim.mouseRadius,
            v => sim.SetMouseRadius(v));
        y -= 40f;

        // منزلق الجاذبية
        CreateLabel(panel, "gravity", new Vector2(20, y), 14, TextAnchor.MiddleLeft, 200);
        y -= 24f;
        CreateSlider(panel, new Vector2(20, y), 200, minGravity, maxGravity, sim.gravity,
            v => sim.SetGravity(v));
        y -= 40f;

    }

    void SetMode(string txt) { if (modeLabel != null) modeLabel.text = txt; }

    // ---------- دوال بناء عناصر UI ----------

    RectTransform CreatePanel(Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject("Panel", typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = new Color(0.1f, 0.1f, 0.12f, 0.85f);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        return rt;
    }

    Button CreateButton(Transform parent, string label, Vector2 pos, Vector2 size,
                        Color color, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;

        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(onClick);

        CreateLabel(rt, label, Vector2.zero, 15, TextAnchor.MiddleCenter, size.x, size.y);
        return btn;
    }

    Text CreateLabel(Transform parent, string text, Vector2 pos, int fontSize,
                    TextAnchor anchor, float width, float height = 30f)
    {
        var go = new GameObject("Label", typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.text = text;
        t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        t.fontSize = fontSize;
        t.alignment = anchor;
        t.color = Color.white;
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, height);
        return t;
    }

    Slider CreateSlider(Transform parent, Vector2 pos, float width,
                       float min, float max, float value, UnityEngine.Events.UnityAction<float> onChange)
    {
        var go = new GameObject("Slider", typeof(Slider));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(width, 20);

        // خلفية
        var bg = new GameObject("Background", typeof(Image));
        bg.transform.SetParent(go.transform, false);
        bg.GetComponent<Image>().color = new Color(0.25f, 0.25f, 0.3f);
        var bgRt = bg.GetComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
        bgRt.sizeDelta = Vector2.zero;

        // منطقة التعبئة
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        var faRt = fillArea.GetComponent<RectTransform>();
        faRt.anchorMin = Vector2.zero; faRt.anchorMax = Vector2.one;
        faRt.sizeDelta = Vector2.zero;

        var fill = new GameObject("Fill", typeof(Image));
        fill.transform.SetParent(fillArea.transform, false);
        fill.GetComponent<Image>().color = new Color(0.4f, 0.6f, 0.95f);
        var fillRt = fill.GetComponent<RectTransform>();
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = new Vector2(0, 1);
        fillRt.sizeDelta = new Vector2(10, 0);

        // المقبض
        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(go.transform, false);
        var haRt = handleArea.GetComponent<RectTransform>();
        haRt.anchorMin = Vector2.zero; haRt.anchorMax = Vector2.one;
        haRt.sizeDelta = Vector2.zero;

        var handle = new GameObject("Handle", typeof(Image));
        handle.transform.SetParent(handleArea.transform, false);
        handle.GetComponent<Image>().color = Color.white;
        var hRt = handle.GetComponent<RectTransform>();
        hRt.sizeDelta = new Vector2(16, 20);

        var slider = go.GetComponent<Slider>();
        slider.fillRect = fillRt;
        slider.handleRect = hRt;
        slider.targetGraphic = handle.GetComponent<Image>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.onValueChanged.AddListener(onChange);

        return slider;
    }
}