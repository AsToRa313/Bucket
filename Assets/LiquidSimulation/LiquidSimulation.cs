// =============================================================
// LiquidSimulation.cs
// ضَع هذا الـ Script على نفس الـ GameObject الذي يحمل
// SphericalPendulumMath (وعاء البندول)
// =============================================================

using UnityEngine;
using UnityEngine.Rendering;

public class LiquidSimulation : MonoBehaviour
{
    // =============================================================
    //  الحقول العامة (تظهر في الـ Inspector)
    // =============================================================

    [Header("─── المراجع الإلزامية ───")]

    [Tooltip("اسحب ملف LiquidParticle.compute من مجلد المشروع إلى هنا")]
    public ComputeShader computeShader;

    [Tooltip("اسحب المادة التي أنشأتها من LiquidParticle.shader إلى هنا")]
    public Material liquidMaterial;

    [Tooltip("اسحب Sphere Mesh من المشروع. لإيجاده: في أي GameObject اختر Mesh Filter ثم خذ الـ Mesh")]
    public Mesh particleMesh;

    [Header("─── معاملات الجسيمات ───")]

    [Tooltip("عدد الجسيمات — يُستحسن أن يكون مضاعفاً لـ 64 للأداء الأمثل")]
    [Range(64, 1024)]
    public int particleCount = 256;

    [Tooltip("كتلة كل جسيم بالكيلوغرام")]
    public float particleMass = 1f;

    [Header("─── أبعاد الوعاء ─── (اضبطها لتطابق حجم الوعاء في المشهد)")]

    [Tooltip("نصف القطر الداخلي للوعاء بالمتر")]
    public float bowlRadius = 0.45f;

    [Tooltip("المسافة من مركز الوعاء إلى قاعه (قيمة موجبة)")]
    public float bowlBottomDepth = 0.3f;

    [Header("─── فيزياء السائل (SPH) ───")]

    [Tooltip("نصف قطر التأثير h: زيادته تزيد السيولة، تقليله يجعل السائل متقطعاً")]
    public float smoothingRadius = 0.18f;

    [Tooltip("كثافة الراحة ρ₀: اضبطها حتى لا يتجمع السائل أو يتمدد")]
    public float restDensity = 1f;

    [Tooltip("معامل الضغط k: كلما زاد كان السائل أصعب في الضغط عليه")]
    public float pressureConstant = 40f;

    [Tooltip("معامل اللزوجة μ: يجعل السائل يسير بسلاسة (0=ماء، 1=عسل)")]
    [Range(0f, 1f)]
    public float viscosity = 0.08f;

    [Tooltip("معامل التخامد: يمتص الطاقة الزائدة لمنع الاهتزاز العشوائي")]
    [Range(0.9f, 0.999f)]
    public float damping = 0.995f;

    [Header("─── مظهر السائل ───")]

    [Tooltip("حجم كل جسيم في التصيير")]
    public float particleRenderSize = 0.06f;

    [Tooltip("لون السائل مع الشفافية")]
    public Color liquidColor = new Color(0.15f, 0.45f, 0.95f, 0.85f);

    // =============================================================
    //  المتغيرات الخاصة
    // =============================================================

    // مخزن بيانات الجسيمات على الـ GPU
    // نستخدم GraphicsBuffer (الأحدث في Unity 6) بدلاً من ComputeBuffer
    private GraphicsBuffer particleBuffer;

    // لتمرير البيانات للـ Material بدون إنشاء مادة جديدة في كل إطار
    private MaterialPropertyBlock propertyBlock;

    // حدود التصيير (يحتاجها Unity لتحديد ما إذا كان يجب رسم الجسيمات)
    private Bounds renderBounds;

    // فهارس نوى الـ Compute Shader
    private int kernelDensity; // CSComputeDensity
    private int kernelUpdate;  // CSUpdateParticles

    // عدد مجموعات الـ Threads المطلوبة
    private int threadGroups;

    // لحساب تسارع الوعاء عبر المشتقة الثانية للموقع
    private Vector3 prevBowlPosition;
    private Vector3 prevBowlVelocity;
    private Vector3 bowlAcceleration;
    private Vector3 smoothedAcceleration; // تنعيم للتسارع

    // =============================================================
    //  هيكل بيانات الجسيم
    //  يجب أن يطابق struct Particle في ملف .compute بالضبط
    //  sizeof = 12 + 12 + 4 + 4 = 32 byte
    // =============================================================
    private struct ParticleData
    {
        public Vector3 position; // 12 byte
        public Vector3 velocity; // 12 byte
        public float   density;  //  4 byte
        public float   pressure; //  4 byte
    }

