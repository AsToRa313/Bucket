using UnityEngine;

public class SPHSimulation : MonoBehaviour
{
    [Header("=== عدد الجزيئات ===")]
    [Range(50, 1000)]
    public int numParticles = 200;

    [Header("=== إعدادات SPH ===")]
    public float smoothingRadius        = 0.3f;
    public float targetDensity          = 10f;
    public float pressureMultiplier     = 50f;
    public float nearPressureMultiplier = 5f;
    public float viscosityStrength      = 0.1f;
    [Range(0f, 1f)]
    public float collisionDamping       = 0.5f;

    [Header("=== إعدادات الوقت ===")]
    public float timeScale     = 0.5f;
    public int   stepsPerFrame = 1;

    [Header("=== المراجع ===")]
    public ComputeShader sphCompute;
    public SphericalPendulumMath bucketPendulum;

    [Header("=== التسرب ===")]
    public bool  holeOpen   = true;
    public float holeRadius = 0.05f;
    public CanvasPainter canvasPainter;
    public Color paintColor = Color.red;

    // ---- struct مطابق للـ Compute Shader ----
    struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector2 density;
        public float _p0, _p1;
    }

    ComputeBuffer particleBuffer;

    // Kernels
    int k_External, k_Density, k_Pressure, k_Update;

    bool ready = false;

    // تتبع حركة السطل
    Vector3 lastBucketPos;

    // حجم السطل
    readonly Vector3 HALF = new Vector3(0.2f, 0.25f, 0.2f);

    // =================== Start ===================

    void Start()
    {
        if (sphCompute == null)
        {
            Debug.LogError("❌ sphCompute غير مربوط!");
            return;
        }

        k_External = sphCompute.FindKernel("ExternalForces");
        k_Density  = sphCompute.FindKernel("ComputeDensity");
        k_Pressure = sphCompute.FindKernel("ComputePressure");
        k_Update   = sphCompute.FindKernel("UpdatePositions");

        CreateAndFillBuffer();
        BindBuffers();

        lastBucketPos = GetBucketPos();
        ready = true;

        Debug.Log($"✅ SPH جاهز — {numParticles} جزيء");
    }

    void CreateAndFillBuffer()
    {
        int stride = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Particle));
        particleBuffer = new ComputeBuffer(numParticles, stride);

        Vector3 center = GetBucketPos();
        Particle[] particles = new Particle[numParticles];

        for (int i = 0; i < numParticles; i++)
        {
            particles[i].position = center + new Vector3(
                Random.Range(-HALF.x * 0.7f,  HALF.x * 0.7f),
                Random.Range(-HALF.y * 0.7f,  HALF.y * 0.7f),
                Random.Range(-HALF.z * 0.7f,  HALF.z * 0.7f)
            );
            particles[i].velocity = Vector3.zero;
            particles[i].density  = Vector2.one * 0.001f;
        }

        particleBuffer.SetData(particles);
    }

    void BindBuffers()
    {
        foreach (int k in new[] { k_External, k_Density, k_Pressure, k_Update })
            sphCompute.SetBuffer(k, "Particles", particleBuffer);

        sphCompute.SetInt("numParticles", numParticles);
    }

    // =================== Update ===================

    void Update()
    {
        if (!ready) return;

        // حرّك الجزيئات مع السطل على CPU
        Vector3 currentPos = GetBucketPos();
        Vector3 delta      = currentPos - lastBucketPos;

        if (delta.magnitude > 0.0001f)
        {
            Particle[] particles = new Particle[numParticles];
            particleBuffer.GetData(particles);
            for (int i = 0; i < numParticles; i++)
                particles[i].position += delta;
            particleBuffer.SetData(particles);
        }

        lastBucketPos = currentPos;

        // شغّل الـ SPH
        float dt = Mathf.Min(Time.deltaTime * timeScale / stepsPerFrame, 0.016f);
        for (int i = 0; i < stepsPerFrame; i++)
            Step(dt, currentPos);

        // رش دهان
        if (holeOpen) CheckDrip();
    }

    void Step(float dt, Vector3 bPos)
    {
        sphCompute.SetFloat ("deltaTime",              dt);
        sphCompute.SetFloat ("gravity",                -9.81f);
        sphCompute.SetFloat ("smoothingRadius",        smoothingRadius);
        sphCompute.SetFloat ("targetDensity",          targetDensity);
        sphCompute.SetFloat ("pressureMultiplier",     pressureMultiplier);
        sphCompute.SetFloat ("nearPressureMultiplier", nearPressureMultiplier);
        sphCompute.SetFloat ("viscosityStrength",      viscosityStrength);
        sphCompute.SetFloat ("collisionDamping",       collisionDamping);
        sphCompute.SetVector("bucketMin", bPos - HALF);
        sphCompute.SetVector("bucketMax", bPos + HALF);

        int groups = Mathf.CeilToInt(numParticles / 64f);
        sphCompute.Dispatch(k_External, groups, 1, 1);
        sphCompute.Dispatch(k_Density,  groups, 1, 1);
        sphCompute.Dispatch(k_Pressure, groups, 1, 1);
        sphCompute.Dispatch(k_Update,   groups, 1, 1);
    }

    void CheckDrip()
    {
        if (canvasPainter == null) return;
        Vector3 hole = GetBucketPos() + Vector3.down * HALF.y;

        Particle[] data = new Particle[numParticles];
        particleBuffer.GetData(data);

        foreach (var p in data)
        {
            if (Vector3.Distance(p.position, hole) < holeRadius * 3f)
            {
                var ray = new Ray(p.position, Vector3.down);
                if (Physics.Raycast(ray, out RaycastHit hit, 20f)
                    && hit.collider.CompareTag("Canvas"))
                {
                    float sz = 0.015f + p.velocity.magnitude * 0.003f;
                    canvasPainter.Paint(hit.textureCoord, paintColor, sz);
                }
            }
        }
    }

    Vector3 GetBucketPos() =>
    bucketPendulum ? bucketPendulum.GetBucketPosition() : Vector3.zero;

    public ComputeBuffer GetParticleBuffer() => particleBuffer;
    public int GetParticleCount()            => numParticles;

    void OnDestroy() => particleBuffer?.Release();

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;
        Vector3 p = GetBucketPos();
        Gizmos.color = new Color(0, 1, 1, 0.3f);
        Gizmos.DrawWireCube(p, HALF * 2);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(p + Vector3.down * HALF.y, holeRadius * 3f);
    }
}