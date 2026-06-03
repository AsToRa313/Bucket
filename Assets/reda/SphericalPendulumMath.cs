using UnityEngine;

public class SphericalPendulumMath : MonoBehaviour
{
    public enum BucketShape { Cylindrical, Conical, Cubic }

    [Header("Data Architecture")]
    public PendulumData data;

    [Header("Mathematical Parameters")]
    public Transform pivot;
    public float baseLength = 5f;
    public float gravity = 9.81f;
    public float airDamping = 0.001f;
    public float pivotFriction = 0.05f;

    [Header("Bucket Geometry")]
    public BucketShape shape = BucketShape.Cylindrical;
    public float bucketHeight = 0.4f;

    [Header("Variable Mass Dynamics")]
    public float emptyBucketMass = 0.5f;
    public float maxPaintMass = 2.0f;
    public float currentPaintMass = 2.0f;
    public float drainRate = 0.05f;

    [Header("Torsional Twist Settings")]
    public float torsionalStiffness = 0.8f;
    public float torsionalDamping = 0.05f;
    public float bucketRadius = 0.2f;

    private float twistAngle = 0f;
    private float twistVelocity = 0f;


    [Header("Rope Visual Settings")]
    public int ropeSegments = 15; // عدد قطع الحبل لجعل الانحناء ناعماً
    public float sagFactor = 0.6f; // مدى تأثر الحبل بالارتخاء وهبوطه للأسفل

    // [متحولات الحركة الديكارتية الجديدة]
    private Vector3 velocity = Vector3.zero;
    private float effectiveLength = 5f;
    private bool isDragging = false;
    private float dragDepth;

    // متحولات الزوايا (يتم حسابها خلف الكواليس لإمداد الـ Data و الـ Shader)
    private float theta = 0f;
    private float phi = 0f;
    private float thetaVelocity = 0f;
    private float phiVelocity = 0f;
    private float lastTheta = 0f;
    private float lastPhi = 0f;

    private LineRenderer ropeRenderer;

   void Start()
{
    ropeRenderer = GetComponent<LineRenderer>();
    
    if (pivot != null)
    {
        // العودة للحساب التلقائي بناءً على موقع السطل الحالي في الـ Scene
        baseLength = Vector3.Distance(transform.position, pivot.position);
        UpdateMassAndCOM();
        SyncAnglesFromPosition();
    }
}