    // =============================================================
    void Start()
    {
        // ─── التحقق من المتطلبات ───
        if (computeShader == null)
        {
            Debug.LogError("[LiquidSimulation] لم يتم تعيين Compute Shader! اسحب LiquidParticle.compute إلى الحقل في الـ Inspector");
            enabled = false;
            return;
        }
        if (liquidMaterial == null)
        {
            Debug.LogError("[LiquidSimulation] لم يتم تعيين Material! أنشئ Material من LiquidParticle.shader واسحبه إلى الحقل");
            enabled = false;
            return;
        }
        if (particleMesh == null)
        {
            Debug.LogError("[LiquidSimulation] لم يتم تعيين Particle Mesh! أنشئ Sphere واسحب الـ Mesh من MeshFilter إلى الحقل");
            enabled = false;
            return;
        }
        if (!SystemInfo.supportsComputeShaders)
        {
            Debug.LogError("[LiquidSimulation] بطاقة الرسوميات لا تدعم Compute Shaders!");
            enabled = false;
            return;
        }

        // ─── تهيئة كل شيء ───
        InitializeBuffer();
        InitializeParticles();
        SetupComputeShader();
        SetupRendering();

        // حفظ الموقع الأولي لحساب التسارع لاحقاً
        prevBowlPosition  = transform.position;
        prevBowlVelocity  = Vector3.zero;
        smoothedAcceleration = Vector3.zero;

        Debug.Log($"[LiquidSimulation] تم تهيئة {particleCount} جسيم سائل بنجاح!");
    }

