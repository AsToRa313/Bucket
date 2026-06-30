using UnityEngine;

/// <summary>
/// مدير محاكاة سوائل على GPU بصيغة Clavet (Double-Density Relaxation).
/// يوفّر GetPositionBuffer/GetVelocityBuffer/GetParticleCount للـ SPHRenderer.
/// </summary>
public class SPHSimulation1 : MonoBehaviour
{
    [Header("Compute Shader")]
    public ComputeShader compute;

    [Header("Particle Settings")]
    public int numParticles = 4000;
    [Range(1, 8)]
    public int iterations = 2;

    [Header("Fluid Parameters (Clavet)")]
    public float smoothingRadius = 0.08f;
    [Tooltip("كثافة التوازن — احسبها تلقائياً مستحسن")]
    public bool autoRestDensity = true;
    public float restDensity = 10f;
    public float stiffness = 1.0f;
    public float nearStiffness = 2.0f;
    [Tooltip("تخميد السرعة (1=بدون، 0.95-0.99=استقرار، يمتص الدوامة)")]
    [Range(0.9f, 1f)]
    public float velocityDamping = 0.98f;
    public float gravity = 9.81f;
    [Tooltip("سقف قوة القصور من حركة السطل (يمنع انفجار السائل عند السحب المفاجئ)")]
    public float maxInertiaAccel = 30f;
    [Range(0f, 1f)]
    public float collisionDamping = 0.4f;
    [Tooltip("ارتداد الجزيئات عن الجدران (0=تلتصق، 0.1-0.3=ارتداد خفيف). قيمة عالية تسبب تسارع عند الحواف")]
    [Range(0f, 0.8f)]
    public float wallRestitution = 0.1f;

    [Header("Bucket Integration")]
    public SphericalPendulumMath pendulum;
    public Transform bucketTransform;
    public float bucketRadius = 0.25f;
    public float bucketHeight = 0.6f;
    [Range(0f, 1f)]
    public float initialFillRatio = 0.8f;

    [System.Serializable]
    public struct DrainHole
    {
        [Tooltip("موقع الثقب بإحداثيات السطل المحلية (المركز=0)")]
        public Vector3 localPosition;
        [Tooltip("نصف قطر منطقة الثقب")]
        public float radius;
    }

    [Header("Drain Holes (ثقوب التصريف)")]
    [Tooltip("الثقوب التي يخرج منها الدهان. أضف/احذف من هنا.")]
    public DrainHole[] holes = new DrainHole[]
    {
        // ثقب بالقاع
        new DrainHole { localPosition = new Vector3(0f, -0.3f, 0f), radius = 0.05f },
        // ثقبان جانبيان
        new DrainHole { localPosition = new Vector3(0.25f, -0.1f, 0f), radius = 0.04f },
        new DrainHole { localPosition = new Vector3(-0.25f, -0.1f, 0f), radius = 0.04f },
    };

    [Header("Vortex (دوامة التصريف)")]
    [Tooltip("تفعيل دوامة حول الثقوب")]
    public bool enableVortex = true;
    [Tooltip("نطاق تأثير الدوامة حول الثقب (مضاعف لنصف القطر)")]
    public float vortexRange = 4f;
    [Tooltip("قوة الجذب نحو الثقب")]
    public float vortexPull = 2f;
    [Tooltip("قوة الالتفاف حول الثقب")]
    public float vortexSpin = 3f;

    [Header("Visualization")]
    [Tooltip("لوّن الجزيئات بألوان ثابتة حسب موقعها الابتدائي (يكشف الدوران/الدوامة)")]
    public bool useFixedColors = false;

    [Header("Canvas Painting (الرسم على اللوحة)")]
    [Tooltip("اللوحة التي ترسم عليها القطرات الساقطة")]
    public CanvasPainter canvas;
    [Tooltip("مستوى ارتفاع اللوحة (Y بالعالم) - القطرة ترسم لما تنزل تحته")]
    public float canvasY = -1.5f;
    [Tooltip("نصف حجم اللوحة أفقياً (X,Z) لفحص دخول القطرة")]
    public Vector2 canvasHalfSize = new Vector2(1f, 1f);
    [Tooltip("لون الطلاء على اللوحة")]
    public Color paintColor = new Color(0.8f, 0.1f, 0.1f, 1f);
    [Tooltip("كل كم فريم نفحص القطرات الساقطة (أعلى = أداء أفضل)")]
    public int canvasCheckInterval = 2;

