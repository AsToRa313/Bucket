using UnityEngine;

/// <summary>
/// المشهد التوضيحي 1: سائل في صندوق مستطيل.
/// يبيّن حركة السائل واستقراره. اضغط Space لهزّ الإطار.
/// يستخدم خوارزمية Clavet Double Density (نفس المشروع الأساسي) لكن مبسّطة.
/// </summary>
public class FluidBoxDemo : MonoBehaviour
{
    [Header("Compute Shader")]
    public ComputeShader compute;

    [Header("إعدادات الجسيمات")]
    public int numParticles = 3000;
    public int iterations = 3;

    [Header("معاملات السائل (Clavet)")]
    public float smoothingRadius = 0.15f;
    public float restDensity = 4f;
    public float stiffness = 0.5f;
    public float nearStiffness = 0.5f;
    public float velocityDamping = 0.99f;
    public float gravity = 9.81f;
    public float collisionDamping = 0.3f;

    [Header("الصندوق (Box) - نصف الأبعاد من المركز")]
    public Vector3 boxSize = new Vector3(1.5f, 2f, 1.5f);
    public bool showBoxGizmo = true;

    [Header("الهزّ (Shake)")]
    [Tooltip("قوة الهزّ عند ضغط Space")]
    public float shakeForce = 25f;
    [Tooltip("مدة الهزّة بالثواني")]
    public float shakeDuration = 0.3f;

    [Header("العرض (Rendering)")]
    public Mesh particleMesh;
    [Tooltip("الماتيريال (لو فارغ، يُنشأ تلقائياً من الشيدر)")]
    public Material particleMaterial;
    [Tooltip("شيدر الجسيمات - يُستخدم لو الماتيريال فارغ")]
    public Shader particleShader;
    public float particleSize = 0.08f;
    public Color fluidColor = new Color(0.2f, 0.5f, 0.95f, 1f);

    // buffers
    ComputeBuffer positionBuffer, velocityBuffer, prevPositionBuffer;
    ComputeBuffer cellCounts, cellStart, cellEnd, sortedIndices, particleCellIndex;
    ComputeBuffer argsBuffer;

    int kIntegrate, kClear, kCount, kPrefix, kScatter, kDensity, kVelocity;
    const int THREADS = 256;

    Vector3Int gridRes;
    int numCells;
    float cellSize;
    Vector3 gridOrigin;

    Vector3 shakeAccel = Vector3.zero;
    float shakeTimer = 0f;
    bool ready = false;

    void Start()
    {
        if (compute == null) { Debug.LogError("Compute فارغ!"); return; }

        // إنشاء ماتيريال تلقائي إذا لم يُربط (يضمن الشيدر الصحيح)
        if (particleMaterial == null)
        {
            Shader sh = particleShader != null ? particleShader : Shader.Find("Custom/DemoParticle");
            if (sh == null)
            {
                Debug.LogError("شيدر Custom/DemoParticle غير موجود! تأكد من إضافة DemoParticle.shader");
                return;
            }
            particleMaterial = new Material(sh);
            Debug.Log("[FluidBoxDemo] أُنشئ ماتيريال تلقائياً من الشيدر");
        }

        SetupGrid();
        CreateBuffers();
        InitParticles();
        CacheKernels();
        SetupRenderArgs();
        ready = true;
    }

    void SetupGrid()
    {
        cellSize = smoothingRadius;
        // الشبكة تغطي الصندوق + هامش
        Vector3 fullSize = boxSize * 2f + Vector3.one * cellSize * 2f;
        gridRes = new Vector3Int(
            Mathf.CeilToInt(fullSize.x / cellSize),
            Mathf.CeilToInt(fullSize.y / cellSize),
            Mathf.CeilToInt(fullSize.z / cellSize));
        numCells = gridRes.x * gridRes.y * gridRes.z;
        gridOrigin = transform.position - boxSize - Vector3.one * cellSize;
    }

    void CreateBuffers()
    {
        positionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        velocityBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        prevPositionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        cellCounts = new ComputeBuffer(numCells, sizeof(uint));
        cellStart = new ComputeBuffer(numCells, sizeof(uint));
        cellEnd = new ComputeBuffer(numCells, sizeof(uint));
        sortedIndices = new ComputeBuffer(numParticles, sizeof(uint));
        particleCellIndex = new ComputeBuffer(numParticles, sizeof(uint));
    }

