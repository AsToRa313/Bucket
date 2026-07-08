using UnityEngine;
using Seb.Helpers;   


public class FluidClavetSim : MonoBehaviour
{
    [Header("Compute Shader")]
    public ComputeShader compute;

    [Header("الجسيمات")]
    [Tooltip("عدد الجسيمات لكل محور (الإجمالي = هذا³). 40³=64000، 46³≈97000")]
    public int particlesPerAxis = 40;
    [Tooltip("تكرارات الاسترخاء (أعلى = أكثر استقراراً، أبطأ)")]
    public int iterations = 2;

    // ===== أنواع السوائل الجاهزة (Presets) =====
    public enum FluidType { Custom, Water, Paint, Honey, Oil, Lava }
    [Header("نوع السائل (Fluid Preset)")]
    [Tooltip("اختر نوعاً جاهزاً - يضبط كل القيم واللون تلقائياً. Custom = تحكّم يدوي")]
    public FluidType fluidType = FluidType.Water;
    FluidType lastAppliedType = FluidType.Custom;

    [Header("معاملات Clavet")]
    public float smoothingRadius = 0.2f;
    public float restDensity = 10f;
    [Tooltip("حساب كثافة السكون تلقائياً من التوزيع الابتدائي (يُنصح به - يمنع التبعثر عند تغيير smoothingRadius)")]
    public bool autoRestDensity = true;
    public float stiffness = 0.5f;
    public float nearStiffness = 0.5f;
    [Header("اللزوجة (Viscosity) - نوع السائل")]
    [Tooltip("قوة اللزوجة: 0 = ماء رقيق، عالي = عسل/دهان سميك")]
    [Range(0f, 12f)]
    public float viscosityStrength = 0f;
    [Tooltip("اللزوجة التربيعية (للسرعات العالية) - عادة أصغر من الخطية")]
    [Range(0f, 5f)]
    public float viscosityBeta = 0f;
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

    [Header("تحكم الماوس (Mouse Interaction)")]
    [Tooltip("زر الماوس الأيسر = دفع، الأيمن = جذب")]
    public bool enableMouse = true;
    [Tooltip("نصف قطر تأثير الماوس على السائل")]
    public float mouseRadius = 1.5f;
    [Tooltip("قوة الدفع/الجذب")]
    public float mouseStrength = 60f;
    [Tooltip("الكاميرا المستخدمة لإسقاط الماوس (فارغ = Main Camera)")]
    public Camera interactionCamera;

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

  
    SpatialHash spatialHash;

    // عرض
    ComputeBuffer argsBuffer;
    Bounds renderBounds;

    // معرّفات الـ kernels
    int kExternalForces, kUpdateHash, kReorder, kReorderCopyBack;
    int kCalculateDensity, kViscosity, kRelax, kUpdatePositions;
    const int THREADS = 256;

    bool ready = false;

    // حالة تفاعل الماوس (تُحدّث في Update، تُقرأ في FixedUpdate)
    bool mouseActiveNow = false;
    Vector3 mouseWorldPos = Vector3.zero;
    float mouseSign = 1f;   // +1 دفع، -1 جذب

    // يُستدعى عند تغيير قيمة بالإنسبكتر - يطبّق الـ preset فوراً للمعاينة
    void OnValidate()
    {
        if (fluidType != FluidType.Custom && fluidType != lastAppliedType)
            ApplyFluidPreset(fluidType);
    }

    void Start()
    {
        if (compute == null) { Debug.LogError("Compute فارغ!"); return; }

        // طبّق نوع السائل المختار (يضبط القيم واللون) قبل التوليد
        if (fluidType != FluidType.Custom)
            ApplyFluidPreset(fluidType);

        numParticles = particlesPerAxis * particlesPerAxis * particlesPerAxis;
        Debug.Log($"[FluidClavet] عدد الجسيمات = {numParticles}");

        CreateBuffers();
        SpawnParticles();
        CacheKernels();
        SetupRenderArgs();
        ready = true;
    }

    // ================= واجهة التحكم العامة (للـ UI) =================

    
    public void ResetFluid()
    {
        if (!ready) return;
        SpawnParticles();
        Debug.Log("[FluidClavet] تمت إعادة تشغيل السائل");
    }


    public void SetPushMode() { mouseSign = 1f; enableMouse = true; }
    public void SetPullMode() { mouseSign = -1f; enableMouse = true; }
    public void ToggleMouse(bool on) { enableMouse = on; }
    public void SetMouseStrength(float v) { mouseStrength = v; }
    public void SetMouseRadius(float v) { mouseRadius = v; }
    public void SetGravity(float v) { gravity = v; }

