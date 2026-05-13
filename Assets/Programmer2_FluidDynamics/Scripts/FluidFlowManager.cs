using UnityEngine;

public class FluidFlowManager : MonoBehaviour
{
    // هذه الخانة التي ستظهر لك في Unity لسحب ملف RedPaint إليها
    public FluidDataSO fluidData;

    [Header("Physical Constants")]
    public float rho = 1200f;
    public float holeArea = 0.0001f;
    public float dischargeCoefficient = 0.62f;

    [Header("Current State")]
    public float paintHeight = 0.2f;
    public Vector3 effectiveGravity = new Vector3(0, -9.81f, 0);

    [Header("Final Outputs for Programmer 3")]
    public float outputFlowQuantity; // كمية السائل الخارجة لحظياً
    public float outputVelocity;     // سرعة الخروج الفعلية
    public Vector3 outputExitAngle;  // زاوية الخروج اللحظية
    private Vector3 lastVelocity;
    void Update()
    {
        // 0. استيراد الخصائص الفيزيائية من ملف البيانات (إذا وجد)
        if (fluidData != null)
        {
            rho = fluidData.density;
            // ملاحظة: الـ dischargeCoefficient يتم التحكم به أيضاً من سكريبت الانسداد
        }

        //[cite_start]// 1. حساب الجاذبية الفعالة (الضغط الهيدروديناميكي) [cite: 15]
        // في حال وجود Rigidbody، سنطرح تسارع السطل من جاذبية الأرض
        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
           // [cite_start]// g_eff = g_earth - a_bucket 
            effectiveGravity = Physics.gravity - (rb.linearVelocity - lastVelocity) / Time.deltaTime;
            lastVelocity = rb.linearVelocity;
        }
        else
        {
            effectiveGravity = Physics.gravity; // حالة السكون
        }

       // [cite_start]// 2. حساب السرعة المثالية باستخدام قانون تورشيللي: v = sqrt(2 * g * h) [cite: 16]
        float gMagnitude = effectiveGravity.magnitude;
        float idealVelocity = Mathf.Sqrt(2 * gMagnitude * paintHeight);

        //[cite_start]// 3. حساب السرعة الفعلية ومعدل التدفق [cite: 16]
        // السرعة الفعلية تأخذ بعين الاعتبار لزوجة واحتكاك الفتحة
        outputVelocity = idealVelocity * dischargeCoefficient;
        float flowRate = dischargeCoefficient * holeArea * outputVelocity;

       // [cite_start]// 4. تحديث انخفاض مستوى الطلاء (ديناميكا الكتلة المتغيرة) [cite: 7, 8]
        paintHeight -= (flowRate * Time.deltaTime) / holeArea;
        paintHeight = Mathf.Max(paintHeight, 0); // ضمان عدم نفاذ السائل تحت الصفر

       // [cite_start]// 5. تجهيز المخرجات النهائية للمبرمج الثالث 
        outputFlowQuantity = flowRate * Time.deltaTime; // كمية السائل الخارجة في هذا الفريم
        outputExitAngle = -transform.up; // اتجاه الفتحة (أسفل السطل) 
    }

    
    
}