    // =============================================================
    void InitializeBuffer()
    {
        // إنشاء مخزن الجسيمات
        // GraphicsBuffer.Target.Structured = يمكن استخدامه في Compute Shader والـ Shader معاً
        // stride = 32 byte (حجم ParticleData)
        particleBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.Structured,
            particleCount,
            32
        );
    }

    // =============================================================
    void InitializeParticles()
    {
        ParticleData[] data = new ParticleData[particleCount];

        // ─── توزيع الجسيمات في شبكة دائرية داخل الوعاء ───
        // نستخدم seed ثابتاً لنفس النتيجة في كل مرة نشغل المشهد
        System.Random rng      = new System.Random(12345);
        float   layerSpacing   = (bowlRadius * 1.6f) / Mathf.Max(Mathf.CeilToInt(Mathf.Sqrt(particleCount * 0.5f)), 1);
        int     rings          = Mathf.CeilToInt(Mathf.Sqrt(particleCount));
        int     index          = 0;

        for (int ring = 0; ring <= rings && index < particleCount; ring++)
        {
            // عدد الجسيمات في هذه الحلقة يزداد مع زيادة القطر
            float ringRadius = (ring / (float)rings) * bowlRadius * 0.85f;
            int   ringCount  = Mathf.Max(1, Mathf.RoundToInt(ring * 6));

            for (int i = 0; i < ringCount && index < particleCount; i++)
            {
                // زاوية موزعة بالتساوي على الحلقة مع إزاحة عشوائية خفيفة
                float angle = (i / (float)ringCount) * Mathf.PI * 2f
                            + (float)(rng.NextDouble() - 0.5) * 0.2f;

                // عشوائية خفيفة في المسافة لتجنب التوزيع المنتظم جداً
                float r = ringRadius + (float)(rng.NextDouble() - 0.5) * layerSpacing * 0.2f;

                // الموقع في الإحداثيات المحلية للوعاء
                Vector3 localPos = new Vector3(
                    Mathf.Cos(angle) * r,
                    -bowlBottomDepth * 0.45f + (float)(rng.NextDouble() * bowlBottomDepth * 0.5f),
                    Mathf.Sin(angle) * r
                );

                // تحويل إلى الفضاء العالمي مع مراعاة اتجاه الوعاء
                Vector3 worldPos = transform.position + transform.TransformDirection(localPos);

                data[index] = new ParticleData
                {
                    position = worldPos,
                    velocity = Vector3.zero,
                    density  = restDensity,
                    pressure = 0f
                };
                index++;
            }
        }

        // تعبئة أي جسيمات متبقية في المركز
        while (index < particleCount)
        {
            float lx = (float)(rng.NextDouble() - 0.5) * bowlRadius * 0.3f;
            float lz = (float)(rng.NextDouble() - 0.5) * bowlRadius * 0.3f;
            float ly = -bowlBottomDepth * 0.3f;
            Vector3 localPos = new Vector3(lx, ly, lz);
            data[index] = new ParticleData
            {
                position = transform.position + transform.TransformDirection(localPos),
                velocity = Vector3.zero,
                density  = restDensity,
                pressure = 0f
            };
            index++;
        }

        // رفع البيانات إلى الـ GPU
        particleBuffer.SetData(data);
    }

    // =============================================================
    void SetupComputeShader()
    {
        // الحصول على فهارس النوى
        kernelDensity = computeShader.FindKernel("CSComputeDensity");
        kernelUpdate  = computeShader.FindKernel("CSUpdateParticles");

        // كل مجموعة = 64 خيط (كما حددنا في [numthreads(64,1,1)])
        threadGroups = Mathf.CeilToInt(particleCount / 64f);

        // ربط المخزن بكلتا النواتين
        computeShader.SetBuffer(kernelDensity, "_Particles", particleBuffer);
        computeShader.SetBuffer(kernelUpdate,  "_Particles", particleBuffer);
    }

    // =============================================================
    void SetupRendering()
    {
        propertyBlock = new MaterialPropertyBlock();
        propertyBlock.SetBuffer("_Particles",    particleBuffer);
        propertyBlock.SetFloat( "_ParticleSize", particleRenderSize);
        propertyBlock.SetColor( "_Color",        liquidColor);

        // حدود التصيير: تشمل كل الوعاء مع هامش
        renderBounds = new Bounds(transform.position, Vector3.one * (bowlRadius * 6f));
    }

    // =============================================================
    // FixedUpdate: تحديث المحاكاة الفيزيائية
    // نستخدم FixedUpdate ليتزامن مع نظام الفيزياء في Unity
    // =============================================================
    void FixedUpdate()
    {
        if (particleBuffer == null) return;

        // ─── حساب تسارع الوعاء ───
        // التسارع = مشتقة السرعة = (السرعة الحالية - السرعة السابقة) / الزمن
        Vector3 currentPos = transform.position;
        Vector3 currentVel = (currentPos - prevBowlPosition) / Time.fixedDeltaTime;
        Vector3 rawAccel   = (currentVel - prevBowlVelocity) / Time.fixedDeltaTime;

        // تنعيم التسارع لمنع القفزات المفاجئة عند بدء السحب
        smoothedAcceleration = Vector3.Lerp(smoothedAcceleration, rawAccel, 0.3f);
        bowlAcceleration     = Vector3.ClampMagnitude(smoothedAcceleration, 25f);

        prevBowlVelocity  = currentVel;
        prevBowlPosition  = currentPos;

        // ─── إرسال المعاملات إلى الـ Compute Shader ───
        computeShader.SetInt(   "_ParticleCount",     particleCount);
        computeShader.SetFloat( "_DeltaTime",         Time.fixedDeltaTime);
        computeShader.SetFloat( "_ParticleMass",      particleMass);
        computeShader.SetFloat( "_SmoothingRadius",   smoothingRadius);
        computeShader.SetFloat( "_RestDensity",       restDensity);
        computeShader.SetFloat( "_PressureConstant",  pressureConstant);
        computeShader.SetFloat( "_Viscosity",         viscosity);
        computeShader.SetFloat( "_Damping",           damping);

        // معاملات الوعاء: نُمرر المحاور المحلية لنعرف اتجاه الوعاء في الفضاء العالمي
        computeShader.SetVector("_BowlCenter",        (Vector4)transform.position);
        computeShader.SetVector("_BowlUp",            (Vector4)transform.up);
        computeShader.SetVector("_BowlRight",         (Vector4)transform.right);
        computeShader.SetVector("_BowlForward",       (Vector4)transform.forward);
        computeShader.SetFloat( "_BowlRadius",        bowlRadius);
        computeShader.SetFloat( "_BowlBottomOffset",  -bowlBottomDepth);

        // الفيزياء الخارجية
        computeShader.SetVector("_Gravity",           (Vector4)Physics.gravity);
        computeShader.SetVector("_BowlAcceleration",  (Vector4)bowlAcceleration);

        // ─── تشغيل الـ Compute Shader ───

        // المرحلة 1: حساب كثافة وضغط كل جسيم
        // يجب أن تنتهي هذه المرحلة قبل بدء المرحلة التالية
        computeShader.Dispatch(kernelDensity, threadGroups, 1, 1);

        // المرحلة 2: تطبيق القوى وتحديث المواقع
        computeShader.Dispatch(kernelUpdate, threadGroups, 1, 1);
    }

    // =============================================================
    // Update: تصيير الجسيمات في كل إطار
    // =============================================================
    void Update()
    {
        if (particleBuffer == null || liquidMaterial == null) return;

        // تحديث مركز حدود التصيير مع حركة الوعاء
        renderBounds.center = transform.position;

        // تحديث الـ PropertyBlock بالقيم الحالية
        propertyBlock.SetBuffer("_Particles",    particleBuffer);
        propertyBlock.SetFloat( "_ParticleSize", particleRenderSize);
        propertyBlock.SetColor( "_Color",        liquidColor);

        // ─── تصيير الجسيمات ───
        // DrawMeshInstancedProcedural: يرسم (particleCount) نسخة من الـ Mesh
        // الـ GPU يقرأ موقع كل نسخة مباشرة من particleBuffer
        // لا حاجة لإنشاء GameObjects أو تحريك Transform لكل جسيم
        Graphics.DrawMeshInstancedProcedural(
            particleMesh,              // الشكل (كرة)
            0,                         // رقم الـ Submesh
            liquidMaterial,            // المادة
            renderBounds,              // حدود التصيير
            particleCount,             // عدد النسخ
            propertyBlock,             // الـ Properties (تشمل Buffer و Color)
            ShadowCastingMode.Off,     // إيقاف الظلال للأداء
            false                      // لا استقبال ظلال
        );
    }

    // =============================================================
    // دالة عامة: إعادة تهيئة الجسيمات (يمكن ربطها بزر في الـ UI)
    // =============================================================
    public void ResetSimulation()
    {
        if (particleBuffer == null) return;
        InitializeParticles();
        prevBowlPosition     = transform.position;
        prevBowlVelocity     = Vector3.zero;
        smoothedAcceleration = Vector3.zero;
        bowlAcceleration     = Vector3.zero;
        Debug.Log("[LiquidSimulation] تم إعادة تهيئة المحاكاة");
    }

    // =============================================================
    // تنظيف الذاكرة عند تدمير الكائن
    // هذا ضروري جداً وإلا ستبقى الذاكرة محجوزة على الـ GPU
    // =============================================================
    void OnDestroy()
    {
        if (particleBuffer != null)
        {
            particleBuffer.Release();
            particleBuffer = null;
        }
    }

    // =============================================================
    // رسم مساعد في نافذة Scene لمساعدتك على ضبط أبعاد الوعاء
    // =============================================================
