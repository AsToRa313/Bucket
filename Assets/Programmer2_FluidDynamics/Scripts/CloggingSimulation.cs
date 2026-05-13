using UnityEngine;

public class CloggingSimulation : MonoBehaviour
{
    private FluidFlowManager flowManager;

    [Header("Clogging Parameters")]
    public float surfaceTension = 0.072f;
    public float contactAngle = 45f;
    public float dryingRate = 0.01f;

    [Header("Current Status")]
    public bool isClogged = false;
    private float effectiveHoleRadius;

    void Start()
    {
        flowManager = GetComponent<FluidFlowManager>();
        effectiveHoleRadius = Mathf.Sqrt(flowManager.holeArea / Mathf.PI);
    }

    void Update()
    {
        if (flowManager == null) return;

        // 1. محاكاة جفاف الحواف
        effectiveHoleRadius -= dryingRate * Time.deltaTime;
        effectiveHoleRadius = Mathf.Max(effectiveHoleRadius, 0.00001f); // نمنع الوصول للصفر تماماً لتجنب خطأ القسمة

        // 2. تطبيق معادلة Young-Laplace: Pressure = (2 * SurfaceTension * cos(Angle)) / Radius
        float cosAngle = Mathf.Cos(contactAngle * Mathf.Deg2Rad);
        float penetrationPressure = (2 * surfaceTension * cosAngle) / effectiveHoleRadius;

        // 3. حساب الضغط الهيدروستاتيكي الفعلي
        float hydrostaticPressure = flowManager.rho * flowManager.effectiveGravity.magnitude * flowManager.paintHeight;

        // 4. التحقق من الانسداد (Clogging)
        if (hydrostaticPressure < penetrationPressure || effectiveHoleRadius <= 0.0001f)
        {
            isClogged = true;
            flowManager.dischargeCoefficient = 0;
        }
        else
        {
            isClogged = false;
            // تقليل معامل التصريف بناءً على ضيق الفتحة مقارنة بالمساحة الأصلية
            flowManager.dischargeCoefficient = 0.62f * (effectiveHoleRadius / (Mathf.Sqrt(flowManager.holeArea / Mathf.PI)));
        }
    }
}