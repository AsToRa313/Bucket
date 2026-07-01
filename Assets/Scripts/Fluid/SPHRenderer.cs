using UnityEngine;

/// <summary>
/// رندر جزيئات SPH بـ GPU Instancing
/// كل جزيء كرة صغيرة، لونها يتغير حسب السرعة
/// </summary>
public class SPHRenderer : MonoBehaviour
{
    [Header("المراجع")]
    public SPHSimulation1 simulation;

    [Header("شكل الجزيئات")]
    [Range(0.01f, 0.2f)]
    public float particleSize = 0.05f;

    [Header("ألوان")]
    public Color colorSlow = new Color(0.1f, 0.4f, 0.9f, 1f); // أزرق = بطيء
    public Color colorFast = new Color(1.0f, 0.3f, 0.0f, 1f); // أحمر = سريع
    [Tooltip("استخدم لون الدهان الموحّد من SPHSimulation بدل تدرّج السرعة")]
    public bool useUnifiedColor = true;
    public float speedScale = 3f; // سرعة = maxSpeed لتغيير اللون

    Material mat;
    Mesh mesh;
    ComputeBuffer argsBuffer;
    readonly uint[] args = new uint[5];

    void Start()
    {
        if (simulation == null)
        {
            Debug.LogError("❌ SPHRenderer: simulation فارغ!");
            return;
        }

        var shader = Shader.Find("Custom/SPHParticle");
        if (shader == null)
        {
            Debug.LogError("❌ SPHRenderer: ما لقيت شيدر Custom/SPHParticle!");
            return;
        }

        mat = new Material(shader);
        mesh = BuildSphereMesh();

        argsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint),
                                       ComputeBufferType.IndirectArguments);
        RefreshArgs();
        Debug.Log("✅ SPHRenderer جاهز");
    }

    void RefreshArgs()
    {
        args[0] = mesh.GetIndexCount(0);
        args[1] = (uint)simulation.GetParticleCount();
        args[2] = mesh.GetIndexStart(0);
        args[3] = (uint)mesh.GetBaseVertex(0);
        args[4] = 0;
        argsBuffer.SetData(args);
    }

    Mesh BuildSphereMesh()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var m = go.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(go);
        return m;
    }

    void Update()
    {
        if (mat == null || argsBuffer == null || simulation == null) return;

        mat.SetBuffer("_PositionBuffer", simulation.GetPositionBuffer());
        mat.SetBuffer("_VelocityBuffer", simulation.GetVelocityBuffer());
        mat.SetBuffer("_ColorBuffer", simulation.GetColorBuffer());
        mat.SetFloat("_Size", particleSize);

        // منطق الألوان:
        // - useUnifiedColor: كل جسيم يعرض لونه الفعلي من colorBuffer
        //   (يدعم ألوان متعددة - لون الدهان وقت خروج كل جسيم)
        // - useFixedColors (تشخيص): قوس قزح من colorBuffer
        // كلاهما يقرأ colorBuffer، فنفعّل _UseFixedColor في الحالتين
        Color cSlow = colorSlow;
        Color cFast = colorFast;
        float useBuffer = 0f;
        if (useUnifiedColor || simulation.UseFixedColors())
        {
            // اقرأ اللون الفعلي لكل جسيم من الـ buffer
            useBuffer = 1f;
        }
        mat.SetFloat("_UseFixedColor", useBuffer);
        mat.SetColor("_ColorSlow", cSlow);
        mat.SetColor("_ColorFast", cFast);
        mat.SetFloat("_SpeedScale", speedScale);

        Graphics.DrawMeshInstancedIndirect(
            mesh, 0, mat,
            new Bounds(Vector3.zero, Vector3.one * 200f),
            argsBuffer
        );
    }

    void OnDestroy()
    {
        argsBuffer?.Release();
        if (mat != null) Destroy(mat);
    }
}