    // للـ UI: تغيير نوع السائل وقت التشغيل (يطبّق القيم واللون فوراً)
    public void SetFluidType(int typeIndex)
    {
        ApplyFluidPreset((FluidType)typeIndex);
    }

    // ===== تطبيق نوع السائل: يضبط القيم الفيزيائية واللون =====
    public void ApplyFluidPreset(FluidType type)
    {
        fluidType = type;
        lastAppliedType = type;
        switch (type)
        {
            case FluidType.Water:   // ماء: رقيق جداً، سيولة عالية، يتناثر بحرّية
                viscosityStrength = 0f; viscosityBeta = 0f;
                stiffness = 0.6f; nearStiffness = 0.5f;
                velocityDamping = 0.995f;
                fluidColor = new Color(0.25f, 0.55f, 0.95f, 1f);
                break;
            case FluidType.Paint:   // دهان: لزج متماسك، يتدفّق ببطء
                viscosityStrength = 3f; viscosityBeta = 1f;
                stiffness = 0.5f; nearStiffness = 0.6f;
                velocityDamping = 0.96f;
                fluidColor = new Color(0.1f, 0.1f, 0.1f, 1f);
                break;
            case FluidType.Honey:   // عسل: لزوجة قصوى، بطيء جداً وكتلة متماسكة
                viscosityStrength = 8f; viscosityBeta = 3f;
                stiffness = 0.4f; nearStiffness = 0.7f;
                velocityDamping = 0.92f;
                fluidColor = new Color(0.95f, 0.7f, 0.15f, 1f);
                break;
            case FluidType.Oil:     // زيت: لزوجة خفيفة-متوسطة، انسياب ناعم
                viscosityStrength = 1.5f; viscosityBeta = 0.4f;
                stiffness = 0.5f; nearStiffness = 0.5f;
                velocityDamping = 0.98f;
                fluidColor = new Color(0.35f, 0.28f, 0.12f, 1f);
                break;
            case FluidType.Lava:    // حمم: لزوجة عالية، ثقيل بطيء
                viscosityStrength = 6f; viscosityBeta = 2f;
                stiffness = 0.45f; nearStiffness = 0.65f;
                velocityDamping = 0.93f;
                fluidColor = new Color(0.95f, 0.35f, 0.1f, 1f);
                break;
            case FluidType.Custom:  // يدوي: لا نغيّر شيئاً
                break;
        }
        Debug.Log("[FluidClavet] نوع السائل: " + type);
    }

    // ==============================================================

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

