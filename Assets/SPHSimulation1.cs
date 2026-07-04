using UnityEngine;
using System.Collections.Generic;

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
    public float maxInertiaAccel = 1f;
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

    [Header("Demo: Two Droplets (وضع القطرتين)")]
    [Tooltip("تفعيل وضع القطرتين: يهيّئ الجسيمات كقطرتين بلونين بدل ملء السطل")]
    public bool twoDropletsMode = false;
    [Tooltip("لون القطرة الأولى")]
    public Color dropletColor1 = new Color(0.9f, 0.15f, 0.15f, 1f);
    [Tooltip("لون القطرة الثانية")]
    public Color dropletColor2 = new Color(0.15f, 0.3f, 0.95f, 1f);
    [Tooltip("نصف قطر كل قطرة")]
    public float dropletRadius = 0.3f;
    [Tooltip("المسافة الأفقية بين القطرتين")]
    public float dropletSeparation = 0.5f;
    [Tooltip("ارتفاع القطرتين فوق مركز السطل")]
    public float dropletHeight = 1.5f;
    [Tooltip("سرعة تقارب القطرتين نحو بعض (تضمن اللقاء والاندماج)")]
    public float dropletApproachSpeed = 0.8f;

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
    [Tooltip("احسب مستوى اللوحة تلقائياً من موقعها (مستحسن - يمنع الرسم تحت اللوحة)")]
    public bool autoCanvasY = true;
    [Tooltip("احسب حجم اللوحة تلقائياً (للـ Unity Plane: نصف الحجم = 5×scale)")]
    public bool autoCanvasSize = true;
    [Tooltip("إزاحة فوق سطح اللوحة (لالتقاط القطرة عند اللمس مباشرة)")]
    public float canvasSurfaceOffset = 0.02f;
    [Tooltip("مستوى ارتفاع اللوحة (Y بالعالم) - يُحسب تلقائياً لو autoCanvasY مفعّل")]
    public float canvasY = -1.5f;
    [Tooltip("نصف حجم اللوحة أفقياً (X,Z) لفحص دخول القطرة")]
    public Vector2 canvasHalfSize = new Vector2(1f, 1f);
    [Tooltip("لون الطلاء على اللوحة")]
    public Color paintColor = new Color(0.8f, 0.1f, 0.1f, 1f);
    [Tooltip("نصف قطر البقعة بالمتر (بحجم القطرة)")]
    public float canvasSplatRadius = 0.03f;
    [Range(0f, 1f)]
    [Tooltip("شفافية الطلاء (1=صلب)")]
    public float canvasPaintOpacity = 0.6f;
    [Tooltip("قلب المحور الأفقي للرسم (لو الرسم معكوس يمين/يسار)")]
    public bool canvasFlipU = false;
    [Tooltip("قلب المحور العمودي للرسم (لو الرسم معكوس فوق/تحت)")]
    public bool canvasFlipV = false;
    [Tooltip("توسّع البقعة عند تراكم الدهان فوق بعضه")]
    public bool enablePoolGrowth = true;
    [Tooltip("قوة نمو البقعة عند التشبّع (1=حتى الضعف)")]
    public float poolGrowth = 1.5f;
    [Tooltip("خلط الألوان عند نزول لون فوق لون مختلف (رطب)")]
    public bool enableWetMix = true;
    [Range(0f, 1f)]
    [Tooltip("قوة الخلط الطرحي (0=متوسط بسيط، 1=طرحي مثل الدهان)")]
    public float wetMixStrength = 0.5f;
    [Tooltip("كل كم فريم نفحص القطرات الساقطة (أعلى = أداء أفضل)")]
    public int canvasCheckInterval = 2;

    ComputeBuffer positionBuffer;
    ComputeBuffer prevPositionBuffer;
    ComputeBuffer velocityBuffer;
    ComputeBuffer stateBuffer;
    ComputeBuffer holeBuffer;
    ComputeBuffer colorBuffer;
    ComputeBuffer splatPointsBuffer;   // مواقع القطرات الواصلة (append)
    ComputeBuffer splatCountBuffer;    // عدّاد القطرات الواصلة

    ComputeBuffer cellCountsBuffer;
    ComputeBuffer cellStartBuffer;
    ComputeBuffer cellEndBuffer;
    ComputeBuffer sortedIndicesBuffer;
    ComputeBuffer particleCellIndexBuffer;
    ComputeBuffer blockSumsBuffer;   // مجاميع الكتل لـ prefix sum المتوازي
    // buffers إعادة الترتيب (Reorder)
    ComputeBuffer sortedPositionsBuffer, sortedPrevPositionsBuffer, sortedVelocitiesBuffer, sortedStatesBuffer, sortedColorsBuffer;

    // buffers الهدف لإعادة الترتيب (Reorder)
    ComputeBuffer sortedPositionBuffer;
    ComputeBuffer sortedPrevPositionBuffer;
    ComputeBuffer sortedVelocityBuffer;
    ComputeBuffer sortedStateBuffer;
    ComputeBuffer sortedColorBuffer;

    int gridResolution, numCells;
    Vector3 gridMin;

    int kGravity, kClearGrid, kCount, kScanBlocks, kScanBlockSums, kScanCombine, kScatter;
    int kReorderPosVel, kReorderStateColor, kReorderCopyBackPosVel, kReorderCopyBackStateColor;
    int kRelax, kVelocity, kCheckHoles, kVortex, kPaintCanvas;
    const int THREADS = 256;

    RenderTexture canvasRT;   // تكستشر اللوحة القابل للكتابة من GPU
    ComputeBuffer canvasAccumBuffer;   // تراكم الدهان لكل بكسل
    Vector3 prevBucketVel = Vector3.zero;
    bool ready = false;

    void Start()
    {
        if (compute == null) { Debug.LogError("compute فارغ!"); return; }
        if (bucketTransform == null && pendulum != null)
            bucketTransform = pendulum.transform;

        RebuildSimulation();
    }

    /// <summary>
    /// يعيد بناء المحاكاة بالكامل (الشبكة + الـ buffers + توزيع الجسيمات).
    /// استدعِها من واجهة التحكم بعد تغيير numParticles / bucketRadius / bucketHeight /
    /// smoothingRadius / initialFillRatio / twoDropletsMode أو أي قيمة تؤثر على التهيئة.
    /// آمنة للاستدعاء المتكرر (تحرّر الـ buffers القديمة أولاً لو موجودة).
    /// </summary>
    public void RebuildSimulation()
    {
        if (compute == null) { Debug.LogError("compute فارغ!"); return; }

        ready = false;
        ReleaseBuffers();

        SetupGrid();
        CreateBuffers();
        InitializeParticles();
        SetupCanvasTexture();
        CacheKernels();
        ready = true;
        Debug.Log($"SPHSimulation جاهز: {numParticles} جسيم، شبكة {gridResolution}، restDensity={restDensity:F2}");
    }

    // ينشئ تكستشر اللوحة القابل للكتابة من GPU ويربطه بالمادة
    void SetupCanvasTexture()
    {
        if (canvas == null) return;

        int res = canvas.textureResolution;
        canvasRT = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat);
        canvasRT.enableRandomWrite = true;   // يسمح للـ compute بالكتابة
        canvasRT.Create();

        // buffer تراكم الدهان لكل بكسل (يبدأ صفر)
        canvasAccumBuffer = new ComputeBuffer(res * res, sizeof(float));
        canvasAccumBuffer.SetData(new float[res * res]);

        // املأ اللوحة بلون الخلفية
        RenderTexture.active = canvasRT;
        GL.Clear(true, true, canvas.backgroundColor);
        RenderTexture.active = null;

        // اعرض التكستشر على اللوحة + أعطه للـ painter
        canvas.SetGPUTexture(canvasRT);
    }

    void SetupGrid()
    {
        // الشبكة يجب أن تغطّي منطقة السائل كاملة (السطل + هامش للقطرات)
        float worldSize = Mathf.Max(bucketRadius * 2f, bucketHeight) * 2.5f;

        // نريد عدد خلايا ≈ عدد الجسيمات (تجنّب التكدّس)، بحد أقصى 48³
        int cap = 48;
        int desiredRes = Mathf.CeilToInt(Mathf.Pow(numParticles, 1f / 3f)) + 2;
        gridResolution = Mathf.Clamp(desiredRes, 8, cap);

        // حجم الخلية = حجم المنطقة / عدد الخلايا (يضمن تغطية كاملة)
        gridCellSize = worldSize / gridResolution;
        // لكن حجم الخلية يجب ألا يقل عن smoothingRadius (وإلا نفوّت جيران)
        gridCellSize = Mathf.Max(gridCellSize, smoothingRadius);

        numCells = gridResolution * gridResolution * gridResolution;
    }
    float gridCellSize;

    void UpdateGridOrigin()
    {
        Vector3 c = bucketTransform != null ? bucketTransform.position : Vector3.zero;
        float half = gridResolution * gridCellSize * 0.5f;
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
        // مجاميع الكتل: عدد الكتل = ceil(numCells / 256)
        int numBlocks = Mathf.CeilToInt(numCells / 256f);
        blockSumsBuffer = new ComputeBuffer(numBlocks, sizeof(uint));
        // buffers إعادة الترتيب (نفس أحجام الجسيمات)
        sortedPositionsBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortedPrevPositionsBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortedVelocitiesBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortedStatesBuffer = new ComputeBuffer(numParticles, sizeof(uint));
        sortedColorsBuffer = new ComputeBuffer(numParticles, sizeof(float) * 4);

        // buffers الهدف لإعادة الترتيب (نفس أحجام الأصلية)
        sortedPositionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortedPrevPositionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortedVelocityBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortedStateBuffer = new ComputeBuffer(numParticles, sizeof(uint));
        sortedColorBuffer = new ComputeBuffer(numParticles, sizeof(float) * 4);

        // buffer append لمواقع القطرات الواصلة للوحة + عدّاد للقراءة
        splatPointsBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3,
                                              ComputeBufferType.Append);
        splatCountBuffer = new ComputeBuffer(1, sizeof(int),
                                              ComputeBufferType.IndirectArguments);
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
        // وضع القطرتين للعرض التوضيحي: قطرتان بلونين بدل ملء السطل
        if (twoDropletsMode)
        {
            InitializeTwoDroplets();
            return;
        }

        float fill = pendulum != null ? pendulum.GetFillRatio() : initialFillRatio;
        fill = Mathf.Clamp01(fill <= 0f ? initialFillRatio : fill);

        Vector3 center = bucketTransform != null ? bucketTransform.position : Vector3.zero;
        Quaternion rot = bucketTransform != null ? bucketTransform.rotation : Quaternion.identity;

        var positions = new Vector3[numParticles];
        var velocities = new Vector3[numParticles];
        float bottomY = -bucketHeight * 0.5f;
        float fillHeight = bucketHeight * fill;
        float fillRadius = bucketRadius * 0.9f;

        // توزيع منتظم (شبكة + جيتر خفيف) بدل العشوائي البحت.
        // العشوائي البحت يسبب تكتلات محلية عشوائية (كثافة زائدة موضعية عبر تذبذب إحصائي طبيعي)
        // تولّد ضغط Clavet عنيف من أول فريم وتنفجر - خصوصاً مع أعداد كبيرة (150 ألف+).
        float cylinderVolume = Mathf.PI * fillRadius * fillRadius * Mathf.Max(fillHeight, 0.001f);
        float spacing = Mathf.Pow(cylinderVolume / Mathf.Max(1, numParticles), 1f / 3f) * 0.85f;
        // أرضية أعلى للمسافة: تمنع انفجار أبعاد الشبكة لو الحجم صغير جداً نسبة للعدد
        spacing = Mathf.Max(spacing, 0.001f);

        // سقف صارم على أبعاد الشبكة (يمنع التعليق رياضياً بغض النظر عن أي قيم مدخلة)
        const int MAX_AXIS = 150;
        int nx = Mathf.Clamp(Mathf.CeilToInt((fillRadius * 2f) / spacing), 1, MAX_AXIS);
        int ny = Mathf.Clamp(Mathf.CeilToInt(fillHeight / spacing), 1, MAX_AXIS);

        // إعادة حساب spacing الفعلي بناءً على الأبعاد بعد القفل (لو انقفلت الأبعاد، نكبّر spacing ليطابق)
        float actualSpacingXZ = (fillRadius * 2f) / nx;
        float actualSpacingY = fillHeight / ny;

        var candidates = new List<Vector3>(Mathf.Min(numParticles + 64, MAX_AXIS * MAX_AXIS * MAX_AXIS));
        int maxCandidates = numParticles * 2;
        for (int ix = 0; ix < nx && candidates.Count < maxCandidates; ix++)
        {
            float px = -fillRadius + (ix + 0.5f) * actualSpacingXZ;
            for (int iz = 0; iz < nx && candidates.Count < maxCandidates; iz++)
            {
                float pz = -fillRadius + (iz + 0.5f) * actualSpacingXZ;
                if (px * px + pz * pz > fillRadius * fillRadius) continue;   // خارج الأسطوانة
                for (int iy = 0; iy < ny && candidates.Count < maxCandidates; iy++)
                {
                    float py = bottomY + (iy + 0.5f) * actualSpacingY;
                    Vector3 jitter = Random.insideUnitSphere * Mathf.Min(actualSpacingXZ, actualSpacingY) * 0.15f;
                    candidates.Add(new Vector3(px, py, pz) + jitter);
                }
            }
        }

        // خذ أول numParticles من الشبكة المنتظمة (تغطية متساوية للحجم كامل)
        // احتياطي: لو الشبكة (بعد القفل) ما غطّت العدد المطلوب، أكمل الباقي عشوائياً
        for (int i = 0; i < numParticles; i++)
        {
            Vector3 local;
            if (i < candidates.Count)
            {
                local = candidates[i];
            }
            else
            {
                float r = fillRadius * Mathf.Sqrt(Random.value);
                float a = Random.Range(0f, Mathf.PI * 2f);
                float y = bottomY + Random.Range(0f, fillHeight);
                local = new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
            }

            // قفل أمان نهائي: مهما كانت الحسابات، لا يُسمح لأي جسيم يبدأ خارج السطل فعلياً
            float radial = Mathf.Sqrt(local.x * local.x + local.z * local.z);
            float maxRadial = bucketRadius * 0.95f;
            if (radial > maxRadial)
            {
                float scale = maxRadial / Mathf.Max(radial, 0.0001f);
                local.x *= scale;
                local.z *= scale;
            }
            local.y = Mathf.Clamp(local.y, bottomY + 0.001f, bottomY + bucketHeight - 0.001f);

            positions[i] = center + rot * local;
            velocities[i] = Vector3.zero;
        }


        positionBuffer.SetData(positions);
        prevPositionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);

        // كل الجزيئات تبدأ داخل السطل (state = 0)
        var states = new uint[numParticles];
        stateBuffer.SetData(states);

        // ألوان الجسيمات: لون الدهان الحالي (أو قوس قزح للتشخيص)
        colorBuffer = new ComputeBuffer(numParticles, sizeof(float) * 4);
        var colors = new Vector4[numParticles];
        for (int i = 0; i < numParticles; i++)
        {
            if (useFixedColors)
            {
                // وضع التشخيص: قوس قزح حسب الزاوية (يكشف الدوران)
                Vector3 local = Quaternion.Inverse(rot) * (positions[i] - center);
                float angle = Mathf.Atan2(local.z, local.x);
                float hue = (angle + Mathf.PI) / (2f * Mathf.PI);
                Color c = Color.HSVToRGB(hue, 0.85f, 1f);
                colors[i] = new Vector4(c.r, c.g, c.b, 1f);
            }
            else
            {
                // الوضع العادي: كل الجسيمات بلون الدهان الحالي
                colors[i] = new Vector4(paintColor.r, paintColor.g, paintColor.b, 1f);
            }
        }
        colorBuffer.SetData(colors);

        if (autoRestDensity)
        {
            restDensity = EstimateRestDensity(positions);
            Debug.Log($"[SPH] restDensity محسوبة تلقائياً = {restDensity:F2}");
        }
    }

    // تهيئة القطرتين: مجموعتان كرويتان بلونين مختلفين (للعرض التوضيحي)
    void InitializeTwoDroplets()
    {
        Vector3 center = bucketTransform != null ? bucketTransform.position : Vector3.zero;

        var positions = new Vector3[numParticles];
        var velocities = new Vector3[numParticles];
        var colors = new Vector4[numParticles];

        int half = numParticles / 2;
        Vector3 c1 = center + new Vector3(-dropletSeparation * 0.5f, dropletHeight, 0);
        Vector3 c2 = center + new Vector3(dropletSeparation * 0.5f, dropletHeight, 0);
        Vector4 col1 = new Vector4(dropletColor1.r, dropletColor1.g, dropletColor1.b, 1f);
        Vector4 col2 = new Vector4(dropletColor2.r, dropletColor2.g, dropletColor2.b, 1f);

        for (int i = 0; i < numParticles; i++)
        {
            bool first = i < half;
            Vector3 dropCenter = first ? c1 : c2;
            // توزيع عشوائي داخل كرة
            Vector3 p;
            do
            {
                p = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
            } while (p.sqrMagnitude > 1f);
            positions[i] = dropCenter + p * dropletRadius;
            // سرعة تقارب ابتدائية: كل قطرة تتجه نحو الأخرى (تضمن اللقاء)
            float toward = first ? dropletApproachSpeed : -dropletApproachSpeed;
            velocities[i] = new Vector3(toward, 0f, 0f);
            colors[i] = first ? col1 : col2;
        }

        positionBuffer.SetData(positions);
        prevPositionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);

        // كل الجسيمات حرة (state=0 يكفي - بدون سطل فعلي، الحدود واسعة)
        var states = new uint[numParticles];
        stateBuffer.SetData(states);

        colorBuffer = new ComputeBuffer(numParticles, sizeof(float) * 4);
        colorBuffer.SetData(colors);

        if (autoRestDensity)
        {
            restDensity = EstimateRestDensity(positions);
            Debug.Log($"[SPH] وضع القطرتين - restDensity = {restDensity:F2}");
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
        kScanBlocks = compute.FindKernel("PrefixScanBlocks");
        kScanBlockSums = compute.FindKernel("PrefixScanBlockSums");
        kScanCombine = compute.FindKernel("PrefixScanCombine");
        kScatter = compute.FindKernel("ScatterParticles");
        kReorderPosVel = compute.FindKernel("ReorderData_PosVel");
        kReorderStateColor = compute.FindKernel("ReorderData_StateColor");
        kReorderCopyBackPosVel = compute.FindKernel("ReorderCopyBack_PosVel");
        kReorderCopyBackStateColor = compute.FindKernel("ReorderCopyBack_StateColor");
        kRelax = compute.FindKernel("DoubleDensityRelax");
        kVelocity = compute.FindKernel("ComputeVelocity");
        kCheckHoles = compute.FindKernel("CheckHoles");
        kVortex = compute.FindKernel("ApplyVortex");
        kPaintCanvas = compute.FindKernel("PaintOnCanvas");

        int[] particleKernels = { kGravity, kCount, kScatter, kRelax, kVelocity, kCheckHoles, kVortex, kPaintCanvas };
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
        int[] gridKernels = { kClearGrid, kScanBlocks, kScanBlockSums, kScanCombine };
        foreach (int k in gridKernels)
        {
            compute.SetBuffer(k, "CellCounts", cellCountsBuffer);
            compute.SetBuffer(k, "CellStart", cellStartBuffer);
            compute.SetBuffer(k, "CellEnd", cellEndBuffer);
            compute.SetBuffer(k, "BlockSums", blockSumsBuffer);
        }

        // ربط buffers إعادة الترتيب (4 كرنلات - كل كرنل يستخدم اللي يحتاجه بس)
        int[] reorderKernels = { kReorderPosVel, kReorderStateColor, kReorderCopyBackPosVel, kReorderCopyBackStateColor };
        foreach (int k in reorderKernels)
        {
            compute.SetBuffer(k, "Positions", positionBuffer);
            compute.SetBuffer(k, "PrevPositions", prevPositionBuffer);
            compute.SetBuffer(k, "Velocities", velocityBuffer);
            compute.SetBuffer(k, "States", stateBuffer);
            compute.SetBuffer(k, "SortedIndices", sortedIndicesBuffer);
            compute.SetBuffer(k, "SortedPositions", sortedPositionsBuffer);
            compute.SetBuffer(k, "SortedPrevPositions", sortedPrevPositionsBuffer);
            compute.SetBuffer(k, "SortedVelocities", sortedVelocitiesBuffer);
            compute.SetBuffer(k, "SortedStates", sortedStatesBuffer);
            compute.SetBuffer(k, "ParticleColorsSorted", sortedColorsBuffer);
            compute.SetBuffer(k, "ParticleColorsIn", colorBuffer);
        }

        // ربط buffer القطرات الواصلة بـ ComputeVelocity (هو من يكتب فيه)
        compute.SetBuffer(kVelocity, "SplatPoints", splatPointsBuffer);

        // ربط تكستشر اللوحة بـ kernel الرسم على GPU
        if (canvasRT != null)
        {
            compute.SetTexture(kPaintCanvas, "CanvasTex", canvasRT);
            compute.SetBuffer(kPaintCanvas, "CanvasAccum", canvasAccumBuffer);
            compute.SetBuffer(kPaintCanvas, "ParticleColors", colorBuffer);
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
        // عدد كتل الـ prefix scan (كل كتلة 256 خلية)
        int scanBlocks = Mathf.CeilToInt(numCells / 256f);

        // صفّر عدّاد القطرات الواصلة قبل خطوة المحاكاة
        if (canvas != null)
            splatPointsBuffer.SetCounterValue(0);

        compute.Dispatch(kGravity, pGroups, 1, 1);

        for (int it = 0; it < iterations; it++)
        {
            compute.Dispatch(kClearGrid, cGroups, 1, 1);
            compute.Dispatch(kCount, pGroups, 1, 1);
            // prefix sum متوازي على 3 مراحل (بدل خيط واحد بطيء)
            compute.Dispatch(kScanBlocks, scanBlocks, 1, 1);
            compute.Dispatch(kScanBlockSums, 1, 1, 1);
            compute.Dispatch(kScanCombine, scanBlocks, 1, 1);
            compute.Dispatch(kScatter, pGroups, 1, 1);
            // إعادة الترتيب: الجيران يصيروا متجاورين بالذاكرة (وصول أسرع للكرت)
            // مقسّمة لأربع كرنلات بسبب حد DirectX (أقصى 8 UAV لكل كرنل)
            compute.Dispatch(kReorderPosVel, pGroups, 1, 1);
            compute.Dispatch(kReorderStateColor, pGroups, 1, 1);
            compute.Dispatch(kReorderCopyBackPosVel, pGroups, 1, 1);
            compute.Dispatch(kReorderCopyBackStateColor, pGroups, 1, 1);
            compute.Dispatch(kRelax, pGroups, 1, 1);
        }

        compute.Dispatch(kVelocity, pGroups, 1, 1);

        // دوامة التصريف: جذب + التفاف حول الثقوب
        if (enableVortex)
            compute.Dispatch(kVortex, pGroups, 1, 1);

        // فحص الثقوب: تحويل الجزيئات القريبة لقطرات حرة
        compute.Dispatch(kCheckHoles, pGroups, 1, 1);

        // الرسم فوراً بعد كشف الوصول (نفس الخطوة = صفر تأخير)
        if (canvas != null && canvasRT != null)
        {
            UpdateCanvasConstants();
            compute.Dispatch(kPaintCanvas, pGroups, 1, 1);
        }

        // تشخيص دوري
        if (debugCanvas && Time.frameCount % 60 == 0)
            DiagnoseStates();
    }

    [Header("Debug")]
    public bool debugCanvas = false;
    uint[] diagStates;
    Vector3[] diagPositions;
    void DiagnoseStates()
    {
        if (diagStates == null || diagStates.Length != numParticles)
        {
            diagStates = new uint[numParticles];
            diagPositions = new Vector3[numParticles];
        }
        stateBuffer.GetData(diagStates);
        positionBuffer.GetData(diagPositions);
        int inBucket = 0, free = 0, waiting = 0, consumed = 0, painted = 0;
        int firstWaiting = -1;
        for (int i = 0; i < numParticles; i++)
        {
            uint st = diagStates[i];
            if (st == 0) inBucket++;
            else if (st == 1) { free++; if (firstWaiting < 0) firstWaiting = i; }
            else if (st == 2) consumed++;
            else if (st == 3) waiting++;
            else if (st == 4) painted++;
        }
        Debug.Log($"[تشخيص] داخل={inBucket} حرة={free} تنتظر_رسم={waiting} مستهلكة={consumed} مرسومة={painted}");

        // اطبع موقع اللوحة وحجمها المحسوب + UV لأول قطرة حرة
        Debug.Log($"[لوحة] مركز={canvas.transform.position:F2} نصف_حجم={canvasHalfSize:F2} canvasY={canvasY:F2}");
        if (firstWaiting >= 0)
        {
            Vector3 p = diagPositions[firstWaiting];
            float u = (p.x - canvas.transform.position.x) / (canvasHalfSize.x * 2f) + 0.5f;
            float v = (p.z - canvas.transform.position.z) / (canvasHalfSize.y * 2f) + 0.5f;
            Debug.Log($"[قطرة حرة] موقع={p:F2} => UV=({u:F2},{v:F2})");
        }
    }

    void Update()
    {
        // كشف تغيير لون الدهان من الإنسبكتر أثناء التشغيل => طبّقه فوراً
        if (ready && paintColor != lastAppliedColor)
        {
            ApplyColorToInBucketParticles(paintColor);
            lastAppliedColor = paintColor;
        }

        // الرسم انتقل لـ FixedUpdate (بعد كشف الوصول مباشرة) لصفر تأخير
    }

    Color lastAppliedColor = Color.clear;

    // يطبّق لوناً على الجسيمات داخل السطل (state=0) فقط
    void ApplyColorToInBucketParticles(Color c)
    {
        if (colorBuffer == null || !ready) return;
        var states = new uint[numParticles];
        stateBuffer.GetData(states);
        var colors = new Vector4[numParticles];
        colorBuffer.GetData(colors);
        for (int i = 0; i < numParticles; i++)
        {
            if (states[i] == 0)
                colors[i] = new Vector4(c.r, c.g, c.b, 1f);
        }
        colorBuffer.SetData(colors);
    }

    // يمرّر ثوابت اللوحة للـ compute (تُستدعى من Update و SetConstants)
    void UpdateCanvasConstants()
    {
        if (canvas == null) return;
        if (autoCanvasY)
            canvasY = canvas.transform.position.y + canvasSurfaceOffset;
        if (autoCanvasSize)
        {
            Vector3 sc = canvas.transform.lossyScale;
            canvasHalfSize = new Vector2(5f * sc.x, 5f * sc.z);
        }
        compute.SetInt("canvasEnabled", 1);
        compute.SetFloat("canvasY", canvasY);
        compute.SetVector("canvasCenter", canvas.transform.position);
        compute.SetVector("canvasHalfSize", canvasHalfSize);
        if (canvasRT != null)
        {
            compute.SetInt("canvasResolution", canvas.textureResolution);
            compute.SetVector("paintColorGPU", paintColor);
            float metersToPixels = canvas.textureResolution / (canvasHalfSize.x * 2f);
            compute.SetFloat("splatRadiusPixels", canvasSplatRadius * metersToPixels);
            compute.SetFloat("paintOpacityGPU", canvasPaintOpacity);
            compute.SetInt("canvasFlipU", canvasFlipU ? 1 : 0);
            compute.SetInt("canvasFlipV", canvasFlipV ? 1 : 0);
            compute.SetInt("canvasPooling", enablePoolGrowth ? 1 : 0);
            compute.SetFloat("poolGrowth", poolGrowth);
            compute.SetInt("canvasWetMix", enableWetMix ? 1 : 0);
            compute.SetFloat("wetMixStrength", wetMixStrength);
        }
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
        compute.SetFloat("gridCellSize", gridCellSize);
        compute.SetVector("gridMin", gridMin);

        compute.SetInt("numHoles", Mathf.Max(1, holes.Length));

        compute.SetFloat("vortexRange", vortexRange);
        compute.SetFloat("vortexPull", vortexPull);
        compute.SetFloat("vortexSpin", vortexSpin);

        // ثوابت اللوحة (لكشف وصول القطرات داخل الـ compute)
        bool canvasOn = canvas != null;
        // احسب مستوى اللوحة تلقائياً من موقعها + إزاحة السطح
        if (canvasOn && autoCanvasY)
            canvasY = canvas.transform.position.y + canvasSurfaceOffset;
        // احسب حجم اللوحة تلقائياً: Unity Plane حجمه 10×10 عند scale=1
        // فنصف الحجم = 5 × scale
        if (canvasOn && autoCanvasSize)
        {
            Vector3 sc = canvas.transform.lossyScale;
            canvasHalfSize = new Vector2(5f * sc.x, 5f * sc.z);
        }
        compute.SetInt("canvasEnabled", canvasOn ? 1 : 0);
        compute.SetFloat("canvasY", canvasY);
        Vector3 cc = canvasOn ? canvas.transform.position : Vector3.zero;
        compute.SetVector("canvasCenter", cc);
        compute.SetVector("canvasHalfSize", canvasHalfSize);

        // ثوابت الرسم على GPU
        if (canvasOn && canvasRT != null)
        {
            compute.SetInt("canvasResolution", canvas.textureResolution);
            compute.SetVector("paintColorGPU", paintColor);
            // نصف قطر البقعة بالبكسل: من متر إلى بكسل
            float metersToPixels = canvas.textureResolution / (canvasHalfSize.x * 2f);
            compute.SetFloat("splatRadiusPixels", canvasSplatRadius * metersToPixels);
            compute.SetFloat("paintOpacityGPU", canvasPaintOpacity);
        }
    }

    public int GetParticleCount() => numParticles;
    public ComputeBuffer GetPositionBuffer() => positionBuffer;
    public ComputeBuffer GetVelocityBuffer() => velocityBuffer;
    public ComputeBuffer GetColorBuffer() => colorBuffer;
    public ComputeBuffer GetStateBuffer() => stateBuffer;
    public bool UseFixedColors() => useFixedColors;

    /// <summary>
    /// يبدّل لون الدهان الحالي. الجسيمات التي لا تزال في السطل تأخذ اللون الجديد.
    /// استدعِها من زر UI لتغيير لون الرسم.
    /// </summary>
    public void SetPaintColor(Color newColor)
    {
        paintColor = newColor;
        lastAppliedColor = newColor;
        ApplyColorToInBucketParticles(newColor);
        Debug.Log($"[SPH] تبديل لون الدهان إلى {newColor}");
    }

    void OnDestroy()
    {
        ReleaseBuffers();
    }

    /// <summary>يحرّر كل compute buffers والـ RenderTexture الحاليين. آمنة للاستدعاء حتى لو لم تُنشأ بعد.</summary>
    void ReleaseBuffers()
    {
        positionBuffer?.Release();
        prevPositionBuffer?.Release();
        velocityBuffer?.Release();
        stateBuffer?.Release();
        holeBuffer?.Release();
        colorBuffer?.Release();
        splatPointsBuffer?.Release();
        splatCountBuffer?.Release();
        if (canvasRT != null) canvasRT.Release();
        canvasAccumBuffer?.Release();
        cellCountsBuffer?.Release();
        cellStartBuffer?.Release();
        cellEndBuffer?.Release();
        sortedIndicesBuffer?.Release();
        particleCellIndexBuffer?.Release();
        blockSumsBuffer?.Release();
        sortedPositionsBuffer?.Release();
        sortedPrevPositionsBuffer?.Release();
        sortedVelocitiesBuffer?.Release();
        sortedStatesBuffer?.Release();
        sortedColorsBuffer?.Release();

        positionBuffer = null; prevPositionBuffer = null; velocityBuffer = null; stateBuffer = null;
        holeBuffer = null; colorBuffer = null; splatPointsBuffer = null; splatCountBuffer = null;
        canvasRT = null; canvasAccumBuffer = null; cellCountsBuffer = null; cellStartBuffer = null;
        cellEndBuffer = null; sortedIndicesBuffer = null; particleCellIndexBuffer = null; blockSumsBuffer = null;
        sortedPositionsBuffer = null; sortedPrevPositionsBuffer = null; sortedVelocitiesBuffer = null;
        sortedStatesBuffer = null; sortedColorsBuffer = null;
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