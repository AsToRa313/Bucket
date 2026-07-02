using UnityEngine;

public class SPHRenderer1 : MonoBehaviour
{
    [Header("المراجع")]
    public SimpleFluidBox simulation; // تم التعديل ليتصل بالسكربت المبسّط

    [Header("شكل الجزيئات")]
    [Range(0.01f, 0.2f)]
    public float particleSize = 0.05f;

    [Header("ألوان (حسب السرعة)")]
    public Color colorSlow = new Color(0.1f, 0.4f, 0.9f, 1f); // أزرق = بطيء
    public Color colorFast = new Color(1.0f, 0.3f, 0.0f, 1f); // أحمر = سريع
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
        Debug.Log("✅ SPHRenderer جاهز للنسخة المبسّطة");
    }

    void RefreshArgs()
    {
        args[0] = mesh.GetIndexCount(0);
        args[1] = (uint)simulation.numParticles; // تم التعديل لقراءة المتغير المباشر
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

        // نمرر المواقع والسرعات فقط (لا يوجد ColorBuffer في النسخة المبسطة)
        mat.SetBuffer("_PositionBuffer", simulation.GetPositionBuffer());
        mat.SetBuffer("_VelocityBuffer", simulation.GetVelocityBuffer());

        mat.SetFloat("_Size", particleSize);
        mat.SetColor("_ColorSlow", colorSlow);
        mat.SetColor("_ColorFast", colorFast);
        mat.SetFloat("_SpeedScale", speedScale);

        // نخبر الشيدر أن يعتمد على السرعة دائماً ولا يبحث عن لون ثابت
        mat.SetFloat("_UseFixedColor", 0f);

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