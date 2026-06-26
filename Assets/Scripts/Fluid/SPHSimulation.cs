using UnityEngine;
using Seb.Helpers;
using Seb.GPUSorting;
using Unity.Mathematics;

public class SPHSimulation : MonoBehaviour
{
    [Header("عدد الجزيئات")]
    [Range(500, 20000)]
    public int numParticles = 5000;

    [Header("إعدادات SPH")]
    public float smoothingRadius        = 0.10f;
    [Tooltip("تُحسب تلقائياً عند البداية — لا تعدّليها إلا إذا عرفتِ ما تفعلين")]
    public float targetDensity          = 0f;
    public float pressureMultiplier     = 50f;
    public float nearPressureMultiplier = 10f;
    public float viscosityStrength      = 0.08f;
    [Range(0f, 1f)]
    public float collisionDamping       = 0.35f;
    [Range(0.8f, 1f)]
    public float velocityDamping        = 0.992f;
    [Range(0f, 0.5f)]
    public float wallFriction           = 0.08f;

    [Header("السطل")]
    [Tooltip("نصف حجم السطل — اضبطيه حتى يطابق الـ Gizmo الأزرق مع السطل الفعلي")]
    public Vector3 bucketHalfSize = new Vector3(0.3f, 0.4f, 0.3f);
    public bool    openTop        = true;

    [Header("الوقت")]
    public float timeScale     = 1f;
    [Range(1, 10)]
    public int   stepsPerFrame = 6;

    [Header("المراجع")]
    [Tooltip("اسحبي هون الـ Bucket GameObject")]
    public Transform     bucketTransform;
    public ComputeShader sphCompute;

    ComputeBuffer positionBuffer;
    ComputeBuffer predictedPositionsBuffer;
    ComputeBuffer velocityBuffer;
    ComputeBuffer densityBuffer;
    ComputeBuffer sortTarget_Positions;
    ComputeBuffer sortTarget_Predicted;
    ComputeBuffer sortTarget_Velocities;
    SpatialHash   spatialHash;

    const int k_External    = 0;
    const int k_SpatialHash = 1;
    const int k_Reorder     = 2;
    const int k_ReorderBack = 3;
    const int k_Density     = 4;
    const int k_Pressure    = 5;
    const int k_Viscosity   = 6;
    const int k_Update      = 7;

    bool    ready = false;
    Vector3 lastBucketPos;
    Vector3 bucketVelocity;

    public ComputeBuffer GetPositionBuffer() => positionBuffer;
    public ComputeBuffer GetVelocityBuffer() => velocityBuffer;
    public int           GetParticleCount()  => numParticles;

    Transform BucketT => bucketTransform != null ? bucketTransform : transform;

    void Start()
{
    if (sphCompute == null) { Debug.LogError("sphCompute فارغ!"); return; }

    lastBucketPos = BucketT.position;

    InitBuffers();
    
    // ← أضيفي هاد: استنى frame واحد قبل ما تبدأ
    StartCoroutine(DelayedInit());
}

System.Collections.IEnumerator DelayedInit()
{
    // استنى حتى PendulumBucket يحط السطل بموقعه الصحيح
    yield return new WaitForEndOfFrame();
    yield return new WaitForEndOfFrame();
    
    lastBucketPos = BucketT.position;
    InitParticles();
    CalibrateTargetDensity();
    BindBuffers();
    ready = true;
    
    Debug.Log($"SPH جاهز — السطل عند {BucketT.position} | {numParticles} جزيء");
}
    void InitBuffers()
    {
        spatialHash              = new SpatialHash(numParticles);
        positionBuffer           = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        predictedPositionsBuffer = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        velocityBuffer           = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        densityBuffer            = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
        sortTarget_Positions     = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        sortTarget_Predicted     = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
        sortTarget_Velocities    = ComputeHelper.CreateStructuredBuffer<float3>(numParticles);
    }

