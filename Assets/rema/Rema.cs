using UnityEngine;

public class Rema : MonoBehaviour
{
    [Header("Data Architecture")]
    public PendulumData sharedData; // صلة الوصل المشتركة بقيم النواس

    [Header("Fluid Properties")]
    public float paintDensity = 1200f;       // كثافة الطلاء الكيلوجرام/متر مكعب
    public float surfaceTension = 0.035f;    // التوتر السطحي للطلاء (جامّا) بالنيوتن/متر
    public float contactAngleDeg = 45f;      // زاوية التماس اللحظية مع الحواف بالدرجات

    [Header("Orifice / Hole Geometry")]
    public float holeRadius = 0.005f;        // نصف قطر فتحة السطل بالمتر (5 ملم)
    [Range(0f, 1f)] public float Cv = 0.97f; // معامل السرعة بناءً على حواف الفتحة
    [Range(0f, 1f)] public float Cc = 0.62f; // معامل الانكماش لحواف الفتحة الحادة

    [Header("Clogging Settings (Young-Laplace)")]
    public float dryingRate = 0.00001f;      // معدل جفاف الحواف وتراكم الطلاء لتصغير الفتحة
    [Header("Fluid Dynamics Outputs (المخرجات اللحظية)")]
    
    public float effectiveHoleRadius; // نصف قطر الفتحة اللحظي المتناقص

    // مخرجات المبرمج الثاني للأنظمة اللاحقة
    public float actualFlowVelocity;   // سرعة خروج الطلاء الفعلية
    public float currentDrainRate;    // معدل تصريف الطلاء اللحظي

    private void Start()
    {
        effectiveHoleRadius = holeRadius;
    }

    public void CalculateFluidPhysics(SphericalPendulumMath.BucketShape shape, float bucketHeight, float maxPaintMass)
    {
        if (sharedData == null || sharedData.currentPaintMass <= 0)
        {
            currentDrainRate = 0f;
            actualFlowVelocity = 0f;
            return;
        }

        // 1. حساب الارتفاع اللحظي للسائل (h) بناءً على شكل هندسة السطل
        float currentVolume = sharedData.currentPaintMass / paintDensity;
        float h = 0f;

        switch (shape)
        {
            case SphericalPendulumMath.BucketShape.Cylindrical:
                float maxVolumeCyl = maxPaintMass / paintDensity;
                h = (currentVolume / maxVolumeCyl) * bucketHeight;
                break;

            case SphericalPendulumMath.BucketShape.Conical:
                h = bucketHeight * Mathf.Pow(sharedData.fillRatio, 1f / 3f);
                break;

            case SphericalPendulumMath.BucketShape.Cubic:
                float maxVolumeCub = maxPaintMass / paintDensity;
                h = (currentVolume / maxVolumeCub) * bucketHeight;
                break;
        }

        // 2. حساب الضغط الهيدروديناميكي مع قراءة الجاذبية الفعالة المتغيرة من النواس
        float gEff = sharedData.effectiveGravity > 0 ? sharedData.effectiveGravity : 9.81f;
        float hydrodynamicPressure = paintDensity * gEff * h;

        // 3. فيزياء الانسداد (معادلة يونغ-لابلاس لحساب ضغط الاختراق الشعري)
        float thetaRad = contactAngleDeg * Mathf.Deg2Rad;
        float capillaryPressure = (2f * surfaceTension * Mathf.Cos(thetaRad)) / effectiveHoleRadius;

        // محاكاة جفاف الحواف التدريجي إذا كان التدفق بطيئاً
        if (hydrodynamicPressure < capillaryPressure * 1.5f)
        {
            effectiveHoleRadius -= dryingRate * Time.deltaTime;
            effectiveHoleRadius = Mathf.Max(effectiveHoleRadius, 0.0001f);
        }

        // فحص حدوث الانسداد: إذا لم يتجاوز الضغط الداخلي مقاومة التوتر السطحي ينقطع التدفق
        if (hydrodynamicPressure <= capillaryPressure)
        {
            actualFlowVelocity = 0f;
            currentDrainRate = 0f;
            return;
        }

        // 4. تطبيق قانون تورشيللي المعدّل لمعرفة سرعة التدفق الفعلية
        float idealVelocity = Mathf.Sqrt(2f * gEff * h);
        actualFlowVelocity = Cv * idealVelocity;

        // 5. حساب معدل التصريف الفعلي المستهدف للمبرمج الأول
        float holeArea = Mathf.PI * effectiveHoleRadius * effectiveHoleRadius;
        currentDrainRate = Cc * holeArea * actualFlowVelocity * paintDensity;

        // تغذية الـ ScriptableObject 
        sharedData.fluidExitVelocity = actualFlowVelocity;
        sharedData.dynamicDrainRate = currentDrainRate;
        sharedData.currentHoleRadius = effectiveHoleRadius;
    }
}