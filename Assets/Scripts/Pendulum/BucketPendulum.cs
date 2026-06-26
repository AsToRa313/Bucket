using UnityEngine;

public class BucketPendulum : MonoBehaviour
{
    [Header("=== نقطة التعليق ===")]
    public Transform pivotPoint;

    [Header("=== خصائص الحبل ===")]
    public float ropeLength = 3f;
    public float ropeElasticity = 0f; // مرونة الحبل (0 = صلب)

    [Header("=== خصائص الحركة ===")]
    [Range(0f, 180f)]
    public float startAngle = 45f;
    public float initialAngularVelocity = 0f;

    [Header("=== خصائص البيئة ===")]
    public float gravity = 9.81f;
    [Range(0f, 0.1f)]
    public float airResistance = 0.005f;
    [Range(0f, 0.1f)]
    public float friction = 0.002f;

    [Header("=== حالة المحاكاة ===")]
    public bool isSimulating = false;
    public float currentAngleDegrees;
    public float currentAngularVelocity;

    // متغيرات داخلية
    private float angle;
    private float angularVel;
    private LineRenderer ropeRenderer;

    void Start()
    {
        SetupRopeRenderer();
        ResetPendulum();
    }

    void SetupRopeRenderer()
    {
        // ارسم الحبل بصرياً
        ropeRenderer = gameObject.AddComponent<LineRenderer>();
        ropeRenderer.material = new Material(Shader.Find("Sprites/Default"));
        ropeRenderer.startColor = Color.white;
        ropeRenderer.endColor = Color.white;
        ropeRenderer.startWidth = 0.05f;
        ropeRenderer.endWidth = 0.05f;
        ropeRenderer.positionCount = 2;
    }

    void Update()
    {
        if (isSimulating)
        {
            StepPhysics(Time.deltaTime);
        }

        UpdateRopeVisual();
        UpdateDebugInfo();
    }

    void StepPhysics(float dt)
    {
        // قانون البندول البسيط مع الاحتكاك ومقاومة الهواء
        // α = -(g/L) * sin(θ) - damping * ω
        float totalDamping = airResistance + friction;
        float angularAcc = -(gravity / ropeLength) * Mathf.Sin(angle)
                           - totalDamping * angularVel;

        // تكامل Verlet للدقة
        angularVel += angularAcc * dt;
        angle += angularVel * dt;
    }

    void UpdateRopeVisual()
    {
        if (pivotPoint == null) return;

        // حساب موقع السطل
        float bucketX = pivotPoint.position.x + Mathf.Sin(angle) * ropeLength;
        float bucketY = pivotPoint.position.y - Mathf.Cos(angle) * ropeLength;
        float bucketZ = pivotPoint.position.z;

        transform.position = new Vector3(bucketX, bucketY, bucketZ);

        // تحديث الحبل
        ropeRenderer.SetPosition(0, pivotPoint.position);
        ropeRenderer.SetPosition(1, transform.position);
    }

    void UpdateDebugInfo()
    {
        currentAngleDegrees = angle * Mathf.Rad2Deg;
        currentAngularVelocity = angularVel;
    }

    // دوال عامة للتحكم
    public void StartSimulation()
    {
        isSimulating = true;
    }

    public void StopSimulation()
    {
        isSimulating = false;
    }

    public void ResetPendulum()
    {
        angle = startAngle * Mathf.Deg2Rad;
        angularVel = initialAngularVelocity;
        isSimulating = false;
        UpdateRopeVisual();
    }

    // إرجاع الموقع للسائل
    public Vector3 GetBucketPosition() => transform.position;
    public float GetBucketSpeed() => Mathf.Abs(angularVel * ropeLength);

    void OnDrawGizmos()
    {
        if (pivotPoint == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pivotPoint.position, transform.position);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}
