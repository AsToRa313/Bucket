using UnityEngine;

/// <summary>
/// المشهد التوضيحي 2: قطرتان بلونين مختلفين تسقطان وتندمجان.
/// أحمر + أزرق => بنفسجي (خلط الألوان عند الاندماج).
/// يبيّن التماسك (cohesion) واندماج الألوان في السوائل.
/// اضغط R لإعادة التشغيل.
/// </summary>
public class DropletMergeDemo : MonoBehaviour
{
    [Header("Compute Shader")]
    public ComputeShader compute;

    [Header("إعدادات الجسيمات")]
    [Tooltip("عدد الجسيمات في كل قطرة")]
    public int particlesPerDroplet = 800;
    public int iterations = 4;

    [Header("معاملات السائل (Clavet)")]
    public float smoothingRadius = 0.12f;
    public float restDensity = 5f;
    public float stiffness = 0.4f;
    public float nearStiffness = 0.4f;
    public float velocityDamping = 0.99f;
    public float gravity = 4f;
    public float collisionDamping = 0.2f;

    [Header("القطرتان")]
    [Tooltip("نصف قطر كل قطرة")]
    public float dropletRadius = 0.3f;
    [Tooltip("المسافة الأفقية بين القطرتين")]
    public float separation = 0.5f;
    [Tooltip("ارتفاع بدء السقوط")]
    public float startHeight = 1.5f;
    public Color color1 = new Color(0.9f, 0.15f, 0.15f, 1f);  // أحمر
    public Color color2 = new Color(0.15f, 0.3f, 0.95f, 1f);  // أزرق

    [Header("الاندماج")]
    [Tooltip("سرعة خلط الألوان عند التلامس")]
    public float colorMixRate = 2f;

    [Header("القاع (أرضية للاندماج)")]
    public Vector3 boxSize = new Vector3(2f, 2f, 2f);
    public bool showBoxGizmo = true;

    [Header("العرض")]
    public Mesh particleMesh;
    [Tooltip("الماتيريال (لو فارغ، يُنشأ تلقائياً)")]
    public Material particleMaterial;
    [Tooltip("شيدر الجسيمات - يُستخدم لو الماتيريال فارغ")]
    public Shader particleShader;
    public float particleSize = 0.06f;

    int totalParticles;
    ComputeBuffer positionBuffer, velocityBuffer, prevPositionBuffer, colorBuffer;
    ComputeBuffer cellCounts, cellStart, cellEnd, sortedIndices, particleCellIndex;
    ComputeBuffer argsBuffer;

    int kIntegrate, kClear, kCount, kPrefix, kScatter, kDensity, kVelocity, kMix;
    const int THREADS = 256;

    Vector3Int gridRes;
    int numCells;
    float cellSize;
    Vector3 gridOrigin;
    bool ready = false;

    void Start()
    {
        if (compute == null) { Debug.LogError("Compute فارغ!"); return; }

        if (particleMaterial == null)
        {
            Shader sh = particleShader != null ? particleShader : Shader.Find("Custom/DemoParticle");
            if (sh == null)
            {
                Debug.LogError("شيدر Custom/DemoParticle غير موجود!");
                return;
            }
            particleMaterial = new Material(sh);
            Debug.Log("[DropletMergeDemo] أُنشئ ماتيريال تلقائياً");
        }

        totalParticles = particlesPerDroplet * 2;
        SetupGrid();
        CreateBuffers();
        InitDroplets();
        CacheKernels();
        SetupRenderArgs();
        ready = true;
    }

    void SetupGrid()
    {
        cellSize = smoothingRadius;
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
        positionBuffer = new ComputeBuffer(totalParticles, sizeof(float) * 3);
        velocityBuffer = new ComputeBuffer(totalParticles, sizeof(float) * 3);
        prevPositionBuffer = new ComputeBuffer(totalParticles, sizeof(float) * 3);
        colorBuffer = new ComputeBuffer(totalParticles, sizeof(float) * 4);
        cellCounts = new ComputeBuffer(numCells, sizeof(uint));
        cellStart = new ComputeBuffer(numCells, sizeof(uint));
        cellEnd = new ComputeBuffer(numCells, sizeof(uint));
        sortedIndices = new ComputeBuffer(totalParticles, sizeof(uint));
        particleCellIndex = new ComputeBuffer(totalParticles, sizeof(uint));
    }

