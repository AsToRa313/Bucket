using UnityEngine;
using Seb.Helpers;   // نظام Sebastian: SpatialHash, ComputeHelper

/// <summary>
/// محاكاة سائل Clavet سريعة تصل لعشرات-مئات الآلاف من الجسيمات.
/// تستخدم محرّك الترتيب المكاني المحسّن من Sebastian Lague (SpatialHash + GPU CountSort)
/// مع خوارزمية Clavet (Double Density Relaxation).
///
/// المرحلة 1: سائل معزول في صندوق (بدون ثقوب/رسم) لاختبار الأداء.
///
/// المتطلّبات: مجلد Seb (ComputeHelper, SpatialHash, GPUCountSort, Scan,
/// وملفات الـ compute: CountSort, ScanTest, SpatialOffsets) يجب أن يكون في المشروع.
/// </summary>
public class FluidClavetSim : MonoBehaviour
{
    [Header("Compute Shader")]
    public ComputeShader compute;

    [Header("الجسيمات")]
    [Tooltip("عدد الجسيمات لكل محور (الإجمالي = هذا³). 40³=64000، 46³≈97000")]
    public int particlesPerAxis = 40;
    [Tooltip("تكرارات الاسترخاء (أعلى = أكثر استقراراً، أبطأ)")]
    public int iterations = 2;

    [Header("معاملات Clavet")]
    public float smoothingRadius = 0.2f;
    public float restDensity = 10f;
    public float stiffness = 0.5f;
    public float nearStiffness = 0.5f;
    public float gravity = 9.81f;
    [Range(0f, 1f)]
    public float collisionDamping = 0.4f;
    [Range(0.9f, 1f)]
    [Tooltip("تخميد عام للسرعة كل إطار (نفس آلية الدلو) - ضروري لوصول السائل للسكون")]
    public float velocityDamping = 0.98f;

    [Header("الصندوق")]
    public Vector3 boundsSize = new Vector3(6f, 8f, 6f);
    [Tooltip("منطقة توليد الجسيمات الابتدائية")]
    public Vector3 spawnSize = new Vector3(4f, 4f, 4f);
    public Vector3 spawnCentre = new Vector3(0f, 1f, 0f);
    public float jitter = 0.02f;

    [Header("وضع السطل (Bucket Mode)")]
    [Tooltip("تفعيل حدود السطل الأسطواني بدل الصندوق")]
    public bool useBucket = false;
    [Tooltip("كائن السطل (للحركة والقصور). اتركه فارغاً لسطل ثابت في المركز")]
    public Transform bucketTransform;
    [Tooltip("البندول (اختياري - للقصور من حركته)")]
    public MonoBehaviour pendulum;
    public float bucketRadius = 2f;
    public float bucketHeight = 4f;
    [Tooltip("سقف قصور حركة السطل (يمنع الانفجار)")]
    public float maxInertiaAccel = 30f;
    [Range(0f, 0.8f)]
    public float wallRestitution = 0.1f;

    [Header("العرض")]
    public Mesh particleMesh;
    public Material particleMaterial;
    public float particleSize = 0.1f;
    public Color fluidColor = new Color(0.2f, 0.5f, 0.95f, 1f);

    // عدد الجسيمات الفعلي
    int numParticles;

    // Buffers الأساسية
    ComputeBuffer positionBuffer;
    ComputeBuffer predictedPositionBuffer;
    ComputeBuffer velocityBuffer;
    ComputeBuffer densityBuffer;

    // Buffers إعادة الترتيب
    ComputeBuffer sortTargetPositionBuffer;
    ComputeBuffer sortTargetPredictedBuffer;
    ComputeBuffer sortTargetVelocityBuffer;

    // نظام الترتيب المكاني تبع Sebastian
    SpatialHash spatialHash;

    // عرض
    ComputeBuffer argsBuffer;
    Bounds renderBounds;

    // معرّفات الـ kernels
    int kExternalForces, kUpdateHash, kReorder, kReorderCopyBack;
    int kCalculateDensity, kRelax, kUpdatePositions;
    const int THREADS = 256;

