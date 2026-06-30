using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// محاكاة SPH (Smoothed Particle Hydrodynamics) للطلاء
/// تشتغل على مستويين:
///   1. حركة السائل داخل الدلو أثناء التأرجح
///   2. تدفق قطرات الطلاء على اللوحة
/// </summary>
public class PaintSPH : MonoBehaviour
{
    // ============================================================
    //  إعدادات الجسيمات
    // ============================================================
    [Header("Particle Settings")]
    [Tooltip("عدد جسيمات SPH داخل الدلو")]
    public int maxParticles = 200;

    [Tooltip("نصف قطر تأثير كل جسيم (h)")]
    public float smoothingRadius = 0.3f;

    [Tooltip("نسبة امتلاء الدلو عند البداية (0-1) — تُستخدم مباشرة بدل data")]
    [Range(0f, 1f)]
    public float initialFillRatio = 0.6f;

    [Tooltip("كتلة كل جسيم")]
    public float particleMass = 0.02f;

    [Tooltip("كثافة الراحة للطلاء (kg/m³) - الماء ≈1000، الطلاء أثقل قليلاً")]
    public float restDensity = 1100f;

    // ============================================================
    //  ثوابت فيزياء الطلاء
    // ============================================================
    [Header("Paint Physics")]
    [Tooltip("معامل الضغط (stiffness) - يتحكم بمدى ضغط السائل")]
    public float pressureStiffness = 200f;

    [Tooltip("لزوجة الطلاء - قيمة أعلى = طلاء أكثف وأبطأ")]
    public float viscosity = 0.5f;

    [Tooltip("تنعيم XSPH (0.05-0.3): يلغي الاهتزاز عالي التردد ويزيد الاستقرار")]
    [Range(0f, 0.5f)]
    public float xsphStrength = 0.15f;

    [Tooltip("تخميد السرعة لكل خطوة (0 = بدون، 0.05-0.1 = استقرار جيد). يهدّئ الجزيئات")]
    [Range(0f, 0.3f)]
    public float velocityDamping = 0.08f;

    [Tooltip("عدد الخطوات الفرعية لكل إطار فيزياء (يمنع عدم الاستقرار العددي). 8-12 مناسب")]
    [Range(1, 20)]
    public int subSteps = 10;

    [Tooltip("شد سطحي للطلاء")]
    public float surfaceTension = 0.07f;

    [Tooltip("تسارع الجاذبية (يُقرأ من PendulumData إذا كان متاحاً)")]
    public float gravity = 9.81f;

    // ============================================================
    //  إعدادات الربط مع باقي المشروع
    // ============================================================
    [Header("Bucket Integration")]
    [Tooltip("ربط بـ SphericalPendulumMath لقراءة حركة الدلو")]
    public SphericalPendulumMath pendulum;

    [Tooltip("ربط بـ PendulumData المشتركة")]
    public PendulumData data;

    [Tooltip("Transform الدلو (حاوية الجسيمات)")]
    public Transform bucketTransform;

    [Tooltip("نصف قطر الدلو الداخلي")]
    public float bucketRadius = 0.25f;

    [Tooltip("ارتفاع الدلو الداخلي")]
    public float bucketHeight = 0.4f;

    // ============================================================
    //  إعدادات التدفق على اللوحة
    // ============================================================
    [Header("Paint Flow on Canvas")]
    [Tooltip("معدل إطلاق قطرات الطلاء (جسيم/ثانية)")]
    public float drainRate = 5f;

    [Tooltip("تفعيل تصريف القطرات من فتحة السطل (أطفئه للتركيز على السائل الداخلي)")]
    public bool enableDraining = false;

    [Tooltip("قطر فتحة خروج الطلاء")]
    public float holeRadius = 0.03f;

    [Tooltip("سطح اللوحة (Canvas Transform)")]
    public Transform canvasTransform;

    [Tooltip("نصف حجم اللوحة")]
    public Vector2 canvasSize = new Vector2(2f, 2f);

    [Tooltip("لون الطلاء الحالي")]
    public Color paintColor = Color.red;