    void InitDroplets()
    {
        var positions = new Vector3[totalParticles];
        var velocities = new Vector3[totalParticles];
        var colors = new Vector4[totalParticles];
        Vector3 center = transform.position;

        // القطرة 1 (يسار، أحمر)
        Vector3 c1 = center + new Vector3(-separation * 0.5f, startHeight, 0);
        FillSphere(positions, colors, 0, particlesPerDroplet, c1, color1);
        // القطرة 2 (يمين، أزرق)
        Vector3 c2 = center + new Vector3(separation * 0.5f, startHeight, 0);
        FillSphere(positions, colors, particlesPerDroplet, particlesPerDroplet, c2, color2);

        for (int i = 0; i < totalParticles; i++)
            velocities[i] = Vector3.zero;

        positionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);
        prevPositionBuffer.SetData(positions);
        colorBuffer.SetData(colors);
    }

    // يملأ كرة بالجسيمات بلون معيّن
    void FillSphere(Vector3[] pos, Vector4[] col, int startIdx, int count, Vector3 center, Color c)
    {
        Vector4 cv = new Vector4(c.r, c.g, c.b, 1f);
        for (int i = 0; i < count; i++)
        {
            // توزيع عشوائي داخل كرة
            Vector3 p;
            do
            {
                p = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f));
            } while (p.sqrMagnitude > 1f);
            pos[startIdx + i] = center + p * dropletRadius;
            col[startIdx + i] = cv;
        }
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
        kMix = compute.FindKernel("MixColors");

        int[] pk = { kIntegrate, kCount, kScatter, kDensity, kVelocity, kMix };
        foreach (int k in pk)
        {
            compute.SetBuffer(k, "Positions", positionBuffer);
            compute.SetBuffer(k, "Velocities", velocityBuffer);
            compute.SetBuffer(k, "PrevPositions", prevPositionBuffer);
            compute.SetBuffer(k, "Colors", colorBuffer);
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
        if (Input.GetKeyDown(KeyCode.R))
            InitDroplets();   // إعادة التشغيل

        RenderParticles();
    }

    void FixedUpdate()
    {
        if (!ready) return;

        float dt = Time.fixedDeltaTime;
        int pGroups = Mathf.CeilToInt(totalParticles / (float)THREADS);
        int cGroups = Mathf.CeilToInt(numCells / (float)THREADS);

        SetConstants(dt);

        // Integrate (جاذبية) مرة واحدة قبل الحلقة
        compute.Dispatch(kIntegrate, pGroups, 1, 1);

        // حلقة الاسترخاء: بناء الشبكة + الكثافة المزدوجة
        for (int it = 0; it < iterations; it++)
        {
            compute.Dispatch(kClear, cGroups, 1, 1);
            compute.Dispatch(kCount, pGroups, 1, 1);
            compute.Dispatch(kPrefix, 1, 1, 1);
            compute.Dispatch(kScatter, pGroups, 1, 1);
            compute.Dispatch(kDensity, pGroups, 1, 1);
        }

        // تحديث السرعة + خلط الألوان مرة واحدة بعد الحلقة
        compute.Dispatch(kVelocity, pGroups, 1, 1);
        compute.Dispatch(kMix, pGroups, 1, 1);
    }

    void SetConstants(float dt)
    {
        compute.SetInt("numParticles", totalParticles);
        compute.SetFloat("deltaTime", dt);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("restDensity", restDensity);
        compute.SetFloat("stiffness", stiffness);
        compute.SetFloat("nearStiffness", nearStiffness);
        compute.SetFloat("velocityDamping", velocityDamping);
        compute.SetFloat("collisionDamping", collisionDamping);
        compute.SetFloat("colorMixRate", colorMixRate);
        compute.SetVector("shakeAccel", Vector3.zero);

        Vector3 c = transform.position;
        compute.SetVector("boxMin", c - boxSize);
        compute.SetVector("boxMax", c + boxSize);

        compute.SetVector("gridOrigin", gridOrigin);
        compute.SetFloat("cellSize", cellSize);
        compute.SetInts("gridResolution", gridRes.x, gridRes.y, gridRes.z);
        compute.SetInt("numCells", numCells);
    }

    void SetupRenderArgs()
    {
        uint[] args = new uint[5] { 0, 0, 0, 0, 0 };
        if (particleMesh != null)
        {
            args[0] = particleMesh.GetIndexCount(0);
            args[1] = (uint)totalParticles;
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
        particleMaterial.SetBuffer("_ColorBuffer", colorBuffer);
        particleMaterial.SetFloat("_Size", particleSize);
        particleMaterial.SetFloat("_UseFixedColor", 1f);

        Bounds bounds = new Bounds(transform.position, boxSize * 4f);
        Graphics.DrawMeshInstancedIndirect(particleMesh, 0, particleMaterial, bounds, argsBuffer);
    }

    void OnDrawGizmos()
    {
        if (!showBoxGizmo) return;
        Gizmos.color = Color.gray;
        Gizmos.DrawWireCube(transform.position, boxSize * 2f);
    }

    void OnDestroy()
    {
        positionBuffer?.Release();
        velocityBuffer?.Release();
        prevPositionBuffer?.Release();
        colorBuffer?.Release();
        cellCounts?.Release();
        cellStart?.Release();
        cellEnd?.Release();
        sortedIndices?.Release();
        particleCellIndex?.Release();
        argsBuffer?.Release();
    }
}