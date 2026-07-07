using UnityEngine;
using System.Collections.Generic;
using Seb.Helpers;   // نظام Sebastian: SpatialHash

/// <summary>
/// مدير محاكاة سوائل على GPU بصيغة Clavet مع نظام ترتيب مكاني (Seb Lague).
/// تم إصلاح مشكلة التجمد (Infinite Loop) بربط مصفوفات الترتيب بشكل آمن.
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
    public bool autoRestDensity = true;
    public float restDensity = 10f;
    public float stiffness = 1.0f;
    public float nearStiffness = 2.0f;
    [Range(0.9f, 1f)]
    public float velocityDamping = 0.98f;
    public float gravity = 9.81f;
    public float maxInertiaAccel = 1f;
    [Range(0f, 1f)]
    public float collisionDamping = 0.4f;
    [Range(0f, 0.8f)]
    public float wallRestitution = 0.1f;

    [Header("Bucket Integration")]
    public SphericalPendulumMath pendulum;
    public Transform bucketTransform;
    public float bucketRadius = 0.25f;
    public float bucketHeight = 0.6f;
    [Range(0f, 1f)]
    public float initialFillRatio = 0.8f;

    [Header("Demo: Two Droplets")]
    public bool twoDropletsMode = false;
    public Color dropletColor1 = new Color(0.9f, 0.15f, 0.15f, 1f);
    public Color dropletColor2 = new Color(0.15f, 0.3f, 0.95f, 1f);
    public float dropletRadius = 0.3f;
    public float dropletSeparation = 0.5f;
    public float dropletHeight = 1.5f;
    public float dropletApproachSpeed = 0.8f;

    [System.Serializable]
    public struct DrainHole
    {
        public Vector3 localPosition;
        public float radius;
    }

    [Header("Drain Holes")]
    public DrainHole[] holes = new DrainHole[]
    {
        new DrainHole { localPosition = new Vector3(0f, -0.3f, 0f), radius = 0.05f },
        new DrainHole { localPosition = new Vector3(0.25f, -0.1f, 0f), radius = 0.04f },
        new DrainHole { localPosition = new Vector3(-0.25f, -0.1f, 0f), radius = 0.04f },
    };

    [Header("Vortex")]
    public bool enableVortex = true;
    public float vortexRange = 4f;
    public float vortexPull = 2f;
    public float vortexSpin = 3f;

    [Header("Visualization")]
    public bool useFixedColors = false;

    [Header("Canvas Painting")]
    public CanvasPainter canvas;
    public bool autoCanvasY = true;
    public bool autoCanvasSize = true;
    public float canvasSurfaceOffset = 0.02f;
    public float canvasY = -1.5f;
    public Vector2 canvasHalfSize = new Vector2(1f, 1f);
    public Color paintColor = new Color(0.8f, 0.1f, 0.1f, 1f);
    public float canvasSplatRadius = 0.03f;
    [Range(0f, 1f)]
    public float canvasPaintOpacity = 0.6f;
    public bool canvasFlipU = false;
    public bool canvasFlipV = false;
    public bool enablePoolGrowth = true;
    [Tooltip("سرعة نمو البقعة مع التراكم. أصغر = أخف على الأداء (0.3 موصى به، 1.5 يعلّق)")]
    [Range(0.1f, 1.5f)]
    public float poolGrowth = 0.3f;
    [Tooltip("سقف حجم بركة الطلاء بالبكسل. أصغر = أسرع بكثير (8 موصى به، 20+ يعلّق)")]
    [Range(4, 24)]
    public int poolMaxRadius = 8;
    [Tooltip("حد نمو البقعة: بعد هذا التراكم تتوقف البقعة عن الكبر (لكن تبقى تُرسم فوقها الألوان). أصغر = بقع أصغر وأسرع")]
    [Range(10, 100)]
    public float poolSaturation = 40f;
    [Tooltip("أقصى عدد قطرات تُرسم بكل إطار. يمنع التعليق عند تدفق كثيف. أصغر = أأمن (150 موصى به)")]
    [Range(20, 2000)]
    public int maxSplatsPerFrame = 150;
    [Tooltip("عمق تحت اللوحة تختفي عنده القطرة الحرة (تمنع السقوط اللانهائي الذي يعلّق النظام). متر")]
    public float freeParticleKillDepth = 2f;
    public bool enableWetMix = true;
    [Range(0f, 1f)]
    public float wetMixStrength = 0.5f;
    public int canvasCheckInterval = 2;

    uint[] zeroArr = new uint[] { 0 };

    ComputeBuffer positionBuffer, prevPositionBuffer, velocityBuffer;
    ComputeBuffer stateBuffer, holeBuffer, colorBuffer;
    ComputeBuffer splatPointsBuffer, splatCountBuffer;
    ComputeBuffer paintCounterBuffer;   // عدّاد حد الرسم/إطار

    SpatialHash spatialHash;

    ComputeBuffer sortedPositionsBuffer, sortedPrevPositionsBuffer, sortedVelocitiesBuffer;
    ComputeBuffer sortedStatesBuffer, sortedColorsBuffer;

    int kGravity, kUpdateHash, kReorderPosVel, kReorderStateColor;
    int kReorderCopyBackPosVel, kReorderCopyBackStateColor;
    int kRelax, kVelocity, kCheckHoles, kVortex, kPaintCanvas;
    const int THREADS = 256;

    RenderTexture canvasRT;
    ComputeBuffer canvasAccumBuffer;
    Vector3 prevBucketVel = Vector3.zero;
    bool ready = false;
    Color lastAppliedColor = Color.clear;

    void Start()
    {
        if (compute == null) { Debug.LogError("compute فارغ!"); return; }
        if (bucketTransform == null && pendulum != null) bucketTransform = pendulum.transform;
        RebuildSimulation();
    }

    public void RebuildSimulation()
    {
        if (compute == null) return;
        ready = false;
        ReleaseBuffers();

        CreateBuffers();
        InitializeParticles();
        SetupCanvasTexture();
        CacheKernels();

        ready = true;
        Debug.Log($"[SPH] جاهز وتم إصلاح الترتيب المكاني: {numParticles} جسيم عبر SpatialHash");
    }

    void SetupCanvasTexture()
    {
        if (canvas == null) return;
        int res = canvas.textureResolution;
        canvasRT = new RenderTexture(res, res, 0, RenderTextureFormat.ARGBFloat);
        canvasRT.enableRandomWrite = true;
        canvasRT.Create();

        canvasAccumBuffer = new ComputeBuffer(res * res, sizeof(float));
        canvasAccumBuffer.SetData(new float[res * res]);

        RenderTexture.active = canvasRT;
        GL.Clear(true, true, canvas.backgroundColor);
        RenderTexture.active = null;

        canvas.SetGPUTexture(canvasRT);
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

        spatialHash = new SpatialHash(numParticles);

        sortedPositionsBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortedPrevPositionsBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortedVelocitiesBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        sortedStatesBuffer = new ComputeBuffer(numParticles, sizeof(uint));
        sortedColorsBuffer = new ComputeBuffer(numParticles, sizeof(float) * 4);

        splatPointsBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3, ComputeBufferType.Append);
        splatCountBuffer = new ComputeBuffer(1, sizeof(int), ComputeBufferType.IndirectArguments);
        paintCounterBuffer = new ComputeBuffer(1, sizeof(uint));
    }

    void UploadHoles()
    {
        int n = Mathf.Max(1, holes.Length);
        var data = new Vector4[n];
        for (int i = 0; i < holes.Length; i++)
            data[i] = new Vector4(holes[i].localPosition.x, holes[i].localPosition.y, holes[i].localPosition.z, holes[i].radius);

        if (holes.Length == 0) data[0] = new Vector4(0, -999f, 0, 0f);
        holeBuffer.SetData(data);
    }

    void InitializeParticles()
    {
        if (twoDropletsMode)
        {
            InitializeTwoDroplets();
            return;
        }

        Vector3 center = bucketTransform != null ? bucketTransform.position : Vector3.zero;
        Quaternion rot = bucketTransform != null ? bucketTransform.rotation : Quaternion.identity;

        var positions = new Vector3[numParticles];
        var velocities = new Vector3[numParticles];
        float bottomY = -bucketHeight * 0.5f;
        float fillRadius = bucketRadius * 0.9f;

        // --- توزيع الجسيمات على كامل حجم السطل بالتساوي ---
        // نملأ نسبة fill من ارتفاع السطل، ونحسب المسافة بين الجسيمات
        // من العدد الفعلي (مش من smoothingRadius) - وإلا الجسيمات الزائدة تتكدّس.
        float fillHeight = bucketHeight * 0.95f * initialFillRatio;
        float fillVolume = Mathf.PI * fillRadius * fillRadius * fillHeight;
        // المسافة بين الجسيمات = الجذر التكعيبي لـ (الحجم / العدد)
        float spacing = Mathf.Pow(fillVolume / Mathf.Max(1, numParticles), 1f / 3f);
        spacing = Mathf.Max(spacing, 0.0005f); // حماية من القيم الصفرية
        // ---------------------------------------------------

        const int MAX_AXIS = 150;
        int nx = Mathf.Clamp(Mathf.CeilToInt((fillRadius * 2f) / spacing), 1, MAX_AXIS);
        int ny = Mathf.Clamp(Mathf.CeilToInt(fillHeight / spacing), 1, MAX_AXIS);

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
                if (px * px + pz * pz > fillRadius * fillRadius) continue;
                for (int iy = 0; iy < ny && candidates.Count < maxCandidates; iy++)
                {
                    float py = bottomY + (iy + 0.5f) * actualSpacingY;
                    Vector3 jitter = Random.insideUnitSphere * Mathf.Min(actualSpacingXZ, actualSpacingY) * 0.15f;
                    candidates.Add(new Vector3(px, py, pz) + jitter);
                }
            }
        }

        for (int i = 0; i < numParticles; i++)
        {
            Vector3 local;
            if (i < candidates.Count) local = candidates[i];
            else
            {
                float r = fillRadius * Mathf.Sqrt(Random.value);
                float a = Random.Range(0f, Mathf.PI * 2f);
                float y = bottomY + Random.Range(0f, fillHeight);
                local = new Vector3(r * Mathf.Cos(a), y, r * Mathf.Sin(a));
            }

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
        stateBuffer.SetData(new uint[numParticles]);

        colorBuffer = new ComputeBuffer(numParticles, sizeof(float) * 4);
        var colors = new Vector4[numParticles];
        for (int i = 0; i < numParticles; i++)
        {
            if (useFixedColors)
            {
                Vector3 local = Quaternion.Inverse(rot) * (positions[i] - center);
                float angle = Mathf.Atan2(local.z, local.x);
                float hue = (angle + Mathf.PI) / (2f * Mathf.PI);
                Color c = Color.HSVToRGB(hue, 0.85f, 1f);
                colors[i] = new Vector4(c.r, c.g, c.b, 1f);
            }
            else
            {
                colors[i] = new Vector4(paintColor.r, paintColor.g, paintColor.b, 1f);
            }
        }
        colorBuffer.SetData(colors);

        if (autoRestDensity)
        {
            restDensity = EstimateRestDensity(positions);
        }
    }
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
            Vector3 p;
            do { p = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)); } while (p.sqrMagnitude > 1f);
            positions[i] = dropCenter + p * dropletRadius;

            float toward = first ? dropletApproachSpeed : -dropletApproachSpeed;
            velocities[i] = new Vector3(toward, 0f, 0f);
            colors[i] = first ? col1 : col2;
        }

        positionBuffer.SetData(positions);
        prevPositionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);
        stateBuffer.SetData(new uint[numParticles]);

        colorBuffer = new ComputeBuffer(numParticles, sizeof(float) * 4);
        colorBuffer.SetData(colors);

        if (autoRestDensity)
        {
            restDensity = EstimateRestDensity(positions);
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
        kUpdateHash = compute.FindKernel("UpdateSpatialHash");
        kReorderPosVel = compute.FindKernel("ReorderData_PosVel");
        kReorderStateColor = compute.FindKernel("ReorderData_StateColor");
        kReorderCopyBackPosVel = compute.FindKernel("ReorderCopyBack_PosVel");
        kReorderCopyBackStateColor = compute.FindKernel("ReorderCopyBack_StateColor");
        kRelax = compute.FindKernel("DoubleDensityRelax");
        kVelocity = compute.FindKernel("ComputeVelocity");
        kCheckHoles = compute.FindKernel("CheckHoles");
        kVortex = compute.FindKernel("ApplyVortex");
        kPaintCanvas = compute.FindKernel("PaintOnCanvas");

        int[] particleKernels = { kGravity, kRelax, kVelocity, kCheckHoles, kVortex, kPaintCanvas, kUpdateHash };
        foreach (int k in particleKernels)
        {
            compute.SetBuffer(k, "Positions", positionBuffer);
            compute.SetBuffer(k, "PrevPositions", prevPositionBuffer);
            compute.SetBuffer(k, "Velocities", velocityBuffer);
            compute.SetBuffer(k, "States", stateBuffer);
            compute.SetBuffer(k, "Holes", holeBuffer);

            if (k == kUpdateHash || k == kRelax)
                compute.SetBuffer(k, "SpatialKeys", spatialHash.SpatialKeys);

            // تم إضافة SortedIndices لدالة الاسترخاء هنا لحل مشكلة التجميد
            if (k == kRelax)
            {
                compute.SetBuffer(k, "SpatialOffsets", spatialHash.SpatialOffsets);
                compute.SetBuffer(k, "SortedIndices", spatialHash.SpatialIndices);
            }
        }

        int[] reorderKernels = { kReorderPosVel, kReorderStateColor, kReorderCopyBackPosVel, kReorderCopyBackStateColor };
        foreach (int k in reorderKernels)
        {
            compute.SetBuffer(k, "SortedIndices", spatialHash.SpatialIndices);
            compute.SetBuffer(k, "Positions", positionBuffer);
            compute.SetBuffer(k, "PrevPositions", prevPositionBuffer);
            compute.SetBuffer(k, "Velocities", velocityBuffer);
            compute.SetBuffer(k, "States", stateBuffer);
            compute.SetBuffer(k, "ParticleColorsIn", colorBuffer);

            compute.SetBuffer(k, "SortedPositions", sortedPositionsBuffer);
            compute.SetBuffer(k, "SortedPrevPositions", sortedPrevPositionsBuffer);
            compute.SetBuffer(k, "SortedVelocities", sortedVelocitiesBuffer);
            compute.SetBuffer(k, "SortedStates", sortedStatesBuffer);
            compute.SetBuffer(k, "ParticleColorsSorted", sortedColorsBuffer);
        }

        compute.SetBuffer(kVelocity, "SplatPoints", splatPointsBuffer);

        if (canvasRT != null)
        {
            compute.SetTexture(kPaintCanvas, "CanvasTex", canvasRT);
            compute.SetBuffer(kPaintCanvas, "CanvasAccum", canvasAccumBuffer);
            compute.SetBuffer(kPaintCanvas, "ParticleColors", colorBuffer);
            compute.SetBuffer(kPaintCanvas, "PaintCounter", paintCounterBuffer);
        }
    }

    void FixedUpdate()
    {
        if (!ready) return;

        Vector3 bucketVel = pendulum != null ? pendulum.GetVelocityVector() : Vector3.zero;
        Vector3 bucketAccel = (bucketVel - prevBucketVel) / Time.fixedDeltaTime;
        prevBucketVel = bucketVel;

        float maxAccel = maxInertiaAccel;
        if (bucketAccel.magnitude > maxAccel)
            bucketAccel = bucketAccel.normalized * maxAccel;

        Vector3 externalAccel = -bucketAccel;
        SetConstants(Time.fixedDeltaTime, externalAccel);

        int pGroups = Mathf.CeilToInt(numParticles / (float)THREADS);

        if (canvas != null) splatPointsBuffer.SetCounterValue(0);

        compute.Dispatch(kGravity, pGroups, 1, 1);

        compute.Dispatch(kUpdateHash, pGroups, 1, 1);
        spatialHash.Run();
        compute.Dispatch(kReorderPosVel, pGroups, 1, 1);
        compute.Dispatch(kReorderStateColor, pGroups, 1, 1);
        compute.Dispatch(kReorderCopyBackPosVel, pGroups, 1, 1);
        compute.Dispatch(kReorderCopyBackStateColor, pGroups, 1, 1);

        for (int it = 0; it < iterations; it++)
        {
            compute.Dispatch(kRelax, pGroups, 1, 1);
        }

        compute.Dispatch(kVelocity, pGroups, 1, 1);

        if (enableVortex) compute.Dispatch(kVortex, pGroups, 1, 1);
        compute.Dispatch(kCheckHoles, pGroups, 1, 1);

        if (canvas != null && canvasRT != null)
        {
            UpdateCanvasConstants();
            // صفّر عدّاد الرسم باستخدام المصفوفة المجهزة مسبقاً (يمنع التقطيع)
            paintCounterBuffer.SetData(zeroArr);
            compute.SetInt("maxSplatsPerFrame", maxSplatsPerFrame);
            compute.Dispatch(kPaintCanvas, pGroups, 1, 1);
        }
    }

    void Update()
    {
        if (ready && paintColor != lastAppliedColor)
        {
            ApplyColorToInBucketParticles(paintColor);
            lastAppliedColor = paintColor;
        }
    }

    void ApplyColorToInBucketParticles(Color c)
    {
        if (colorBuffer == null || !ready) return;
        var states = new uint[numParticles];
        stateBuffer.GetData(states);
        var colors = new Vector4[numParticles];
        colorBuffer.GetData(colors);
        for (int i = 0; i < numParticles; i++)
        {
            if (states[i] == 0) colors[i] = new Vector4(c.r, c.g, c.b, 1f);
        }
        colorBuffer.SetData(colors);
    }

    void UpdateCanvasConstants()
    {
        if (canvas == null) return;
        if (autoCanvasY) canvasY = canvas.transform.position.y + canvasSurfaceOffset;
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
            compute.SetInt("poolMaxRadius", poolMaxRadius);
            compute.SetFloat("poolSaturation", poolSaturation);
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
        compute.SetFloat("velocityDamping", velocityDamping);

        Vector3 c = bucketTransform != null ? bucketTransform.position : Vector3.zero;
        Quaternion q = bucketTransform != null ? bucketTransform.rotation : Quaternion.identity;
        compute.SetVector("bucketCenter", c);
        compute.SetVector("bucketRotation", new Vector4(q.x, q.y, q.z, q.w));
        compute.SetFloat("bucketRadius", bucketRadius);
        compute.SetFloat("bucketHeight", bucketHeight);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("wallRestitution", wallRestitution);
        compute.SetFloat("freeParticleKillDepth", freeParticleKillDepth);
        compute.SetVector("externalAccel", externalAccel);

        compute.SetInt("numHoles", Mathf.Max(1, holes.Length));
        compute.SetFloat("vortexRange", vortexRange);
        compute.SetFloat("vortexPull", vortexPull);
        compute.SetFloat("vortexSpin", vortexSpin);

        UpdateCanvasConstants();
    }

    public int GetParticleCount() => numParticles;
    public ComputeBuffer GetPositionBuffer() => positionBuffer;
    public ComputeBuffer GetVelocityBuffer() => velocityBuffer;
    public ComputeBuffer GetColorBuffer() => colorBuffer;
    public ComputeBuffer GetStateBuffer() => stateBuffer;
    public bool UseFixedColors() => useFixedColors;

    public void SetPaintColor(Color newColor)
    {
        paintColor = newColor;
        lastAppliedColor = newColor;
        ApplyColorToInBucketParticles(newColor);
    }

    void OnDestroy() => ReleaseBuffers();

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
        paintCounterBuffer?.Release();
        if (canvasRT != null) canvasRT.Release();
        canvasAccumBuffer?.Release();
        spatialHash?.Release();

        sortedPositionsBuffer?.Release();
        sortedPrevPositionsBuffer?.Release();
        sortedVelocitiesBuffer?.Release();
        sortedStatesBuffer?.Release();
        sortedColorsBuffer?.Release();

        positionBuffer = null;
        spatialHash = null;
    }

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