    void FixedUpdate()
    {
        if (isDragging || pivot == null) return;

        UpdateMassAndCOM();

        float totalMass = emptyBucketMass + currentPaintMass;
        float massDerivative = (currentPaintMass > 0) ? drainRate : 0f;

        // 1. حساب التخامد الفيزيائي الإجمالي
        float dragCoefficient = 1.0f;
        if (shape == BucketShape.Cylindrical) dragCoefficient = 0.82f;
        else if (shape == BucketShape.Cubic) dragCoefficient = 1.05f;
        else if (shape == BucketShape.Conical) dragCoefficient = 0.50f;

        float totalDamping = ((airDamping * dragCoefficient) + massDerivative) / totalMass + pivotFriction;

        // 2. محاكاة السقوط الحر مبدئياً (تطبيق الجاذبية والتخامد على متجه السرعة)
        velocity += Vector3.down * gravity * Time.fixedDeltaTime;
        velocity -= velocity * totalDamping * Time.fixedDeltaTime;

        // 3. حساب الموقع المتوقع في الإطار القادم
        Vector3 nextPosition = transform.position + velocity * Time.fixedDeltaTime;
        Vector3 offset = nextPosition - pivot.position;
        float currentDistance = offset.magnitude;

        // 4. فحص حالة الحبل (مشدود أم مرتخي سقوط حر)
        if (currentDistance >= effectiveLength)
        {
            // [الحبل مشدود]: تم الوصول للحد الأقصى لطول الحبل
            Vector3 ropeDirection = offset.normalized;

            // قصر الموقع مجبراً على محيط كرة النواس
            transform.position = pivot.position + ropeDirection * effectiveLength;

            // حساب السرعة المتجهة باتجاه الحبل (السرعة الطردية للخارج)
            float radialVelocity = Vector3.Dot(velocity, ropeDirection);

            if (radialVelocity > 0f)
            {
                // إعدام السرعة الخارجية (امتصاص الصدمة) لإبقاء السطل داخل المدار
                velocity -= ropeDirection * radialVelocity;
            }
        }
        else
        {
            // [الحبل مرتخي]: السطل في حالة سقوط حر كامل داخل نطاق الكرة
            transform.position = nextPosition;
        }

        // 5. إعادة حساب الزوايا والسرعات الزاوية من الموقع الديكارتي الجديد للمحافظة على الـ Data Architecture
        SyncAnglesFromPosition();

        // ---------------- [ إضافة: فيزياء فتل الحبل حول نفسه ] ----------------
        // عزم العطالة لأسطوانة (Iy = 0.5 * M * R^2) يتأثر ديناميكياً بنقصان الطلاء وخفة السطل
        float I_y = 0.5f * totalMass * (bucketRadius * bucketRadius);

        // حساب عزم الفتل = عزم الاسترداد النابضي + عزم التخميد اللزوجي المبطئ للبرم
        float torsionalTorque = (-torsionalStiffness * twistAngle) - (torsionalDamping * twistVelocity);

        // التسارع الزاوي للبرم = العزم / عزم العطالة
        float twistAcceleration = torsionalTorque / I_y;

        // التكامل الزمني بطريقة أويلر لتحديث سرعة وزاوية الالتواء
        twistVelocity += twistAcceleration * Time.fixedDeltaTime;
        twistAngle += twistVelocity * Time.fixedDeltaTime;
        // ---------------------------------------------------------------------

        // 6. شرط النوم والاستقرار النهائي عند المركز الميت (محدث ليشمل الفتل)
        if (Mathf.Abs(theta) < 0.02f && velocity.magnitude < 0.05f && Mathf.Abs(twistVelocity) < 0.05f)
        {
            transform.position = pivot.position + Vector3.down * effectiveLength;
            velocity = Vector3.zero;
            theta = 0f;
            thetaVelocity = 0f;
            phiVelocity = 0f;

            // تصفير متغيرات الفتل تماماً ليموت الاهتزاز المجهري
            twistAngle = 0f;
            twistVelocity = 0f;
        }

        UpdateRopeRenderer();
        UpdateSharedData();
    }

    void SyncAnglesFromPosition()
    {
        Vector3 localPos = transform.position - pivot.position;

        lastTheta = theta;
        lastPhi = phi;

        // حساب ثيتا وفاي من الموقع الفعلي الحالي
        theta = Mathf.Acos(Mathf.Clamp(-localPos.normalized.y, -1f, 1f));
        phi = Mathf.Atan2(localPos.z, localPos.x);

        // اشتقاق السرعات الزاوية بالنسبة للزمن لتغذية الشيرد داتا
        if (Time.fixedDeltaTime > 0)
        {
            thetaVelocity = (theta - lastTheta) / Time.fixedDeltaTime;
            phiVelocity = Mathf.DeltaAngle(lastPhi * Mathf.Rad2Deg, phi * Mathf.Rad2Deg) * Mathf.Deg2Rad / Time.fixedDeltaTime;
        }
    }

 
    private Vector3 lastDragPosition;

    void OnMouseDown()
    {
        isDragging = true;
        velocity = Vector3.zero;
        dragDepth = Camera.main.WorldToScreenPoint(transform.position).z;

        // تسجيل الموقع الابتدائي عند الضغط
        lastDragPosition = transform.position;
    }

    void OnMouseDrag()
    {
        if (pivot == null) return;

        dragDepth += Input.GetAxis("Mouse ScrollWheel") * 5f;
        Vector3 mouseScreenPoint = new Vector3(Input.mousePosition.x, Input.mousePosition.y, dragDepth);
        Vector3 mouseWorldPoint = Camera.main.ScreenToWorldPoint(mouseScreenPoint);

        // حساب الموقع المستهدف بناءً على سحبة الماوس
        Vector3 offset = (mouseWorldPoint - pivot.position).normalized * effectiveLength;
        Vector3 newPosition = pivot.position + offset;

        // [الإصلاح الجوهري]: حساب سرعة الرمي الحقيقية بناءً على المسافة المقطوعة بين الإطارات
        if (Time.deltaTime > 0)
        {
            velocity = (newPosition - lastDragPosition) / Time.deltaTime;
        }

        // تحديث المواقع
        transform.position = newPosition;
        lastDragPosition = newPosition; // تخزين الموقع للإطار القادم

        SyncAnglesFromPosition();
        UpdateRopeRenderer();
    }