    // ============================================================
    //  إعدادات العرض
    // ============================================================
    [Header("Visualization")]
    public bool showParticles = true;
    public float particleRenderSize = 0.05f;
    public Material particleMaterial;

    // ============================================================
    //  هياكل البيانات الداخلية
    // ============================================================

    /// <summary>جسيم SPH واحد</summary>
    private struct SPHParticle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float density;
        public float pressure;
        public Vector3 force;
        public bool isInBucket;   // true = داخل الدلو، false = قطرة على اللوحة
        public Color color;
        public float life;         // عمر القطرة خارج الدلو (ثواني)
        public Vector3 xsphCorrection; // تصحيح سرعة XSPH للتنعيم
    }

    private SPHParticle[] particles;
    private int activeCount = 0;

    // Grid تسريع البحث عن الجيران
    private Dictionary<Vector3Int, List<int>> spatialGrid
        = new Dictionary<Vector3Int, List<int>>();

    // وقت آخر إطلاق قطرة
    private float lastDrainTime = 0f;

    // Mesh لرسم الجسيمات
    private Mesh particleMesh;
    private Matrix4x4[] renderMatrices;

    // ============================================================
    //  دوال Unity
    // ============================================================

    void Awake()
    {
        particles = new SPHParticle[maxParticles];
        renderMatrices = new Matrix4x4[maxParticles];
        particleMesh = CreateSphereMesh();

        InitializeBucketParticles();
    }

    void FixedUpdate()
    {
        // الخطوة الزمنية الفرعية: تقسيم الإطار لخطوات صغيرة مستقرة عددياً
        // (الخطوة الكاملة 20ms كبيرة جداً للـ SPH وتسبب اهتزازاً دائماً)
        int n = Mathf.Max(1, subSteps);
        float subDt = Time.fixedDeltaTime / n;

        for (int step = 0; step < n; step++)
        {
            // 0. حساب معاملات النواة مرة واحدة لكل خطوة
            PrecomputeKernels();

            // 1. بناء Grid المكاني لتسريع البحث
            BuildSpatialGrid();

            // 2. حساب الكثافة والضغط
            ComputeDensityAndPressure();

            // 3. حساب القوى (ضغط + لزوجة + جاذبية + شد سطحي)
            ComputeForces();

            // 4. تكامل الحركة (Euler-Cromer) بخطوة فرعية
            Integrate(subDt);

            // 5. حدود الدلو والعالم
            EnforceBoundaries();
        }

        // 6. إطلاق قطرات من فتحة الدلو (مرة واحدة لكل إطار)
        DrainPaintDrops();
    }

    void Update()
    {
        if (showParticles && particleMaterial != null)
            RenderParticles();
    }

    // ============================================================
    //  تهيئة جسيمات البداية داخل الدلو
    // ============================================================
    void InitializeBucketParticles()
    {
        // التعبئة من قيمة PaintSPH مباشرة (data.fillRatio مهمل لأنه قد يكون صفر)
        float fillRatio = Mathf.Clamp01(initialFillRatio);
        int bucketFill = Mathf.RoundToInt(maxParticles * fillRatio);
        bucketFill = Mathf.Clamp(bucketFill, 0, maxParticles);

        Vector3 center = bucketTransform != null
            ? bucketTransform.position
            : Vector3.zero;
        Quaternion rot = bucketTransform != null
            ? bucketTransform.rotation
            : Quaternion.identity;

        // نظام إحداثيات موحّد: المركز عند local.y = 0
        //   القاع  = -bucketHeight * 0.5
        //   السقف  = +bucketHeight * 0.5
        // عمود السائل يملأ من القاع للأعلى بمقدار (bucketHeight * fillRatio)
        float bottomY = -bucketHeight * 0.5f;
        float fillHeight = bucketHeight * fillRatio;

        // توزيع منظّم خفيف (jitter) بدل العشوائية البحتة لتجنّب التكدّس
        for (int i = 0; i < bucketFill && i < maxParticles; i++)
        {
            // توزيع قطري متجانس المساحة: sqrt يمنع تكدّس الجسيمات بالمركز
            float r = bucketRadius * 0.85f * Mathf.Sqrt(Random.value);
            float angle = Random.Range(0f, Mathf.PI * 2f);

            // y ضمن عمود السائل فقط [bottomY , bottomY + fillHeight]
            float y = bottomY + Random.Range(0f, fillHeight);

            Vector3 localPos = new Vector3(
                r * Mathf.Cos(angle),
                y,
                r * Mathf.Sin(angle));

            particles[i] = new SPHParticle
            {
                position = center + rot * localPos,
                velocity = Vector3.zero,
                isInBucket = true,
                color = paintColor,
                life = 0f
            };
        }
        activeCount = bucketFill;
    }

    // ============================================================
    //  بناء Grid المكاني (O(n) بدل O(n²))
    // ============================================================

    void BuildSpatialGrid()
    {
        spatialGrid.Clear();
        float cellSize = smoothingRadius;

        for (int i = 0; i < activeCount; i++)
        {
            Vector3Int cell = WorldToCell(particles[i].position, cellSize);
            if (!spatialGrid.ContainsKey(cell))
                spatialGrid[cell] = new List<int>();
            spatialGrid[cell].Add(i);
        }
    }

    Vector3Int WorldToCell(Vector3 pos, float size)
        => new Vector3Int(
            Mathf.FloorToInt(pos.x / size),
            Mathf.FloorToInt(pos.y / size),
            Mathf.FloorToInt(pos.z / size));

    // مخزن جيران مُعاد استخدامه (يتجنب تخصيص ذاكرة في كل استدعاء)
    private List<int> _neighborBuffer = new List<int>(128);

    List<int> GetNeighbors(int idx)
    {
        _neighborBuffer.Clear();
        float cell = smoothingRadius;
        Vector3Int c = WorldToCell(particles[idx].position, cell);

        for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    var key = new Vector3Int(c.x + dx, c.y + dy, c.z + dz);
                    if (spatialGrid.TryGetValue(key, out var list))
                        _neighborBuffer.AddRange(list);
                }
        return _neighborBuffer;
    }

    // ============================================================
    //  Kernels الـ SPH
    // ============================================================

    // Poly6 Kernel - لحساب الكثافة (يستخدم المعاملات المحسوبة مسبقاً)
    float Poly6(float r)
    {
        if (r >= smoothingRadius) return 0f;
        float diff = _h2 - r * r;
        return _poly6Coeff * diff * diff * diff;
    }

    // Spiky Gradient - لحساب قوة الضغط
    Vector3 SpikyGrad(Vector3 rVec, float r)
    {
        if (r >= smoothingRadius || r < 0.0001f) return Vector3.zero;
        float hr = smoothingRadius - r;
        return _spikyCoeff * hr * hr * (rVec / r);
    }

    // Viscosity Laplacian - لحساب قوة اللزوجة
    float ViscLaplacian(float r)
    {
        if (r >= smoothingRadius) return 0f;
        return _viscCoeff * (smoothingRadius - r);
    }

    // ============================================================
    //  حساب الكثافة والضغط (مرحلة 1)
    // ============================================================

    void ComputeDensityAndPressure()
    {
        for (int i = 0; i < activeCount; i++)
        {
            float density = 0f;
            var neighbors = GetNeighbors(i);

            foreach (int j in neighbors)
            {
                float r = Vector3.Distance(particles[i].position, particles[j].position);
                density += particleMass * Poly6(r);
            }

            // نضمن حد أدنى للكثافة
            particles[i].density = Mathf.Max(density, restDensity * 0.1f);

            // معادلة حالة الغاز (Equation of State) - مناسبة للطلاء
            // P = k * (ρ - ρ₀)
            // نمنع الضغط السالب: السوائل لا تولّد قوة جذب من الضغط،
            // الضغط السالب يجعل الجسيمات تتجاذب وتنهار. clamp إلى 0.
            particles[i].pressure = Mathf.Max(0f,
                pressureStiffness * (particles[i].density - restDensity));
        }
    }

    // ============================================================
    //  حساب القوى (مرحلة 2)
    // ============================================================

    void ComputeForces()
    {
        // الجاذبية الفعلية (قد تتأثر بحركة الدلو)
        Vector3 gravityVec = new Vector3(0f, -gravity, 0f);

        // تسارع الدلو (يؤثر على السائل الداخلي)
        Vector3 bucketAccel = Vector3.zero;
        if (pendulum != null)
        {

            // جديد - صح
            Vector3 vel = pendulum != null ? pendulum.GetVelocityVector() : Vector3.zero;
            bucketAccel = (vel - _prevBucketVel) / Time.fixedDeltaTime;
            _prevBucketVel = vel;
        }

        for (int i = 0; i < activeCount; i++)
        {
            Vector3 fPressure = Vector3.zero;
            Vector3 fViscosity = Vector3.zero;
            Vector3 fSurface = Vector3.zero;
            Vector3 xsph = Vector3.zero;
            var neighbors = GetNeighbors(i);

            foreach (int j in neighbors)
            {
                if (i == j) continue;

                Vector3 rVec = particles[i].position - particles[j].position;
                float sqr = rVec.sqrMagnitude;
                // تخطٍّ مبكر: تجاهل الجيران خارج نصف قطر التأثير قبل حساب sqrt
                if (sqr >= _h2) continue;
                float r = Mathf.Sqrt(sqr);

                // --- قوة الضغط (صيغة متماثلة symmetric — أكثر استقراراً) ---
                float di = particles[i].density;
                float dj = particles[j].density;
                float pressureTerm = particles[i].pressure / (di * di)
                                   + particles[j].pressure / (dj * dj);
                fPressure -= particleMass * particleMass * pressureTerm
                             * SpikyGrad(rVec, r);

                // --- قوة اللزوجة ---
                Vector3 velDiff = particles[j].velocity - particles[i].velocity;
                fViscosity += particleMass
                    * (velDiff / (particles[j].density + 0.001f))
                    * ViscLaplacian(r);

                // --- تصحيح XSPH: تنعيم السرعة نحو متوسط الجيران ---
                // velDiff = vj - vi ، نوزّنه بالنواة وبمتوسط الكثافة
                float wj = Poly6(r);
                xsph += (2f * particleMass / (di + dj)) * velDiff * wj;
            }

            fViscosity *= viscosity;
            particles[i].xsphCorrection = xsphStrength * xsph;

            // --- الجاذبية + تأثير حركة الدلو على السائل الداخلي ---
            // القوة = الكتلة × التسارع (وحدات متّسقة مع قوة الضغط)
            Vector3 fGravity = particleMass * gravityVec;
            if (particles[i].isInBucket)
                fGravity -= particleMass * bucketAccel; // قوة الخمول العكسية

            particles[i].force = fPressure + fViscosity + fGravity + fSurface;
        }
    }

    private Vector3 _prevBucketVel = Vector3.zero;

    // معاملات النواة المحسوبة مسبقاً (تُحدّث مرة كل إطار بدل كل زوج جسيمات)
    private float _poly6Coeff, _spikyCoeff, _viscCoeff, _h2;
    void PrecomputeKernels()
    {
        float h = smoothingRadius;
        _h2 = h * h;
        _poly6Coeff = 315f / (64f * Mathf.PI * Mathf.Pow(h, 9f));
        _spikyCoeff = -45f / (Mathf.PI * Mathf.Pow(h, 6f));
        _viscCoeff = 45f / (Mathf.PI * Mathf.Pow(h, 6f));
    }

    // ============================================================
    //  تكامل الحركة (Euler-Cromer) - مرحلة 3
    // ============================================================

    void Integrate(float dt)
    {
        // تخميد متناسب مع الخطوة: حتى لا يتضاعف التأثير مع عدد الخطوات الفرعية
        float dampPerStep = velocityDamping * dt / Time.fixedDeltaTime;

        for (int i = 0; i < activeCount; i++)
        {
            if (particles[i].density < 0.0001f) continue;

            // التسارع = القوة / الكتلة (وحدات فيزيائية صحيحة)
            Vector3 accel = particles[i].force / particleMass;
            particles[i].velocity += accel * dt;

            // تخميد عام: يمتص الحركة الزائدة فتهدأ الجزيئات وتستقر
            particles[i].velocity *= (1f - dampPerStep);

            float maxSpeed = 10f;
            if (particles[i].velocity.magnitude > maxSpeed)
                particles[i].velocity = particles[i].velocity.normalized * maxSpeed;

            // XSPH: حرّك الموضع بسرعة منعّمة نحو متوسط الجيران (يلغي الاهتزاز)
            Vector3 smoothedVel = particles[i].velocity + particles[i].xsphCorrection;
            particles[i].position += smoothedVel * dt;

            // لما القطرة توصل الأرضية Y=0
            if (!particles[i].isInBucket && particles[i].position.y <= 0f)
            {
                // أرسل للـ CanvasPainter
                if (canvasTransform != null)
                {
                    CanvasPainter painter = canvasTransform.GetComponent<CanvasPainter>();
                    if (painter != null)
                        painter.Splat(particles[i].position,
                                      particles[i].velocity,
                                      particles[i].color);
                }

                // أوقف الجسيم
                particles[i].velocity = Vector3.zero;
                particles[i].isInBucket = false;
                particles[i].life = 999f; // خلّيه يختفي
            }

            if (!particles[i].isInBucket)
                particles[i].life += dt;
        }
    }

    // ============================================================
    //  تطبيق حدود الدلو والعالم - مرحلة 4
    // ============================================================

    void EnforceBoundaries()
    {
        if (bucketTransform == null) return;

        Vector3 center = bucketTransform.position;
        Quaternion rot = bucketTransform.rotation;
        float restitution = 0.3f;

        for (int i = 0; i < activeCount; i++)
        {
            if (!particles[i].isInBucket) continue;

            // تحويل لإحداثيات الدلو المحلية
            Vector3 local = Quaternion.Inverse(rot)
                            * (particles[i].position - center);

            bool changed = false;

            // النظام المركزي: القاع عند -bucketHeight*0.5 ، السقف عند +bucketHeight*0.5
            float bottomY = -bucketHeight * 0.5f;
            float topY = bucketHeight * 0.5f;

            // حد القاع
            if (local.y < bottomY)
            {
                local.y = bottomY;
                changed = true;
                Vector3 lv = Quaternion.Inverse(rot) * particles[i].velocity;
                lv.y = Mathf.Abs(lv.y) * restitution;
                particles[i].velocity = rot * lv;
            }

            // حد السقف — منع الخروج من الأعلى
            if (local.y > topY)
            {
                local.y = topY;
                changed = true;
                Vector3 lv = Quaternion.Inverse(rot) * particles[i].velocity;
                lv.y = -Mathf.Abs(lv.y) * restitution;
                particles[i].velocity = rot * lv;
            }

            // حد الجوانب
            float xzDist = new Vector2(local.x, local.z).magnitude;
            if (xzDist > bucketRadius)
            {
                Vector2 xz = new Vector2(local.x, local.z).normalized * bucketRadius;
                local.x = xz.x;
                local.z = xz.y;
                changed = true;

                Vector3 lv = Quaternion.Inverse(rot) * particles[i].velocity;
                Vector2 radial = new Vector2(local.x, local.z).normalized;
                float dot = Vector2.Dot(new Vector2(lv.x, lv.z), radial);
                if (dot > 0f)
                {
                    lv.x -= radial.x * dot * (1f + restitution);
                    lv.z -= radial.y * dot * (1f + restitution);
                }
                particles[i].velocity = rot * lv;
            }

            if (changed)
                particles[i].position = center + rot * local;
        }
    }

    // ============================================================
    //  إطلاق قطرات الطلاء من فتحة الدلو
    // ============================================================

    void DrainPaintDrops()
    {
        if (!enableDraining) return;
        // نعتمد على وجود جسيمات فعلية داخل الدلو بدل currentPaintMass
        // (الأخير قد يكون صفراً في ملف البيانات)
        if (Time.time - lastDrainTime < 1f / drainRate) return;

        lastDrainTime = Time.time;

        // موقع الفتحة (قاع الدلو)
        Vector3 holePos = bucketTransform != null
            ? bucketTransform.position + bucketTransform.up * (-bucketHeight * 0.5f)
            : Vector3.zero;

        // البحث عن جسيم حر لإعادة استخدامه كقطرة
        for (int i = 0; i < activeCount; i++)
        {
            if (particles[i].isInBucket
                && Vector3.Distance(particles[i].position, holePos) < holeRadius * 3f)
            {
                // تحويل هذا الجسيم إلى قطرة خارجية
                particles[i].isInBucket = false;
                particles[i].life = 0f;

                // سرعة أولية للقطرة (باتجاه اللوحة)
                Vector3 dir = canvasTransform != null
                    ? (canvasTransform.position - holePos).normalized
                    : Vector3.down;
                // جديد - صح
                Vector3 pendulumVel = pendulum != null ? pendulum.GetVelocityVector() * 0.3f : Vector3.zero;
                particles[i].velocity = dir * 2f + pendulumVel;

                break;
            }
        }

        // إضافة جسيم جديد إذا كان لا يزال في النطاق
        if (activeCount < maxParticles)
        {
            particles[activeCount] = new SPHParticle
            {
                position = holePos + Random.insideUnitSphere * holeRadius,
                velocity = Vector3.down * 1.5f,
                isInBucket = false,
                color = paintColor,
                life = 0f
            };
            activeCount++;
        }
    }

    // ============================================================
    //  رسم الجسيمات
    // ============================================================

    void RenderParticles()
    {
        if (particleMaterial == null || activeCount == 0) return;

        for (int i = 0; i < activeCount; i++)
        {
            float scale = particles[i].isInBucket
                ? particleRenderSize
                : particleRenderSize * 1.5f;

            Graphics.DrawMesh(
                particleMesh,
                Matrix4x4.TRS(particles[i].position, Quaternion.identity, Vector3.one * scale),
                particleMaterial,
                0);
        }
    }

    // ============================================================
    //  API عام — يُستدعى من سكريبتات أخرى
    // ============================================================

    /// <summary>إرجاع متوسط سرعة السائل داخل الدلو (لريما ومحاكاة التدفق)</summary>
    public Vector3 GetFluidVelocityInsideBucket()
    {
        Vector3 avg = Vector3.zero;
        int cnt = 0;
        for (int i = 0; i < activeCount; i++)
        {
            if (particles[i].isInBucket) { avg += particles[i].velocity; cnt++; }
        }
        return cnt > 0 ? avg / cnt : Vector3.zero;
    }

    /// <summary>نسبة امتلاء الدلو (0-1) محسوبة من الجسيمات الفعلية</summary>
    public float GetFillRatio()
    {
        int inBucket = 0;
        for (int i = 0; i < activeCount; i++)
            if (particles[i].isInBucket) inBucket++;
        return (float)inBucket / maxParticles;
    }

    /// <summary>أقرب جسيم خارج الدلو إلى موقع معين (لرسم الطلاء على اللوحة)</summary>
    public bool TryGetNearestDroplet(Vector3 queryPos, float radius,
        out Vector3 hitPos, out Color hitColor)
    {
        hitPos = Vector3.zero;
        hitColor = Color.clear;
        float best = radius;

        for (int i = 0; i < activeCount; i++)
        {
            if (particles[i].isInBucket) continue;
            float d = Vector3.Distance(particles[i].position, queryPos);
            if (d < best)
            {
                best = d;
                hitPos = particles[i].position;
                hitColor = particles[i].color;
            }
        }
        return best < radius;
    }

    // ============================================================
    //  مساعدات
    // ============================================================

    Mesh CreateSphereMesh()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        var m = go.GetComponent<MeshFilter>().mesh;
        Destroy(go);
        return m;
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || particles == null) return;

        for (int i = 0; i < activeCount; i++)
        {
            Gizmos.color = particles[i].isInBucket
                ? new Color(0f, 0.5f, 1f, 0.5f)
                : new Color(1f, 0.2f, 0f, 0.7f);
            Gizmos.DrawSphere(particles[i].position, particleRenderSize * 0.5f);
        }
    }




}