    ComputeBuffer positionBuffer;
    ComputeBuffer prevPositionBuffer;
    ComputeBuffer velocityBuffer;
    ComputeBuffer stateBuffer;
    ComputeBuffer holeBuffer;
    ComputeBuffer colorBuffer;

    ComputeBuffer cellCountsBuffer;
    ComputeBuffer cellStartBuffer;
    ComputeBuffer cellEndBuffer;
    ComputeBuffer sortedIndicesBuffer;
    ComputeBuffer particleCellIndexBuffer;

    int gridResolution, numCells;
    Vector3 gridMin;

    int kGravity, kClearGrid, kCount, kPrefix, kScatter, kRelax, kVelocity, kCheckHoles, kVortex;
    const int THREADS = 256;
    Vector3 prevBucketVel = Vector3.zero;
    bool ready = false;

    // مصفوفات قراءة القطرات للوحة (تُعاد استخدامها)
    Vector3[] readPositions;
    Vector3[] readVelocities;
    uint[] readStates;

    void Start()
    {
        if (compute == null) { Debug.LogError("compute فارغ!"); return; }
        if (bucketTransform == null && pendulum != null)
            bucketTransform = pendulum.transform;

        SetupGrid();
        CreateBuffers();
        InitializeParticles();
        CacheKernels();
        ready = true;
        Debug.Log($"SPHSimulation جاهز: {numParticles} جسيم، شبكة {gridResolution}، restDensity={restDensity:F2}");
    }

    void SetupGrid()
    {
        float worldSize = Mathf.Max(bucketRadius * 2f, bucketHeight) * 2f;
        gridResolution = Mathf.Max(4, Mathf.CeilToInt(worldSize / smoothingRadius));
        // سقف صارم: PrefixSum تسلسلي، شبكة كبيرة تخنق الأداء
        // 16³=4096 خلية معقول للمسح التسلسلي
        gridResolution = Mathf.Min(gridResolution, 16);
        numCells = gridResolution * gridResolution * gridResolution;
    }

    void UpdateGridOrigin()
    {
        Vector3 c = bucketTransform != null ? bucketTransform.position : Vector3.zero;
        float half = gridResolution * smoothingRadius * 0.5f;
        gridMin = c - new Vector3(half, half, half);
    }

    void CreateBuffers()
    {
        positionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        prevPositionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        velocityBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        stateBuffer = new ComputeBuffer(numParticles, sizeof(uint));

        int holeCount = Mathf.Max(1, holes.Length);
        holeBuffer = new ComputeBuffer(holeCount, sizeof(float) * 4);
        UploadHoles();

        cellCountsBuffer = new ComputeBuffer(numCells, sizeof(uint));
        cellStartBuffer = new ComputeBuffer(numCells, sizeof(uint));
        cellEndBuffer = new ComputeBuffer(numCells, sizeof(uint));
        sortedIndicesBuffer = new ComputeBuffer(numParticles, sizeof(uint));
        particleCellIndexBuffer = new ComputeBuffer(numParticles, sizeof(uint));
    }

    void UploadHoles()
    {
        int n = Mathf.Max(1, holes.Length);
        var data = new Vector4[n];
        for (int i = 0; i < holes.Length; i++)
            data[i] = new Vector4(holes[i].localPosition.x, holes[i].localPosition.y,
                                  holes[i].localPosition.z, holes[i].radius);
        // لو ما في ثقوب، ثقب وهمي بنصف قطر صفر (لا يؤثّر)
        if (holes.Length == 0)
            data[0] = new Vector4(0, -999f, 0, 0f);
        holeBuffer.SetData(data);
    }