    void OnMouseUp()
    {
        isDragging = false;
        // هنا يرث السطل متجه السرعة بالكامل (بما فيه مركبات الفتل الأفقي) ليطير بشكل دائري واقعي
    }

    void UpdateMassAndCOM()
    {
        if (currentPaintMass > 0)
        {
            currentPaintMass -= drainRate * Time.fixedDeltaTime;
            if (currentPaintMass < 0) currentPaintMass = 0;
        }

        float fillRatio = maxPaintMass > 0 ? currentPaintMass / maxPaintMass : 0;
        float z_cm = 0f;

        switch (shape)
        {
            case BucketShape.Conical: z_cm = (3f / 4f) * bucketHeight * Mathf.Pow(fillRatio, 1f / 3f); break;
            default: z_cm = (bucketHeight * fillRatio) / 2f; break;
        }
        effectiveLength = baseLength + (bucketHeight - z_cm);
    }

    void UpdateRopeRenderer()
    {
        if (pivot == null || ropeRenderer == null) return;

        Vector3 p0 = pivot.position;               // السقف
        Vector3 comPosition = transform.position;  // مركز الثقل (الأب)
        float offsetToHandle = effectiveLength - baseLength;

        // 1. حساب الاتجاه الأساسي بدون التأثر بدوران المجسم (لمنع تضارب الفيزياء)
        Vector3 directionToPivot = (p0 - comPosition).normalized;
        Vector3 straightHandlePos = comPosition + directionToPivot * offsetToHandle;

        // 2. حساب نقطة المنتصف والانحناء (Slack) عند السقوط الحر
        Vector3 p1 = (p0 + straightHandlePos) * 0.5f;
        float currentDistance = (straightHandlePos - p0).magnitude;

        if (currentDistance < baseLength && !isDragging)
        {
            float slack = baseLength - currentDistance;
            p1 += Vector3.down * slack * sagFactor;
        }

        // 3. توجيه السطل ليلحق انحناء الحبل من المركز (آمن فيزيائياً)
        Vector3 ropeDirectionAtBucket = (p1 - comPosition).normalized;
        if (ropeDirectionAtBucket.magnitude > 0.01f)
        {
            Quaternion baseRotation = Quaternion.FromToRotation(Vector3.up, ropeDirectionAtBucket);
            Quaternion twistRotation = Quaternion.AngleAxis(twistAngle * Mathf.Rad2Deg, Vector3.up);
            transform.rotation = baseRotation * twistRotation;
        }

        // 4. [الحل السحري الآمن]: بعد أن استقر دوران السطل، نقرأ مكان المقبض الفعلي
        Vector3 actualHandlePosition = transform.position + transform.up * offsetToHandle;

        // 5. نرسم الحبل ليتصل بالمقبض الفعلي ولن يفلت أبداً
        ropeRenderer.positionCount = ropeSegments;
        for (int i = 0; i < ropeSegments; i++)
        {
            float t = i / (float)(ropeSegments - 1);
            Vector3 point = Mathf.Pow(1f - t, 2) * p0 + 2f * (1f - t) * t * p1 + Mathf.Pow(t, 2) * actualHandlePosition;
            ropeRenderer.SetPosition(i, point);
        }

        // 6. الخدعة السحرية: فتل صورة الحبل (Texture) لتبدو واقعية
        if (ropeRenderer.material != null)
        {
            // تحريك الـ Texture أفقياً (X) ليعطي إيحاء بفتل (برم) الحبل
            float visualTwistMultiplier = -0.5f; // يمكنك تكبير أو تصغير هذا الرقم لضبط سرعة وشكل الفتلة
            ropeRenderer.material.mainTextureOffset = new Vector2(twistAngle * visualTwistMultiplier, 0);
        }
    }

    void UpdateSharedData()
    {
        if (data == null) return;

        data.totalMass = emptyBucketMass + currentPaintMass;
        data.currentPaintMass = currentPaintMass;
        data.effectiveLength = effectiveLength;
        data.fillRatio = maxPaintMass > 0 ? currentPaintMass / maxPaintMass : 0;

        data.theta = theta;
        data.phi = phi;

        data.angularVelocityTheta = thetaVelocity;
        data.angularVelocityPhi = phiVelocity;

        data.linearVelocity = velocity;
        data.currentPosition = transform.position;
    }
}