using UnityEngine;

public class SPHRenderer : MonoBehaviour
{
    [Header("=== المراجع ===")]
    public SPHSimulation simulation;

    [Header("=== شكل الجزيئات ===")]
    public float particleSize  = 0.08f;
    public Color particleColor = new Color(1f, 0.2f, 0.2f, 1f);

    Material mat;
    Mesh     mesh;
    ComputeBuffer argsBuffer;
    uint[] args = new uint[5];

    void Start()
    {
        // تأكد إن الـ simulation جاهز
        if (simulation == null)
        {
            Debug.LogError("❌ Simulation غير مربوط بالـ SPHRenderer!");
            return;
        }

        var shader = Shader.Find("Custom/SPHParticle");
        if (shader == null)
        {
            Debug.LogError("❌ ما لقيت الشيدر Custom/SPHParticle!");
            return;
        }

        mat  = new Material(shader);
        mesh = GetSphereMesh();

        argsBuffer = new ComputeBuffer(
            1, args.Length * sizeof(uint),
            ComputeBufferType.IndirectArguments
        );

        args[0] = mesh.GetIndexCount(0);
        args[1] = (uint)simulation.GetParticleCount();
        args[2] = mesh.GetIndexStart(0);
        args[3] = (uint)mesh.GetBaseVertex(0);
        args[4] = 0;
        argsBuffer.SetData(args);

        Debug.Log("✅ SPHRenderer جاهز");
    }

    Mesh GetSphereMesh()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var m  = go.GetComponent<MeshFilter>().sharedMesh;
        Destroy(go);
        return m;
    }

    void Update()
    {
        if (mat == null || argsBuffer == null || simulation == null) return;

        var buf = simulation.GetParticleBuffer();
        if (buf == null) return;

        // *** هاد هو الإصلاح — حدّث البفر كل فريم ***
        mat.SetBuffer("_ParticleBuffer", buf);
        mat.SetFloat ("_Size",           particleSize);
        mat.SetColor ("_Color",          particleColor);

        Graphics.DrawMeshInstancedIndirect(
            mesh, 0, mat,
            new Bounds(Vector3.zero, Vector3.one * 100f),
            argsBuffer
        );
    }

    void OnDestroy()
    {
        argsBuffer?.Release();
        if (mat != null) Destroy(mat);
    }
}