    void InitializeParticles()
    {
        float fill = pendulum != null ? pendulum.GetFillRatio() : initialFillRatio;
        fill = Mathf.Clamp01(fill <= 0f ? initialFillRatio : fill);

        Vector3 center = bucketTransform != null ? bucketTransform.position : Vector3.zero;
        Quaternion rot = bucketTransform != null ? bucketTransform.rotation : Quaternion.identity;

        var positions = new Vector3[numParticles];
        var velocities = new Vector3[numParticles];
        float bottomY = -bucketHeight * 0.5f;
        float fillHeight = bucketHeight * fill;

        for (int i = 0; i < numParticles; i++)
        {
            float r = bucketRadius * 0.9f * Mathf.Sqrt(Random.value);
            float a = Random.Range(0f, Mathf.PI * 2f);
            float y = bottomY + Random.Range(0f, fillHeight);
            Vector3 local = new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
            positions[i] = center + rot * local;
            velocities[i] = Vector3.zero;
        }

        positionBuffer.SetData(positions);
        prevPositionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);

        // كل الجزيئات تبدأ داخل السطل (state = 0)
        var states = new uint[numParticles];
        stateBuffer.SetData(states);

        // ألوان ثابتة حسب الزاوية حول المحور العمودي (تكشف الدوران كحلزون)
        colorBuffer = new ComputeBuffer(numParticles, sizeof(float) * 4);
        var colors = new Vector4[numParticles];
        for (int i = 0; i < numParticles; i++)
        {
            Vector3 local = Quaternion.Inverse(rot) * (positions[i] - center);
            float angle = Mathf.Atan2(local.z, local.x); // -π..π
            float hue = (angle + Mathf.PI) / (2f * Mathf.PI); // 0..1
            Color c = Color.HSVToRGB(hue, 0.85f, 1f);
            colors[i] = new Vector4(c.r, c.g, c.b, 1f);
        }
        colorBuffer.SetData(colors);

