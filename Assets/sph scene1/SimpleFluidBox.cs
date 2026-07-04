using UnityEngine;

public class SimpleFluidBox : MonoBehaviour
{
    [Header("المراجع")]
    public ComputeShader compute;

    [Header("إعدادات الصندوق (متوازي المستطيلات)")]
    public Vector3 boxSize = new Vector3(2f, 3f, 2f);
    [Range(0f, 0.9f)] public float fillRatio = 0.5f; // نسبة امتلائه بالسائل

    [Header("فيزياء السائل")]
    public int numParticles = 4000;
    [Range(1, 8)] public int iterations = 3;
    public float smoothingRadius = 0.08f;
    public float restDensity = 10f;
    public float stiffness = 1.0f;
    public float nearStiffness = 2.0f;
    [Range(0.9f, 1f)] public float velocityDamping = 0.98f;
    public float gravity = 9.81f;
    [Range(0f, 0.8f)] public float wallRestitution = 0.1f;

    // Buffers
    ComputeBuffer positionBuffer, prevPositionBuffer, velocityBuffer;
    ComputeBuffer cellCountsBuffer, cellStartBuffer, cellEndBuffer, sortedIndicesBuffer, particleCellIndexBuffer;

    int gridResolution, numCells;
    int kGravity, kClearGrid, kCount, kPrefix, kScatter, kRelax, kVelocity;

    void Start()
    {
        SetupGrid();
        CreateBuffers();
        InitializeParticles();
        CacheKernels();
    }

    void SetupGrid()
    {
        float maxDim = Mathf.Max(boxSize.x, Mathf.Max(boxSize.y, boxSize.z));
       
        gridResolution = Mathf.Clamp(Mathf.CeilToInt(maxDim / smoothingRadius), 4, 40);
        numCells = gridResolution * gridResolution * gridResolution;
    }

    void CreateBuffers()
    {
        positionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        prevPositionBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);
        velocityBuffer = new ComputeBuffer(numParticles, sizeof(float) * 3);

        cellCountsBuffer = new ComputeBuffer(numCells, sizeof(uint));
        cellStartBuffer = new ComputeBuffer(numCells, sizeof(uint));
        cellEndBuffer = new ComputeBuffer(numCells, sizeof(uint));
        sortedIndicesBuffer = new ComputeBuffer(numParticles, sizeof(uint));
        particleCellIndexBuffer = new ComputeBuffer(numParticles, sizeof(uint));
    }

    void InitializeParticles()
    {
        var positions = new Vector3[numParticles];
        var velocities = new Vector3[numParticles];

        float halfX = boxSize.x * 0.45f;
        float halfZ = boxSize.z * 0.45f;
        float startY = -boxSize.y * 0.5f;
        float endY = startY + (boxSize.y * fillRatio);

        // توزيع عشوائي داخل الجزء السفلي من الصندوق
        for (int i = 0; i < numParticles; i++)
        {
            positions[i] = new Vector3(
                Random.Range(-halfX, halfX),
                Random.Range(startY, endY),
                Random.Range(-halfZ, halfZ)
            ) + transform.position; // ربط المركز بموقع الـ GameObject
        }

        positionBuffer.SetData(positions);
        prevPositionBuffer.SetData(positions);
        velocityBuffer.SetData(velocities);
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

        int[] allKernels = { kGravity, kCount, kScatter, kRelax, kVelocity };
        foreach (int k in allKernels)
        {
            compute.SetBuffer(k, "Positions", positionBuffer);
            compute.SetBuffer(k, "PrevPositions", prevPositionBuffer);
            compute.SetBuffer(k, "Velocities", velocityBuffer);
            compute.SetBuffer(k, "CellCounts", cellCountsBuffer);
            compute.SetBuffer(k, "CellStart", cellStartBuffer);
            compute.SetBuffer(k, "CellEnd", cellEndBuffer);
            compute.SetBuffer(k, "SortedIndices", sortedIndicesBuffer);
            compute.SetBuffer(k, "ParticleCellIndex", particleCellIndexBuffer);
        }

        compute.SetBuffer(kClearGrid, "CellCounts", cellCountsBuffer);

        compute.SetBuffer(kPrefix, "CellCounts", cellCountsBuffer);
        compute.SetBuffer(kPrefix, "CellStart", cellStartBuffer);

        // 👇 هذا هو السطر المفقود الذي كان يفجّر الكرت!
        compute.SetBuffer(kPrefix, "CellEnd", cellEndBuffer);
    }

    void FixedUpdate()
    {
        compute.SetInt("numParticles", numParticles);
        compute.SetFloat("deltaTime", Time.fixedDeltaTime);
        compute.SetFloat("gravity", gravity);
        compute.SetFloat("smoothingRadius", smoothingRadius);
        compute.SetFloat("restDensity", restDensity);
        compute.SetFloat("stiffness", stiffness);
        compute.SetFloat("nearStiffness", nearStiffness);
        compute.SetFloat("velocityDamping", velocityDamping);
        compute.SetFloat("wallRestitution", wallRestitution);
        compute.SetVector("boxSize", boxSize);
        compute.SetVector("boxCenter", transform.position); 
        compute.SetInt("gridResolution", gridResolution);

        // إحداثيات الشبكة
        Vector3 gridMin = transform.position - (Vector3.one * (gridResolution * smoothingRadius * 0.5f));
        compute.SetVector("gridMin", gridMin);

        int pGroups = Mathf.CeilToInt(numParticles / 256f);
        int cGroups = Mathf.CeilToInt(numCells / 256f);

        // تنفيذ المحاكاة
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
    }

    void OnDrawGizmos()
    {
        // رسم حدود الصندوق الوهمية في الـ Scene
        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(transform.position, boxSize);
    }

    void OnDestroy()
    {
        positionBuffer?.Release();
        prevPositionBuffer?.Release();
        velocityBuffer?.Release();
        cellCountsBuffer?.Release();
        cellStartBuffer?.Release();
        cellEndBuffer?.Release();
        sortedIndicesBuffer?.Release();
        particleCellIndexBuffer?.Release();
    }

    // لكي يتمكن SPHRenderer من قراءة المواقع
    public ComputeBuffer GetPositionBuffer() => positionBuffer;
    public ComputeBuffer GetVelocityBuffer() => velocityBuffer;
}