#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        // استخدام مصفوفة الـ Transform للوعاء لرسم الأبعاد بشكل صحيح
        Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);

        // رسم الأسطوانة (الوعاء)
        Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.25f);
        DrawWireCylinder(Vector3.down * (bowlBottomDepth * 0.5f), bowlRadius, bowlBottomDepth);

        // رسم قاع الوعاء
        Gizmos.color = new Color(1f, 0.5f, 0f, 0.4f);
        Gizmos.DrawWireCube(
            Vector3.down * bowlBottomDepth,
            new Vector3(bowlRadius * 2f, 0.02f, bowlRadius * 2f)
        );

        // إعادة المصفوفة
        Gizmos.matrix = Matrix4x4.identity;
    }

    // رسم أسطوانة سلكية للـ Gizmo
    void DrawWireCylinder(Vector3 center, float radius, float height)
    {
        int   segments  = 24;
        float halfH     = height * 0.5f;

        for (int i = 0; i < segments; i++)
        {
            float a1 = (i       / (float)segments) * Mathf.PI * 2f;
            float a2 = ((i + 1) / (float)segments) * Mathf.PI * 2f;

            Vector3 p1 = center + new Vector3(Mathf.Cos(a1) * radius, -halfH, Mathf.Sin(a1) * radius);
            Vector3 p2 = center + new Vector3(Mathf.Cos(a2) * radius, -halfH, Mathf.Sin(a2) * radius);
            Vector3 p3 = center + new Vector3(Mathf.Cos(a1) * radius,  halfH, Mathf.Sin(a1) * radius);
            Vector3 p4 = center + new Vector3(Mathf.Cos(a2) * radius,  halfH, Mathf.Sin(a2) * radius);

            Gizmos.DrawLine(p1, p2);
            Gizmos.DrawLine(p3, p4);
            Gizmos.DrawLine(p1, p3);
        }
    }
#endif
}