        if (autoRestDensity)
        {
            restDensity = EstimateRestDensity(positions);
            Debug.Log($"[SPH] restDensity محسوبة تلقائياً = {restDensity:F2}");
        }
    }

    float EstimateRestDensity(Vector3[] positions)
    {
        float h = smoothingRadius;
        float h2 = h * h;
        int samples = Mathf.Min(150, numParticles);
        float sum = 0f;
        for (int si = 0; si < samples; si++)
        {
            int i = (int)((long)si * numParticles / samples);
            float density = 0f;
            for (int j = 0; j < numParticles; j++)
            {
                if (j == i) continue;
                float sqr = (positions[j] - positions[i]).sqrMagnitude;
                if (sqr < h2)
                {
                    float q = 1f - Mathf.Sqrt(sqr) / h;
                    density += q * q;
                }
            }
            sum += density;
        }
        return sum / samples;
    }

    void CacheKernels()
    {
        kGravity = compute.FindKernel("ApplyGravity");
        kClearGrid = compute.FindKernel("ClearGrid");
        kCount = compute.FindKernel("CountParticles");
        kPrefix = compute.FindKernel("PrefixSumNaive");
        kScatter = compute.FindKernel("ScatterParticles");
        kRelax = compute.FindKernel("DoubleDensityRelax");
        kVelocity = compute.FindKernel("ComputeVelocity");
        kCheckHoles = compute.FindKernel("CheckHoles");
        kVortex = compute.FindKernel("ApplyVortex");

        int[] particleKernels = { kGravity, kCount, kScatter, kRelax, kVelocity, kCheckHoles, kVortex };
        foreach (int k in particleKernels)
        {
            compute.SetBuffer(k, "Positions", positionBuffer);
            compute.SetBuffer(k, "PrevPositions", prevPositionBuffer);
            compute.SetBuffer(k, "Velocities", velocityBuffer);
            compute.SetBuffer(k, "States", stateBuffer);
            compute.SetBuffer(k, "Holes", holeBuffer);
            compute.SetBuffer(k, "CellCounts", cellCountsBuffer);
            compute.SetBuffer(k, "CellStart", cellStartBuffer);
            compute.SetBuffer(k, "CellEnd", cellEndBuffer);
            compute.SetBuffer(k, "SortedIndices", sortedIndicesBuffer);
            compute.SetBuffer(k, "ParticleCellIndex", particleCellIndexBuffer);
        }
        int[] gridKernels = { kClearGrid, kPrefix };
        foreach (int k in gridKernels)
        {
            compute.SetBuffer(k, "CellCounts", cellCountsBuffer);
            compute.SetBuffer(k, "CellStart", cellStartBuffer);
            compute.SetBuffer(k, "CellEnd", cellEndBuffer);
        }
    }

    void FixedUpdate()
    {
        if (!ready) return;

        UpdateGridOrigin();

        Vector3 bucketVel = pendulum != null ? pendulum.GetVelocityVector() : Vector3.zero;
        Vector3 bucketAccel = (bucketVel - prevBucketVel) / Time.fixedDeltaTime;
        prevBucketVel = bucketVel;

        // سقف لقوة القصور: يمنع انفجار السائل عند قفزة مفاجئة بموقع السطل
        // (مثل السحب اليدوي بالماوس)
        float maxAccel = maxInertiaAccel;
        if (bucketAccel.magnitude > maxAccel)
            bucketAccel = bucketAccel.normalized * maxAccel;

        Vector3 externalAccel = -bucketAccel;

        SetConstants(Time.fixedDeltaTime, externalAccel);

        int pGroups = Mathf.CeilToInt(numParticles / (float)THREADS);
        int cGroups = Mathf.CeilToInt(numCells / (float)THREADS);

        compute.Dispatch(kGravity, pGroups, 1, 1);

        for (int it = 0; it < iterations; it++)
        {
            compute.Dispatch(kClearGrid, cGroups, 1, 1);
            compute.Dispatch(kCount, pGroups, 1, 1);
            compute.Dispatch(kPrefix, 1, 1, 1);
            compute.Dispatch(kScatter, pGroups, 1, 1);
            compute.Dispatch(kRelax, pGroups, 1, 1);
        }

        compute.Dispatch(kVelocity, pGroups, 1, 1);

        // دوامة التصريف: جذب + التفاف حول الثقوب
        if (enableVortex)
            compute.Dispatch(kVortex, pGroups, 1, 1);

        // فحص الثقوب: تحويل الجزيئات القريبة لقطرات حرة
        compute.Dispatch(kCheckHoles, pGroups, 1, 1);

        // معالجة القطرات الواصلة للوحة (قراءة async غير محجوبة - لا تجمّد الإطار)
        if (canvas != null)
            RequestCanvasReadback();
    }

    // قراءة غير متزامنة: نطلب البيانات وتصل بعد إطارات بدون تجميد الجهاز
    bool readbackPending = false;
    void RequestCanvasReadback()
    {
        if (readbackPending) return;   // طلب واحد فقط في كل مرة
        if (Time.frameCount % Mathf.Max(1, canvasCheckInterval) != 0) return;

        readbackPending = true;
        UnityEngine.Rendering.AsyncGPUReadback.Request(positionBuffer, (req) =>
        {
            OnPositionsReadback(req);
        });
    }

    void OnPositionsReadback(UnityEngine.Rendering.AsyncGPUReadbackRequest req)
    {
        readbackPending = false;
        if (!ready || req.hasError || canvas == null) return;

        var posData = req.GetData<Vector3>();
        int count = Mathf.Min(posData.Length, numParticles);

        // نقرأ الحالات بشكل متزامن (buffer صغير: uint واحد لكل جسيم)
        if (readStates == null || readStates.Length != numParticles)
            readStates = new uint[numParticles];
        stateBuffer.GetData(readStates);

        bool anyDrawn = false;
        for (int i = 0; i < count; i++)
        {
            // state=3 يعني القطرة وصلت اللوحة (الـ compute علّمها)
            if (readStates[i] != 3) continue;

            Vector3 p = posData[i];
            // سرعة صفر => بقعة نظيفة بحجم القطرة (بلا مسار ممتد)
            canvas.Splat(p, Vector3.zero, paintColor);

            readStates[i] = 2;   // مستهلكة نهائياً
            anyDrawn = true;
        }

        // نكتب الحالات فقط (buffer صغير، رخيص) لتثبيت "مستهلكة"
        if (anyDrawn)
            stateBuffer.SetData(readStates);
    }

    void SetConstants(float dt, Vector3 externalAccel)
    {
        compute.SetInt("numParticles", numParticles);
        compute.SetFloat("deltaTime", dt);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("smoothingRadius", smoothingRadius);

        compute.SetFloat("restDensity", restDensity);
        compute.SetFloat("stiffness", stiffness);
        compute.SetFloat("nearStiffness", nearStiffness);
        compute.SetFloat("viscosityBeta", 0f);
        compute.SetFloat("viscositySigma", 0f);
        compute.SetFloat("velocityDamping", velocityDamping);

        Vector3 c = bucketTransform != null ? bucketTransform.position : Vector3.zero;
        Quaternion q = bucketTransform != null ? bucketTransform.rotation : Quaternion.identity;
        compute.SetVector("bucketCenter", c);
        compute.SetVector("bucketRotation", new Vector4(q.x, q.y, q.z, q.w));
        compute.SetFloat("bucketRadius", bucketRadius);
        compute.SetFloat("bucketHeight", bucketHeight);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("wallRestitution", wallRestitution);
        compute.SetVector("externalAccel", externalAccel);

        compute.SetInt("gridResolution", gridResolution);
        compute.SetFloat("gridCellSize", smoothingRadius);
        compute.SetVector("gridMin", gridMin);

        compute.SetInt("numHoles", Mathf.Max(1, holes.Length));

        compute.SetFloat("vortexRange", vortexRange);
        compute.SetFloat("vortexPull", vortexPull);
        compute.SetFloat("vortexSpin", vortexSpin);

        // ثوابت اللوحة (لكشف وصول القطرات داخل الـ compute)
        bool canvasOn = canvas != null;
        compute.SetInt("canvasEnabled", canvasOn ? 1 : 0);
        compute.SetFloat("canvasY", canvasY);
        Vector3 cc = canvasOn ? canvas.transform.position : Vector3.zero;
        compute.SetVector("canvasCenter", cc);
        compute.SetVector("canvasHalfSize", canvasHalfSize);
    }

    public int GetParticleCount() => numParticles;
    public ComputeBuffer GetPositionBuffer() => positionBuffer;
    public ComputeBuffer GetVelocityBuffer() => velocityBuffer;
    public ComputeBuffer GetColorBuffer() => colorBuffer;
    public ComputeBuffer GetStateBuffer() => stateBuffer;
    public bool UseFixedColors() => useFixedColors;

    void OnDestroy()
    {
        positionBuffer?.Release();
        prevPositionBuffer?.Release();
        velocityBuffer?.Release();
        stateBuffer?.Release();
        holeBuffer?.Release();
        colorBuffer?.Release();
        cellCountsBuffer?.Release();
        cellStartBuffer?.Release();
        cellEndBuffer?.Release();
        sortedIndicesBuffer?.Release();
        particleCellIndexBuffer?.Release();
    }

    // رسم الثقوب في محرر Scene للمساعدة في وضعها
    void OnDrawGizmosSelected()
    {
        if (holes != null)
        {
            Transform t = bucketTransform != null ? bucketTransform : transform;
            Gizmos.color = Color.red;
            foreach (var hole in holes)
            {
                Vector3 worldPos = t.position + t.rotation * hole.localPosition;
                Gizmos.DrawWireSphere(worldPos, hole.radius);
            }
        }

        // مستوى اللوحة وحدودها (أصفر)
        if (canvas != null)
        {
            Gizmos.color = Color.yellow;
            Vector3 c = canvas.transform.position;
            c.y = canvasY;
            Vector3 size = new Vector3(canvasHalfSize.x * 2f, 0.01f, canvasHalfSize.y * 2f);
            Gizmos.DrawWireCube(c, size);
        }
    }
}