    bool ready = false;

    // تتبّع حركة السطل (لحساب القصور)
    Vector3 prevBucketPos = Vector3.zero;
    Vector3 prevBucketVel = Vector3.zero;

    void Start()
    {
        if (compute == null) { Debug.LogError("Compute فارغ!"); return; }

        numParticles = particlesPerAxis * particlesPerAxis * particlesPerAxis;
        Debug.Log($"[FluidClavet] عدد الجسيمات = {numParticles}");

        CreateBuffers();
        SpawnParticles();
        CacheKernels();
        SetupRenderArgs();
        ready = true;
    }

    void CreateBuffers()
    {
        positionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        predictedPositionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        velocityBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        densityBuffer = new ComputeBuffer(numParticles, sizeof(float) * 2);

        sortTargetPositionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortTargetPredictedBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortTargetVelocityBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);

        // نظام الترتيب المكاني تبع Sebastian
        spatialHash = new SpatialHash(numParticles);
    }

    void SpawnParticles()
    {
        var positions = new Vector3[numParticles];
        var velocities = new Vector3[numParticles];

        int i = 0;
        for (int x = 0; x < particlesPerAxis; x++)
            for (int y = 0; y < particlesPerAxis; y++)
                for (int z = 0; z < particlesPerAxis; z++)
                {
                    float tx = x / (particlesPerAxis - 1f);
                    float ty = y / (particlesPerAxis - 1f);
                    float tz = z / (particlesPerAxis - 1f);

                    float px = (tx - 0.5f) * spawnSize.x + spawnCentre.x;
                    float py = (ty - 0.5f) * spawnSize.y + spawnCentre.y;
                    float pz = (tz - 0.5f) * spawnSize.z + spawnCentre.z;

                    Vector3 jit = Random.insideUnitSphere * jitter;
                    positions[i] = new Vector3(px, py, pz) + jit;
                    velocities[i] = Vector3.zero;
                    i++;
                }

        positionBuffer.SetData(positions);
        predictedPositionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);
    }

    void CacheKernels()
    {
        kExternalForces = compute.FindKernel("ExternalForces");
        kUpdateHash = compute.FindKernel("UpdateSpatialHash");
        kReorder = compute.FindKernel("Reorder");
        kReorderCopyBack = compute.FindKernel("ReorderCopyBack");
        kCalculateDensity = compute.FindKernel("CalculateDensity");
        kRelax = compute.FindKernel("DoubleDensityRelax");
        kUpdatePositions = compute.FindKernel("UpdatePositions");

        // ربط الـ buffers بكل الـ kernels التي تحتاجها
        int[] allKernels = { kExternalForces, kUpdateHash, kReorder, kReorderCopyBack,
                             kCalculateDensity, kRelax, kUpdatePositions };
        foreach (int k in allKernels)
        {
            compute.SetBuffer(k, "Positions", positionBuffer);
            compute.SetBuffer(k, "PredictedPositions", predictedPositionBuffer);
            compute.SetBuffer(k, "Velocities", velocityBuffer);
            compute.SetBuffer(k, "Densities", densityBuffer);
            compute.SetBuffer(k, "SpatialKeys", spatialHash.SpatialKeys);
            compute.SetBuffer(k, "SpatialOffsets", spatialHash.SpatialOffsets);
            compute.SetBuffer(k, "SortedIndices", spatialHash.SpatialIndices);
        }

        // buffers إعادة الترتيب (لـ Reorder فقط)
        compute.SetBuffer(kReorder, "SortTarget_Positions", sortTargetPositionBuffer);
        compute.SetBuffer(kReorder, "SortTarget_PredictedPositions", sortTargetPredictedBuffer);
        compute.SetBuffer(kReorder, "SortTarget_Velocities", sortTargetVelocityBuffer);
        compute.SetBuffer(kReorderCopyBack, "SortTarget_Positions", sortTargetPositionBuffer);
        compute.SetBuffer(kReorderCopyBack, "SortTarget_PredictedPositions", sortTargetPredictedBuffer);
        compute.SetBuffer(kReorderCopyBack, "SortTarget_Velocities", sortTargetVelocityBuffer);
    }

    void SetupRenderArgs()
    {
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        if (particleMesh != null)
        {
            args[0] = particleMesh.GetIndexCount(0);
            args[1] = (uint)numParticles;
            args[2] = particleMesh.GetIndexStart(0);
            args[3] = particleMesh.GetBaseVertex(0);
        }
        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(args);
        renderBounds = new Bounds(transform.position, boundsSize * 2f);

        // اربط الـ buffers بالماتيريال مرة واحدة (تبقى مربوطة - طريقة Sebastian)
        if (particleMaterial != null)
        {
            particleMaterial.SetBuffer("_PositionBuffer", positionBuffer);
            particleMaterial.SetBuffer("_VelocityBuffer", velocityBuffer);
        }
    }

    void FixedUpdate()
    {
        if (!ready) return;

        // توحيد مع خوارزمية الدلو (SPHSimulation1): الجاذبية مرة واحدة بـ dt كامل،
        // والحلقة iterations هي حل قيود تكراري للاسترخاء فقط (ليست تقسيماً زمنياً).
        float dt = Time.fixedDeltaTime;
        int groups = Mathf.CeilToInt(numParticles / (float)THREADS);

        SetConstants(dt);

        // 1. قوى خارجية (جاذبية) + تنبؤ بالموقع - مرة واحدة، مثل ApplyGravity بالدلو
        compute.Dispatch(kExternalForces, groups, 1, 1);

        // 2-6. حلقة الاسترخاء: إعادة بناء الشبكة + ترتيب + كثافة + إزاحة، عدة مرات
        //      (نفس فلسفة الدلو: تحسين تدريجي لدقة الكثافة، بلا تكرار للجاذبية)
        for (int it = 0; it < iterations; it++)
        {
            RunSimulationStep(dt, groups);
        }

        // 7. تحديث السرعة + الموقع النهائي + التصادم - مرة واحدة بعد الحلقة، مثل ComputeVelocity بالدلو
        compute.Dispatch(kUpdatePositions, groups, 1, 1);
    }

    void RunSimulationStep(float dt, int groups)
    {
        // 2. بناء المفاتيح المكانية
        compute.Dispatch(kUpdateHash, groups, 1, 1);

        // 3. الترتيب المكاني (محرّك Sebastian المتوازي)
        spatialHash.Run();

        // 4. إعادة ترتيب الجسيمات بالذاكرة
        compute.Dispatch(kReorder, groups, 1, 1);
        compute.Dispatch(kReorderCopyBack, groups, 1, 1);

        // 5. حساب الكثافة
        compute.Dispatch(kCalculateDensity, groups, 1, 1);

        // 6. الاسترخاء (Clavet) - نفس معادلة الدلو بالضبط
        compute.Dispatch(kRelax, groups, 1, 1);
    }

    void SetConstants(float dt)
    {
        compute.SetInt("numParticles", numParticles);
        compute.SetFloat("deltaTime", dt);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("velocityDamping", velocityDamping);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("restDensity", restDensity);
        compute.SetFloat("stiffness", stiffness);
        compute.SetFloat("nearStiffness", nearStiffness);
        compute.SetVector("boundsSize", boundsSize);

        // مصفوفات التحويل للصندوق (نمط Sebastian)
        Matrix4x4 localToWorld = Matrix4x4.TRS(transform.position, transform.rotation, boundsSize);
        compute.SetMatrix("localToWorld", localToWorld);
        compute.SetMatrix("worldToLocal", localToWorld.inverse);

        // --- ثوابت السطل الأسطواني ---
        compute.SetInt("useBucket", useBucket ? 1 : 0);
        compute.SetFloat("bucketRadius", bucketRadius);
        compute.SetFloat("bucketHeight", bucketHeight);
        compute.SetFloat("wallRestitution", wallRestitution);

        Vector3 bucketPos = bucketTransform != null ? bucketTransform.position : transform.position;
        Quaternion bucketRot = bucketTransform != null ? bucketTransform.rotation : Quaternion.identity;
        compute.SetVector("bucketCenter", bucketPos);
        compute.SetVector("bucketRotation", new Vector4(bucketRot.x, bucketRot.y, bucketRot.z, bucketRot.w));

        // حساب قصور حركة السطل (تسارع السطل يُطبّق عكسياً على السائل)
        Vector3 externalAccel = Vector3.zero;
        if (useBucket && bucketTransform != null && dt > 0)
        {
            Vector3 bucketVel = (bucketPos - prevBucketPos) / dt;
            Vector3 bucketAccel = (bucketVel - prevBucketVel) / dt;
            // سقف يمنع الانفجار عند الحركة المفاجئة
            if (bucketAccel.magnitude > maxInertiaAccel)
                bucketAccel = bucketAccel.normalized * maxInertiaAccel;
            externalAccel = -bucketAccel;   // القصور عكس اتجاه التسارع
            prevBucketVel = bucketVel;
            prevBucketPos = bucketPos;
        }
        compute.SetVector("externalAccel", externalAccel);
    }

    void LateUpdate()
    {
        RenderParticles();
    }

    void RenderParticles()
    {
        if (!ready) return;
        if (particleMaterial == null || particleMesh == null || argsBuffer == null) return;
        if (positionBuffer == null || !positionBuffer.IsValid()) return;

        particleMaterial.SetBuffer("_PositionBuffer", positionBuffer);
        particleMaterial.SetBuffer("_VelocityBuffer", velocityBuffer);
        particleMaterial.SetFloat("_Size", particleSize);
        particleMaterial.SetColor("_ColorSlow", fluidColor);
        particleMaterial.SetColor("_ColorFast", new Color(0.9f, 0.9f, 1f));
        particleMaterial.SetFloat("_SpeedScale", 8f);

        renderBounds = new Bounds(transform.position, boundsSize * 2f);
        Graphics.DrawMeshInstancedIndirect(particleMesh, 0, particleMaterial, renderBounds, argsBuffer);
    }

    void OnDrawGizmos()
    {
        if (useBucket)
        {
            // ارسم السطل الأسطواني
            Vector3 c = bucketTransform != null ? bucketTransform.position : transform.position;
            Gizmos.color = Color.cyan;
            // دوائر علوية وسفلية (تقريب)
            int seg = 24;
            float halfH = bucketHeight * 0.5f;
            Vector3 prevTop = Vector3.zero, prevBot = Vector3.zero;
            for (int i = 0; i <= seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                Vector3 off = new Vector3(Mathf.Cos(a) * bucketRadius, 0, Mathf.Sin(a) * bucketRadius);
                Vector3 top = c + off + Vector3.up * halfH;
                Vector3 bot = c + off - Vector3.up * halfH;
                if (i > 0)
                {
                    Gizmos.DrawLine(prevTop, top);
                    Gizmos.DrawLine(prevBot, bot);
                }
                Gizmos.DrawLine(bot, top);
                prevTop = top; prevBot = bot;
            }
        }
        else
        {
            Gizmos.color = Color.cyan;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, boundsSize);
            Gizmos.matrix = Matrix4x4.identity;
        }
        // منطقة التوليد
        Gizmos.color = new Color(1, 1, 0, 0.4f);
        Gizmos.DrawWireCube(spawnCentre, spawnSize);
    }

    void OnDestroy()
    {
        positionBuffer?.Release();
        predictedPositionBuffer?.Release();
        velocityBuffer?.Release();
        densityBuffer?.Release();
        sortTargetPositionBuffer?.Release();
        sortTargetPredictedBuffer?.Release();
        sortTargetVelocityBuffer?.Release();
        argsBuffer?.Release();
        spatialHash?.Release();
    }
}