    void InitParticles()
    {
        var positions  = new float3[numParticles];
        var velocities = new float3[numParticles];

        Vector3    center = BucketT.position;
        Quaternion rot    = BucketT.rotation;
        Vector3    half   = bucketHalfSize;
        Vector3    scale  = BucketT.lossyScale;
        Vector3    halfWS = new Vector3(half.x * scale.x, half.y * scale.y, half.z * scale.z);

        // المسافة بين الجزيئات = نصف نصف القطر — قاعدة SPH المستقرة
        float spacing = smoothingRadius * 0.5f;
        float margin  = spacing * 0.5f;

        int nx = Mathf.Max(2, Mathf.FloorToInt((halfWS.x * 2f - margin * 2f) / spacing) + 1);
        int ny = Mathf.Max(2, Mathf.FloorToInt((halfWS.y * 2f - margin * 2f) / spacing) + 1);
        int nz = Mathf.Max(2, Mathf.FloorToInt((halfWS.z * 2f - margin * 2f) / spacing) + 1);

        int gridCapacity = nx * ny * nz;
        if (gridCapacity < numParticles)
            Debug.LogWarning($"SPH: numParticles ({numParticles}) أكبر من سعة الشبكة ({gridCapacity}) — رح يتكدسوا بالمركز!");

        int idx = 0;
        for (int iy = 0; iy < ny && idx < numParticles; iy++)
        for (int ix = 0; ix < nx && idx < numParticles; ix++)
        for (int iz = 0; iz < nz && idx < numParticles; iz++)
        {
            float3 local = new float3(
                -halfWS.x + margin + ix * spacing,
                -halfWS.y + margin + iy * spacing,
                -halfWS.z + margin + iz * spacing
            );
            positions[idx]  = (float3)(center + rot * (Vector3)local);
            velocities[idx] = float3.zero;
            idx++;
        }
        while (idx < numParticles)
        {
            // لا نكدس الجزيئات الزائدة في المركز — نوزّعهم بشكل عشوائي داخل السطل
            float3 randLocal = new float3(
                UnityEngine.Random.Range(-halfWS.x + margin, halfWS.x - margin),
                UnityEngine.Random.Range(-halfWS.y + margin, halfWS.y - margin),
                UnityEngine.Random.Range(-halfWS.z + margin, halfWS.z - margin)
            );
            positions[idx]  = (float3)(center + rot * (Vector3)randLocal);
            velocities[idx] = float3.zero;
            idx++;
        }

        positionBuffer.SetData(positions);
        predictedPositionsBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);
    }

    /// <summary>
    /// يحسب الكثافة النظرية عند التوازن ويضبط targetDensity عليها.
    /// هذا يمنع الانفجار الناتج عن فرق بين الكثافة الفعلية والمستهدفة.
    /// </summary>
    void CalibrateTargetDensity()
    {
        float h = smoothingRadius;
        float s = h * 0.5f;
        float k2 = 15f / (2f * Mathf.PI * Mathf.Pow(h, 5));

        float density = k2 * h * h; // ذاتي عند dst=0

        int range = Mathf.CeilToInt(h / s);
        for (int dx = -range; dx <= range; dx++)
        for (int dy = -range; dy <= range; dy++)
        for (int dz = -range; dz <= range; dz++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            float dst = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) * s;
            if (dst < h)
            {
                float v = h - dst;
                density += v * v * k2;
            }
        }

        // 100.5% من الكثافة النظرية — ضغط طفيف يمنع التمدد والتسرب
        targetDensity = density * 1.005f;
    }

    void BindBuffers()
    {
        Set(k_External,    ("Positions",positionBuffer),("PredictedPositions",predictedPositionsBuffer),("Velocities",velocityBuffer));
        Set(k_SpatialHash, ("PredictedPositions",predictedPositionsBuffer),("SpatialKeys",spatialHash.SpatialKeys),("SpatialOffsets",spatialHash.SpatialOffsets));
        Set(k_Reorder,     ("Positions",positionBuffer),("PredictedPositions",predictedPositionsBuffer),("Velocities",velocityBuffer),
                           ("SortTarget_Positions",sortTarget_Positions),("SortTarget_PredictedPositions",sortTarget_Predicted),
                           ("SortTarget_Velocities",sortTarget_Velocities),("SortedIndices",spatialHash.SpatialIndices));
        Set(k_ReorderBack, ("Positions",positionBuffer),("PredictedPositions",predictedPositionsBuffer),("Velocities",velocityBuffer),
                           ("SortTarget_Positions",sortTarget_Positions),("SortTarget_PredictedPositions",sortTarget_Predicted),
                           ("SortTarget_Velocities",sortTarget_Velocities));
        Set(k_Density,     ("PredictedPositions",predictedPositionsBuffer),("Densities",densityBuffer),("SpatialKeys",spatialHash.SpatialKeys),("SpatialOffsets",spatialHash.SpatialOffsets));
        Set(k_Pressure,    ("PredictedPositions",predictedPositionsBuffer),("Densities",densityBuffer),("Velocities",velocityBuffer),("SpatialKeys",spatialHash.SpatialKeys),("SpatialOffsets",spatialHash.SpatialOffsets));
        Set(k_Viscosity,   ("PredictedPositions",predictedPositionsBuffer),("Densities",densityBuffer),("Velocities",velocityBuffer),("SpatialKeys",spatialHash.SpatialKeys),("SpatialOffsets",spatialHash.SpatialOffsets));
        Set(k_Update,      ("Positions",positionBuffer),("Velocities",velocityBuffer));

        sphCompute.SetInt("numParticles", numParticles);

        float r = smoothingRadius;
        sphCompute.SetFloat("K_SpikyPow2",     15f/(2f*Mathf.PI*Mathf.Pow(r,5)));
        sphCompute.SetFloat("K_SpikyPow3",     15f/(Mathf.PI*Mathf.Pow(r,6)));
        sphCompute.SetFloat("K_SpikyPow2Grad", 15f/(Mathf.PI*Mathf.Pow(r,5)));
        sphCompute.SetFloat("K_SpikyPow3Grad", 45f/(Mathf.PI*Mathf.Pow(r,6)));
    }

    void Set(int k, params (string n, ComputeBuffer b)[] p)
    { foreach (var (n,b) in p) sphCompute.SetBuffer(k,n,b); }

    void FixedUpdate()
    {
        if (!ready) return;
        float frameDt = Mathf.Max(Time.fixedDeltaTime * timeScale, 0.0001f);
        float subDt   = Mathf.Min(frameDt / stepsPerFrame, 0.005f);
        for (int i = 0; i < stepsPerFrame; i++)
            Step(subDt);
        
    }

    void Step(float dt)
{
    // 1. أولاً: جلب بيانات الموقع والدوران والحجم من السطل الحالي
    Vector3 bPos   = BucketT.position;
    Quaternion bRot  = BucketT.rotation;
    Vector3 bScale = BucketT.lossyScale;

    // 2. ثانياً: حساب أو قراءة سرعة السطل بأمان لمنع انفجار الجزيئات
    var bucketPhysics = BucketT.GetComponent<IBucketPhysics>();
    if (bucketPhysics != null)
    {
        bucketVelocity = bucketPhysics.GetVelocityVector();
    }
    else
    {
        bucketVelocity = (bPos - lastBucketPos) / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
    }

    // 3. ثالثاً: تقييد السرعة القصوى لحماية السائل من السحب العنيف بالماوس
    if (bucketVelocity.magnitude > 15f)
    {
        bucketVelocity = bucketVelocity.normalized * 15f;
    }
    
    // حفظ الموقع الحالي للإطار القادم
    lastBucketPos = bPos;

    // 4. رابعاً: إرسال كل البيانات المحدثة إلى كرت الشاشة (Compute Shader)
    sphCompute.SetVector("bucketPos",   new Vector4(bPos.x, bPos.y, bPos.z, 0));
    sphCompute.SetVector("bucketHalf",  new Vector4(bucketHalfSize.x * bScale.x, bucketHalfSize.y * bScale.y, bucketHalfSize.z * bScale.z, 0));
    sphCompute.SetVector("bucketRot",   new Vector4(bRot.x, bRot.y, bRot.z, bRot.w));
    sphCompute.SetVector("bucketScale", new Vector4(bScale.x, bScale.y, bScale.z, 0));
    sphCompute.SetVector("bucketVel",   new Vector4(bucketVelocity.x, bucketVelocity.y, bucketVelocity.z, 0));

    sphCompute.SetFloat("deltaTime",              dt);
    sphCompute.SetFloat("smoothingRadius",        smoothingRadius);
    sphCompute.SetFloat("targetDensity",          targetDensity);
    sphCompute.SetFloat("pressureMultiplier",     pressureMultiplier);
    sphCompute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
    sphCompute.SetFloat("viscosityStrength",      viscosityStrength);
    sphCompute.SetFloat("collisionDamping",       collisionDamping);
    sphCompute.SetFloat("velocityDamping",        velocityDamping);
    sphCompute.SetFloat("wallFriction",           wallFriction);
    sphCompute.SetInt  ("openTop",                openTop ? 1 : 0);

    // 5. خامساً: تشغيل مراحل الحسابات المتتالية على الـ GPU
    ComputeHelper.Dispatch(sphCompute, numParticles, kernelIndex: k_External);
    ComputeHelper.Dispatch(sphCompute, numParticles, kernelIndex: k_SpatialHash);
    spatialHash.Run();
    ComputeHelper.Dispatch(sphCompute, numParticles, kernelIndex: k_Reorder);
    ComputeHelper.Dispatch(sphCompute, numParticles, kernelIndex: k_ReorderBack);
    ComputeHelper.Dispatch(sphCompute, numParticles, kernelIndex: k_Density);
    ComputeHelper.Dispatch(sphCompute, numParticles, kernelIndex: k_Pressure);
    if (viscosityStrength > 0f)
        ComputeHelper.Dispatch(sphCompute, numParticles, kernelIndex: k_Viscosity);
    ComputeHelper.Dispatch(sphCompute, numParticles, kernelIndex: k_Update);
}

    void OnDestroy()
    {
        positionBuffer?.Release(); predictedPositionsBuffer?.Release();
        velocityBuffer?.Release(); densityBuffer?.Release();
        sortTarget_Positions?.Release(); sortTarget_Predicted?.Release();
        sortTarget_Velocities?.Release(); spatialHash?.Release();
    }

    void OnDrawGizmos()
    {
        Vector3    p = BucketT != null ? BucketT.position : transform.position;
        Quaternion r = BucketT != null ? BucketT.rotation : transform.rotation;
        Vector3    s = BucketT != null ? BucketT.lossyScale : Vector3.one;
        Gizmos.color  = new Color(0.2f, 0.7f, 1f, 0.5f);
        Gizmos.matrix = Matrix4x4.TRS(p, r, s);
        Gizmos.DrawWireCube(Vector3.zero, bucketHalfSize * 2f);
        Gizmos.color  = Color.red;
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.DrawSphere(p, 0.04f);
    }
}
