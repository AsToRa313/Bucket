using UnityEngine;

/// <summary>
/// يبدّل لون الدهان في SPHSimulation1.
/// طريقتان للاستخدام:
/// 1. مفاتيح الأرقام 1-5 تبدّل بين الألوان (للتجربة السريعة).
/// 2. اربط أزرار UI باستدعاء SetColorX() من الـ Inspector (OnClick).
/// </summary>
public class PaintColorSwitcher : MonoBehaviour
{
    [Tooltip("نظام المحاكاة")]
    public SPHSimulation1 simulation;

    [Header("الألوان المتاحة (نظام CMYK)")]
    public Color color1 = new Color(0f, 1f, 1f, 1f);    // السماوي (Cyan)
    public Color color2 = new Color(1f, 0f, 1f, 1f);    // الأرجواني (Magenta)
    public Color color3 = new Color(1f, 1f, 0f, 1f);    // الأصفر (Yellow)
    public Color color4 = new Color(0.1f, 0.1f, 0.1f, 1f); // الأسود (Key/Black)
    public Color color5 = new Color(1f, 1f, 1f, 1f);    // الأبيض (White) - مفيد للتفتيح

    [Header("التحكم بلوحة المفاتيح")]
    [Tooltip("تفعيل مفاتيح 1-5 لتبديل الألوان")]
    public bool enableKeyboard = true;

    void Update()
    {
        if (!enableKeyboard || simulation == null) return;

        if (Input.GetKeyDown(KeyCode.Alpha1)) SetColor1();
        else if (Input.GetKeyDown(KeyCode.Alpha2)) SetColor2();
        else if (Input.GetKeyDown(KeyCode.Alpha3)) SetColor3();
        else if (Input.GetKeyDown(KeyCode.Alpha4)) SetColor4();
        else if (Input.GetKeyDown(KeyCode.Alpha5)) SetColor5();
    }
    void Awake()
    {
        // هذه الأسطر ستجبر Unity على تجاهل الـ Inspector واستخدام هذه الألوان دائماً
        color1 = new Color(0f, 1f, 1f, 1f);       // Cyan
        color2 = new Color(1f, 0f, 1f, 1f);       // Magenta
        color3 = new Color(1f, 1f, 0f, 1f);       // Yellow
        color4 = new Color(0.1f, 0.1f, 0.1f, 1f); // Black
        color5 = new Color(1f, 1f, 1f, 1f);       // White
    }
    // هذه الدوال تُربط بأزرار UI (OnClick) أو تُستدعى بالمفاتيح
    public void SetColor1() { if (simulation) simulation.SetPaintColor(color1); }
    public void SetColor2() { if (simulation) simulation.SetPaintColor(color2); }
    public void SetColor3() { if (simulation) simulation.SetPaintColor(color3); }
    public void SetColor4() { if (simulation) simulation.SetPaintColor(color4); }
    public void SetColor5() { if (simulation) simulation.SetPaintColor(color5); }

    // دالة عامة لأي لون مخصّص
    public void SetCustomColor(Color c) { if (simulation) simulation.SetPaintColor(c); }
}