        // حساب restDensity تلقائياً من التوزيع الابتدائي (نفس آلية الدلو).
        // ضروري: القيمة الصحيحة تعتمد على smoothingRadius وكثافة الجسيمات -
        // بدونها، تغيير smoothingRadius يجعل السائل يتبعثر (كثافة سكون خاطئة).
        if (autoRestDensity)
        {
            restDensity = EstimateRestDensity(positions);
            Debug.Log($"[FluidClavet] restDensity محسوبة تلقائياً = {restDensity:F3}");
        }
    }

    // يقدّر كثافة السكون من متوسط الكثافة الفعلية لعيّنة من الجسيمات
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
        kExternalForces = compute.FindKernel("ExternalForces");
        kUpdateHash = compute.FindKernel("UpdateSpatialHash");
        kReorder = compute.FindKernel("Reorder");
        kReorderCopyBack = compute.FindKernel("ReorderCopyBack");
        kCalculateDensity = compute.FindKernel("CalculateDensity");
        kViscosity = compute.FindKernel("ApplyViscosity");
        kRelax = compute.FindKernel("DoubleDensityRelax");
        kUpdatePositions = compute.FindKernel("UpdatePositions");

        // ربط الـ buffers بكل الـ kernels التي تحتاجها
        int[] allKernels = { kExternalForces, kUpdateHash, kReorder, kReorderCopyBack,
                             kCalculateDensity, kViscosity, kRelax, kUpdatePositions };
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

        // 2-4. بناء الشبكة المكانية مرة واحدة بالإطار (تحسين PBF - Macklin & Müller 2013)
        //      الجيران يتغيّرون قليلاً جداً داخل الإطار الواحد، فلا داعي لإعادة
        //      الفرز الكامل (أثقل خطوة) في كل تكرار. هذا يخفّض التكلفة من
        //      iterations×(فرز+كثافة+استرخاء) إلى فرز_واحد + iterations×(كثافة+استرخاء).
        compute.Dispatch(kUpdateHash, groups, 1, 1);
        spatialHash.Run();
        compute.Dispatch(kReorder, groups, 1, 1);
        compute.Dispatch(kReorderCopyBack, groups, 1, 1);

        // اللزوجة: تقارب سرعات الجيران (مرة واحدة، على السرعات، بعد بناء الشبكة).
        // تُطبّق قبل الاسترخاء لأنها تعمل على السرعة بينما الاسترخاء على الموقع.
        compute.Dispatch(kViscosity, groups, 1, 1);

        // 5-6. حلقة الاسترخاء: كثافة + إزاحة فقط (بدون إعادة فرز)
        //      نفس فلسفة الدلو: تحسين تدريجي لدقة الكثافة، بلا تكرار للجاذبية
        for (int it = 0; it < iterations; it++)
        {
            compute.Dispatch(kCalculateDensity, groups, 1, 1);
            compute.Dispatch(kRelax, groups, 1, 1);
        }

        // 7. تحديث السرعة + الموقع النهائي + التصادم - مرة واحدة بعد الحلقة، مثل ComputeVelocity بالدلو
        compute.Dispatch(kUpdatePositions, groups, 1, 1);
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
        compute.SetFloat("viscosityStrength", viscosityStrength);
        compute.SetFloat("viscosityBeta", viscosityBeta);
        compute.SetVector("boundsSize", boundsSize);

      
        Matrix4x4 localToWorld = Matrix4x4.TRS(transform.position, transform.rotation, boundsSize);
        compute.SetMatrix("localToWorld", localToWorld);
        compute.SetMatrix("worldToLocal", localToWorld.inverse);
        compute.SetFloat("wallRestitution", 0.1f);

        // --- ثوابت تفاعل الماوس ---
        compute.SetInt("mouseActive", mouseActiveNow ? 1 : 0);
        compute.SetVector("mousePos", mouseWorldPos);
        compute.SetFloat("mouseRadius", mouseRadius);
        compute.SetFloat("mouseStrength", mouseStrength * mouseSign);
    }

    void Update()
    {
        UpdateMouseInteraction();
        RenderParticles();
    }

    // يقرأ الماوس ويسقط موقعه إلى فضاء العالم عند عمق السائل
    void UpdateMouseInteraction()
    {
        mouseActiveNow = false;
        if (!enableMouse) return;

        bool leftHeld = Input.GetMouseButton(0);    // دفع
        bool rightHeld = Input.GetMouseButton(1);   // جذب
        if (!leftHeld && !rightHeld) return;

        Camera cam = interactionCamera != null ? interactionCamera : Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[FluidClavet] لا توجد كاميرا! اربط Interaction Camera أو ضع tag=MainCamera على كاميرتك");
            return;
        }

        // نسقط الماوس على مستوى يواجه الكاميرا ويمرّ بمركز السائل.
        // هذا يعمل من أي زاوية كاميرا (أمامية، جانبية، علوية).
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 planeNormal = -cam.transform.forward;   // المستوى يواجه الكاميرا
        Plane plane = new Plane(planeNormal, transform.position);
        if (plane.Raycast(ray, out float enter))
        {
            mouseWorldPos = ray.GetPoint(enter);
            mouseActiveNow = true;
            // الوضع (دفع/جذب) يُدار من أزرار UI عبر mouseSign.
            // زر الماوس الأيمن يجذب مؤقتاً بغض النظر عن وضع UI (للراحة)
            if (rightHeld && !leftHeld) mouseSign = -1f;
        }
    }

    void LateUpdate()
    {
        // العرض يتم في Update - محفوظ للتوافق
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

    public void FullReset()
    {
        if (!ready) return;
        ready = false;

        // تحرير الـ Buffers القديمة ضروري جداً عشان ما يصير Memory Leak أو يفرش
        OnDestroy();

        numParticles = particlesPerAxis * particlesPerAxis * particlesPerAxis;
        Debug.Log($"rebuild fluid = {numParticles}");

        CreateBuffers();
        SpawnParticles();
        CacheKernels();
        SetupRenderArgs();

        ready = true;
    }

    void OnDrawGizmos()
    {
        // حدود الصندوق
        Gizmos.color = Color.cyan;
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
        Gizmos.DrawWireCube(Vector3.zero, boundsSize);
        Gizmos.matrix = Matrix4x4.identity;
        // منطقة التوليد
        Gizmos.color = new Color(1, 1, 0, 0.4f);
        Gizmos.DrawWireCube(spawnCentre, spawnSize);
        // دائرة تأثير الماوس (وقت التشغيل): أحمر=دفع، أخضر=جذب
        if (Application.isPlaying && mouseActiveNow)
        {
            Gizmos.color = mouseSign > 0 ? Color.red : Color.green;
            Gizmos.DrawWireSphere(mouseWorldPos, mouseRadius);
        }
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