    void InitParticles()
    {
        var positions = new Vector3[numParticles];
        var velocities = new Vector3[numParticles];
        Vector3 center = transform.position;
        // املأ النصف السفلي من الصندوق بالسائل
        Vector3 fillMin = center - new Vector3(boxSize.x * 0.8f, boxSize.y * 0.9f, boxSize.z * 0.8f);
        Vector3 fillMax = center + new Vector3(boxSize.x * 0.8f, boxSize.y * 0.0f, boxSize.z * 0.8f);
        for (int i = 0; i < numParticles; i++)
        {
            positions[i] = new Vector3(
                Random.Range(fillMin.x, fillMax.x),
                Random.Range(fillMin.y, fillMax.y),
                Random.Range(fillMin.z, fillMax.z));
            velocities[i] = Vector3.zero;
        }
        positionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);
        prevPositionBuffer.SetData(positions);
    }

    void CacheKernels()
    {
        kIntegrate = compute.FindKernel("Integrate");
        kClear = compute.FindKernel("ClearGrid");
        kCount = compute.FindKernel("CountParticles");
        kPrefix = compute.FindKernel("PrefixSum");
        kScatter = compute.FindKernel("ScatterParticles");
        kDensity = compute.FindKernel("DoubleDensity");
        kVelocity = compute.FindKernel("ComputeVelocity");

        int[] pk = { kIntegrate, kCount, kScatter, kDensity, kVelocity };
        foreach (int k in pk)
        {
            compute.SetBuffer(k, "Positions", positionBuffer);
            compute.SetBuffer(k, "Velocities", velocityBuffer);
            compute.SetBuffer(k, "PrevPositions", prevPositionBuffer);
            compute.SetBuffer(k, "CellCounts", cellCounts);
            compute.SetBuffer(k, "CellStart", cellStart);
            compute.SetBuffer(k, "CellEnd", cellEnd);
            compute.SetBuffer(k, "SortedIndices", sortedIndices);
            compute.SetBuffer(k, "ParticleCellIndex", particleCellIndex);
        }
        int[] gk = { kClear, kPrefix };
        foreach (int k in gk)
        {
            compute.SetBuffer(k, "CellCounts", cellCounts);
            compute.SetBuffer(k, "CellStart", cellStart);
            compute.SetBuffer(k, "CellEnd", cellEnd);
        }
    }

    void Update()
    {
        // زر الهزّ
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Vector3 dir = new Vector3(Random.Range(-1f, 1f), 0.3f, Random.Range(-1f, 1f)).normalized;
            shakeAccel = dir * shakeForce;
            shakeTimer = shakeDuration;
        }
        if (shakeTimer > 0f)
        {
            shakeTimer -= Time.deltaTime;
            if (shakeTimer <= 0f) shakeAccel = Vector3.zero;
        }

        RenderParticles();
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
    }

    void RenderParticles()
    {
        if (particleMaterial == null || particleMesh == null || argsBuffer == null) return;
        particleMaterial.SetBuffer("_PositionBuffer", positionBuffer);
        particleMaterial.SetBuffer("_VelocityBuffer", velocityBuffer);
        particleMaterial.SetFloat("_Size", particleSize);
        particleMaterial.SetFloat("_UseFixedColor", 0f);
        particleMaterial.SetColor("_ColorSlow", fluidColor);
        particleMaterial.SetColor("_ColorFast", fluidColor);
        particleMaterial.SetFloat("_SpeedScale", 3f);

        Bounds bounds = new Bounds(transform.position, boxSize * 4f);
        Graphics.DrawMeshInstancedIndirect(particleMesh, 0, particleMaterial, bounds, argsBuffer);
    }

    void FixedUpdate()
    {
        if (!ready) return;

        float dt = Time.fixedDeltaTime;
        int pGroups = Mathf.CeilToInt(numParticles / (float)THREADS);
        int cGroups = Mathf.CeilToInt(numCells / (float)THREADS);

        SetConstants(dt);

        // Integrate (جاذبية) مرة واحدة قبل الحلقة - مثل المشروع الأساسي
        compute.Dispatch(kIntegrate, pGroups, 1, 1);

        // حلقة الاسترخاء: بناء الشبكة + الكثافة المزدوجة فقط
        for (int it = 0; it < iterations; it++)
        {
            compute.Dispatch(kClear, cGroups, 1, 1);
            compute.Dispatch(kCount, pGroups, 1, 1);
            compute.Dispatch(kPrefix, 1, 1, 1);
            compute.Dispatch(kScatter, pGroups, 1, 1);
            compute.Dispatch(kDensity, pGroups, 1, 1);
        }

        // تحديث السرعة مرة واحدة بعد الحلقة
        compute.Dispatch(kVelocity, pGroups, 1, 1);
    }

    void SetConstants(float dt)
    {
        compute.SetInt("numParticles", numParticles);
        compute.SetFloat("deltaTime", dt);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("restDensity", restDensity);
        compute.SetFloat("stiffness", stiffness);
        compute.SetFloat("nearStiffness", nearStiffness);
        compute.SetFloat("velocityDamping", velocityDamping);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetVector("shakeAccel", shakeAccel);

        Vector3 c = transform.position;
        compute.SetVector("boxMin", c - boxSize);
        compute.SetVector("boxMax", c + boxSize);

        compute.SetVector("gridOrigin", gridOrigin);
        compute.SetFloat("cellSize", cellSize);
        compute.SetInts("gridResolution", gridRes.x, gridRes.y, gridRes.z);
        compute.SetInt("numCells", numCells);
    }

    void OnDrawGizmos()
    {
        if (!showBoxGizmo) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, boxSize * 2f);
    }

    void OnDestroy()
    {
        positionBuffer?.Release();
        velocityBuffer?.Release();
        prevPositionBuffer?.Release();
        cellCounts?.Release();
        cellStart?.Release();
        cellEnd?.Release();
        sortedIndices?.Release();
        particleCellIndex?.Release();
        argsBuffer?.Release();
    }
}