using UnityEngine;

/// <summary>
/// لوحة تحكم شاملة في وقت التشغيل (Runtime) لكل متغيرات المحاكاة:
/// عدد الجسيمات، أبعاد السطل، زوايا الاهتزاز (theta/phi)، معاملات السائل، ألوان الرسم...
/// بدل تعديل القيم من الكود أو الـ Inspector، يفتح المستخدم اللوحة ويعدّل بالسلايدرز مباشرة.
///
/// طريقة الاستخدام:
/// 1) ضيف هذا السكربت على أي GameObject في المشهد (مثلاً فاضي اسمه "ControlPanel").
/// 2) اسحب المراجع (Pendulum, FluidSim, FluidRenderer, Canvas) في الـ Inspector — أو اتركها
///    فاضية وسيحاول السكربت يلاقيها تلقائياً بـ FindObjectOfType عند التشغيل.
/// 3) شغّل اللعبة واضغط "F1" أو الزر بالزاوية لفتح/قفل اللوحة.
///
/// ملاحظة عن "Rebuild": بعض القيم (عدد الجسيمات، أبعاد السطل، نصف قطر التمويه...) تُستخدم
/// فقط لحظة تهيئة المحاكاة (توزيع الجسيمات + إنشاء الـ Compute Buffers)، فتغييرها لا يظهر أثره
/// فوراً. هذه القيم معلّمة بـ "(Rebuild)" في اللوحة، وبعد تعديلها يجب الضغط على زر
/// "⚠ Rebuild Fluid Simulation" لإعادة بناء المحاكاة بالقيم الجديدة.
/// </summary>
public class SimulationControlPanel : MonoBehaviour
{
    [Header("=== المراجع (اتركها فاضية للبحث التلقائي) ===")]
    public SphericalPendulumMath pendulum;
    public SPHSimulation1 fluidSim;
    public SPHRenderer fluidRenderer;
    public CanvasPainter canvasPainter;

    [Header("=== إعدادات اللوحة ===")]
    public KeyCode toggleKey = KeyCode.F1;
    public bool startVisible = true;

    bool visible;
    Vector2 scroll;
    Rect panelRect = new Rect(20, 20, 380, 0);

    // حالة الطي لكل قسم
    bool secPendulumMotion = true;
    bool secPendulumGeometry = false;
    bool secPendulumMass = false;
    bool secPendulumRope = false;
    bool secPendulumAngle = true;
    bool secFluidSolver = true;
    bool secFluidBucket = false;
    bool secFluidVortex = false;
    bool secFluidDroplets = false;
    bool secFluidRender = false;
    bool secCanvas = false;

    // قيم مؤقتة لزاوية الاهتزاز (theta/phi) قبل تطبيقها
    float pendingTheta;
    float pendingPhi;

    // علم يظهر تنبيه "يحتاج Rebuild"
    bool rebuildNeeded = false;

    GUIStyle headerStyle;
    GUIStyle warnStyle;
    bool stylesReady = false;

    void Start()
    {
        if (pendulum == null) pendulum = FindObjectOfType<SphericalPendulumMath>();
        if (fluidSim == null) fluidSim = FindObjectOfType<SPHSimulation1>();
        if (fluidRenderer == null) fluidRenderer = FindObjectOfType<SPHRenderer>();
        if (canvasPainter == null) canvasPainter = FindObjectOfType<CanvasPainter>();

        visible = startVisible;

        if (pendulum != null)
        {
            pendingTheta = pendulum.GetThetaDegrees();
            pendingPhi = pendulum.GetPhiDegrees();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            visible = !visible;
    }

    //void SetupStyles()
    //{
    //    if (stylesReady) return;
    //    headerStyle = new GUIStyle(GUI.skin.button);
    //    headerStyle.alignment = TextAnchor.MiddleLeft;
    //    headerStyle.fontStyle = FontStyle.Bold;

    //    warnStyle = new GUIStyle(GUI.skin.button);
    //    warnStyle.normal.textColor = new Color(1f, 0.55f, 0.1f);
    //    warnStyle.fontStyle = FontStyle.Bold;
    //    stylesReady = true;
    //}



    void SetupStyles()
    {
        if (stylesReady) return;

        // تكبير الخط الأساسي لكل عناصر الواجهة
        int fontSize = 17; // يمكنك زيادة هذا الرقم إذا أردت خطاً أكبر
        GUI.skin.label.fontSize = fontSize;
        GUI.skin.button.fontSize = fontSize;
        GUI.skin.toggle.fontSize = fontSize;
        GUI.skin.box.fontSize = fontSize;
        GUI.skin.textField.fontSize = fontSize;

        headerStyle = new GUIStyle(GUI.skin.button);
        headerStyle.alignment = TextAnchor.MiddleLeft;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.fontSize = 19; // خط العناوين أكبر قليلاً

        warnStyle = new GUIStyle(GUI.skin.button);
        warnStyle.normal.textColor = new Color(1f, 0.55f, 0.1f);
        warnStyle.fontStyle = FontStyle.Bold;
        warnStyle.fontSize = 16;

        stylesReady = true;
    }

    void OnGUI()
    {
        SetupStyles();

        // زر فتح/إغلاق دائماً ظاهر بالزاوية
        if (GUI.Button(new Rect(10, 10, 140, 26), visible ? "✕ إغلاق اللوحة" : "☰ فتح لوحة التحكم"))
            visible = !visible;

        if (!visible) return;

        panelRect.x = 75;
        panelRect.y = 100;
        panelRect.width = 480;
        panelRect.height = Mathf.Min(Screen.height - 60, 760);

        GUILayout.BeginArea(panelRect, GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("لوحة تحكم المحاكاة — Simulation Control Panel", headerStyle);
        GUILayout.Space(4);

        DrawLiveReadouts();
        GUILayout.Space(6);

        if (pendulum != null) DrawPendulumSection();
        GUILayout.Space(6);
        if (fluidSim != null) DrawFluidSection();
        GUILayout.Space(6);
        if (canvasPainter != null) DrawCanvasSection();

        GUILayout.Space(10);
        DrawRebuildButton();
        GUILayout.Space(10);

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    // ---------------------------------------------------------------------
    // قراءات لحظية (Read-only)
    // ---------------------------------------------------------------------
    void DrawLiveReadouts()
    {
        GUILayout.Box("", GUILayout.Height(1)); // خط فاصل رفيع
        GUILayout.Label("📊 قراءات لحظية");
        if (pendulum != null)
        {
            GUILayout.Label($"  سرعة السطل: {pendulum.GetBucketSpeed():F2} m/s   |   الامتلاء: {pendulum.GetFillRatio() * 100f:F0}%");
            GUILayout.Label($"  Theta: {pendulum.GetThetaDegrees():F1}°   |   Phi: {pendulum.GetPhiDegrees():F1}°");
        }
        if (fluidSim != null)
        {
            GUILayout.Label($"  عدد الجسيمات الحالي: {fluidSim.GetParticleCount()}");
        }
    }

    // ---------------------------------------------------------------------
    // قسم البندول
    // ---------------------------------------------------------------------
    void DrawPendulumSection()
    {
        GUILayout.Label("🔗 البندول (Pendulum)", headerStyle);

        secPendulumMotion = Foldout("الحركة الأساسية (Motion)", secPendulumMotion);
        if (secPendulumMotion)
        {
            pendulum.baseLength = Slider("طول الحبل (Base Length)", pendulum.baseLength, 0.5f, 15f);
            pendulum.gravity = Slider("الجاذبية (Gravity)", pendulum.gravity, 0f, 30f);
            pendulum.airDamping = Slider("تخامد الهواء (Air Damping)", pendulum.airDamping, 0f, 0.05f, "F4");
            pendulum.pivotFriction = Slider("احتكاك المحور (Pivot Friction)", pendulum.pivotFriction, 0f, 1f);
        }

        secPendulumGeometry = Foldout("شكل السطل (Bucket Geometry)", secPendulumGeometry);
        if (secPendulumGeometry)
        {
            pendulum.shape = (SphericalPendulumMath.BucketShape)EnumToolbar(
                "الشكل (Shape)", pendulum.shape);
            pendulum.bucketHeight = Slider("ارتفاع السطل (Bucket Height)", pendulum.bucketHeight, 0.05f, 3f);
            pendulum.bucketRadius = Slider("نصف قطر السطل (Bucket Radius)", pendulum.bucketRadius, 0.02f, 2f);
        }

        secPendulumMass = Foldout("كتلة الدهان (Paint Mass)", secPendulumMass);
        if (secPendulumMass)
        {
            pendulum.emptyBucketMass = Slider("كتلة السطل الفاضي", pendulum.emptyBucketMass, 0.05f, 5f);
            pendulum.maxPaintMass = Slider("أقصى كتلة دهان", pendulum.maxPaintMass, 0.1f, 10f);
            pendulum.currentPaintMass = Slider("كتلة الدهان الحالية", pendulum.currentPaintMass, 0f, pendulum.maxPaintMass);
            pendulum.drainRate = Slider("معدّل التصريف (Drain Rate)", pendulum.drainRate, 0f, 1f);
        }

        secPendulumRope = Foldout("الحبل والفتل (Rope & Torsion)", secPendulumRope);
        if (secPendulumRope)
        {
            pendulum.torsionalStiffness = Slider("صلابة الفتل (Torsional Stiffness)", pendulum.torsionalStiffness, 0f, 5f);
            pendulum.torsionalDamping = Slider("تخامد الفتل (Torsional Damping)", pendulum.torsionalDamping, 0f, 1f);
            pendulum.ropeSegments = IntSlider("عدد أجزاء الحبل (Rope Segments)", pendulum.ropeSegments, 2, 60);
            pendulum.sagFactor = Slider("ترهّل الحبل (Sag Factor)", pendulum.sagFactor, 0f, 2f);
        }

        secPendulumAngle = Foldout("⚡ زاوية الاهتزاز (Theta / Phi)", secPendulumAngle);
        if (secPendulumAngle)
        {
            GUILayout.Label("اضبط زاوية بداية الاهتزاز يدوياً (كأنك سحبت السطل بالماوس):");
            pendingTheta = Slider("Theta (° من القاع)", pendingTheta, 0f, 90f);
            pendingPhi = Slider("Phi (° حول المحور)", pendingPhi, -180f, 180f);
            if (GUILayout.Button("↺ طبّق الزاوية (Apply Angle)"))
                pendulum.SetSphericalAngles(pendingTheta, pendingPhi);
        }
    }

    // ---------------------------------------------------------------------
    // قسم السائل
    // ---------------------------------------------------------------------
    void DrawFluidSection()
    {
        GUILayout.Label("💧 السائل (Fluid / SPH)", headerStyle);

        secFluidSolver = Foldout("عدد الجسيمات والحلّال (Particles & Solver)", secFluidSolver);
        if (secFluidSolver)
        {
            int newCount = IntSlider("عدد الجسيمات (Rebuild)", fluidSim.numParticles, 100, 20000);
            if (newCount != fluidSim.numParticles) { fluidSim.numParticles = newCount; rebuildNeeded = true; }

            float newSmoothing = Slider("نصف قطر التمويه (Rebuild)", fluidSim.smoothingRadius, 0.02f, 0.3f, "F3");
            if (!Mathf.Approximately(newSmoothing, fluidSim.smoothingRadius)) { fluidSim.smoothingRadius = newSmoothing; rebuildNeeded = true; }

            fluidSim.iterations = IntSlider("عدد التكرارات (Iterations)", fluidSim.iterations, 1, 8);
            fluidSim.stiffness = Slider("الصلابة (Stiffness)", fluidSim.stiffness, 0f, 5f);
            fluidSim.nearStiffness = Slider("الصلابة القريبة (Near Stiffness)", fluidSim.nearStiffness, 0f, 8f);
            fluidSim.velocityDamping = Slider("تخامد السرعة (Velocity Damping)", fluidSim.velocityDamping, 0.9f, 1f, "F3");
            fluidSim.gravity = Slider("الجاذبية (Gravity)", fluidSim.gravity, 0f, 30f);
            fluidSim.maxInertiaAccel = Slider("سقف تسارع القصور (Max Inertia Accel)", fluidSim.maxInertiaAccel, 0f, 10f);
            fluidSim.collisionDamping = Slider("تخامد الاصطدام (Collision Damping)", fluidSim.collisionDamping, 0f, 1f);
            fluidSim.wallRestitution = Slider("ارتداد الجدران (Wall Restitution)", fluidSim.wallRestitution, 0f, 0.8f);
            fluidSim.autoRestDensity = Toggle("حساب Rest Density تلقائياً", fluidSim.autoRestDensity);
            if (!fluidSim.autoRestDensity)
                fluidSim.restDensity = Slider("كثافة التوازن (Rest Density)", fluidSim.restDensity, 0.1f, 50f);
        }

        secFluidBucket = Foldout("أبعاد السطل والامتلاء (Bucket & Fill)", secFluidBucket);
        if (secFluidBucket)
        {
            float r = Slider("نصف قطر السطل (Rebuild)", fluidSim.bucketRadius, 0.02f, 2f);
            if (!Mathf.Approximately(r, fluidSim.bucketRadius)) { fluidSim.bucketRadius = r; rebuildNeeded = true; }

            float h = Slider("ارتفاع السطل (Rebuild)", fluidSim.bucketHeight, 0.05f, 3f);
            if (!Mathf.Approximately(h, fluidSim.bucketHeight)) { fluidSim.bucketHeight = h; rebuildNeeded = true; }

            float fill = Slider("نسبة الامتلاء الابتدائية (Rebuild)", fluidSim.initialFillRatio, 0f, 1f);
            if (!Mathf.Approximately(fill, fluidSim.initialFillRatio)) { fluidSim.initialFillRatio = fill; rebuildNeeded = true; }
        }

        secFluidVortex = Foldout("الدوامة والتصريف (Vortex & Drain)", secFluidVortex);
        if (secFluidVortex)
        {
            fluidSim.enableVortex = Toggle("تفعيل الدوامة", fluidSim.enableVortex);
            if (fluidSim.enableVortex)
            {
                fluidSim.vortexRange = Slider("نطاق الدوامة (Range)", fluidSim.vortexRange, 0f, 15f);
                fluidSim.vortexPull = Slider("قوة الجذب (Pull)", fluidSim.vortexPull, 0f, 10f);
                fluidSim.vortexSpin = Slider("قوة الالتفاف (Spin)", fluidSim.vortexSpin, 0f, 10f);
            }
        }

        secFluidDroplets = Foldout("وضع القطرتين التوضيحي (Two Droplets Demo)", secFluidDroplets);
        if (secFluidDroplets)
        {
            bool two = Toggle("تفعيل وضع القطرتين (Rebuild)", fluidSim.twoDropletsMode);
            if (two != fluidSim.twoDropletsMode) { fluidSim.twoDropletsMode = two; rebuildNeeded = true; }

            if (fluidSim.twoDropletsMode)
            {
                float dr = Slider("نصف قطر القطرة (Rebuild)", fluidSim.dropletRadius, 0.05f, 1f);
                if (!Mathf.Approximately(dr, fluidSim.dropletRadius)) { fluidSim.dropletRadius = dr; rebuildNeeded = true; }

                float ds = Slider("المسافة بين القطرتين (Rebuild)", fluidSim.dropletSeparation, 0f, 3f);
                if (!Mathf.Approximately(ds, fluidSim.dropletSeparation)) { fluidSim.dropletSeparation = ds; rebuildNeeded = true; }

                float dh = Slider("ارتفاع القطرتين (Rebuild)", fluidSim.dropletHeight, 0f, 5f);
                if (!Mathf.Approximately(dh, fluidSim.dropletHeight)) { fluidSim.dropletHeight = dh; rebuildNeeded = true; }

                fluidSim.dropletApproachSpeed = Slider("سرعة التقارب (Approach Speed)", fluidSim.dropletApproachSpeed, 0f, 5f);
                fluidSim.dropletColor1 = ColorSliders("لون القطرة 1", fluidSim.dropletColor1);
                fluidSim.dropletColor2 = ColorSliders("لون القطرة 2", fluidSim.dropletColor2);
            }
        }

        secFluidRender = Foldout("الألوان والعرض (Paint Color & Rendering)", secFluidRender);
        if (secFluidRender)
        {
            fluidSim.useFixedColors = Toggle("وضع تشخيص الألوان (قوس قزح)", fluidSim.useFixedColors);

            Color newPaint = ColorSliders("لون الدهان الحالي (Paint Color)", fluidSim.paintColor);
            if (newPaint != fluidSim.paintColor)
                fluidSim.SetPaintColor(newPaint);

            if (fluidRenderer != null)
            {
                fluidRenderer.particleSize = Slider("حجم الجسيم (Particle Size)", fluidRenderer.particleSize, 0.01f, 0.2f);
                fluidRenderer.speedScale = Slider("مقياس السرعة للون (Speed Scale)", fluidRenderer.speedScale, 0.1f, 10f);
                fluidRenderer.useUnifiedColor = Toggle("استخدم لون الدهان الموحّد", fluidRenderer.useUnifiedColor);
                if (!fluidRenderer.useUnifiedColor)
                {
                    fluidRenderer.colorSlow = ColorSliders("لون بطيء (Slow)", fluidRenderer.colorSlow);
                    fluidRenderer.colorFast = ColorSliders("لون سريع (Fast)", fluidRenderer.colorFast);
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    // قسم اللوحة (Canvas Painting)
    // ---------------------------------------------------------------------
    void DrawCanvasSection()
    {
        GUILayout.Label("🎨 لوحة الرسم (Canvas Painting)", headerStyle);
        secCanvas = Foldout("إعدادات الرسم", secCanvas);
        if (!secCanvas) return;

        canvasPainter.splashRadius = Slider("حجم بقعة القطرة (Splash Radius)", canvasPainter.splashRadius, 0.001f, 0.1f, "F3");
        canvasPainter.paintOpacity = Slider("شفافية الدهان (Paint Opacity)", canvasPainter.paintOpacity, 0f, 1f);
        canvasPainter.velocityStretch = Slider("إطالة المسار بالسرعة (Velocity Stretch)", canvasPainter.velocityStretch, 0f, 10f);
        canvasPainter.dryTime = Slider("زمن الجفاف (Dry Time)", canvasPainter.dryTime, 0.5f, 30f);
        canvasPainter.wetBuildup = Slider("تغميق الدهان الرطب (Wet Buildup)", canvasPainter.wetBuildup, 0f, 1f);
        canvasPainter.wetDiffusion = Slider("انتشار الدهان الرطب (Wet Diffusion)", canvasPainter.wetDiffusion, 0f, 0.5f);
        canvasPainter.enablePooling = Toggle("تفعيل تجمّع البركة (Pooling)", canvasPainter.enablePooling);
        if (canvasPainter.enablePooling)
        {
            canvasPainter.poolAddPerDrop = Slider("إضافة كل قطرة للبركة", canvasPainter.poolAddPerDrop, 0f, 2f);
            canvasPainter.poolSaturation = Slider("حد التشبّع (Saturation)", canvasPainter.poolSaturation, 0.1f, 3f);
            canvasPainter.poolSpread = Slider("قوة الفيض (Spread)", canvasPainter.poolSpread, 0f, 1f);
        }

        int newRes = IntSlider("دقة تكستشر اللوحة (Rebuild)", canvasPainter.textureResolution, 128, 2048);
        if (newRes != canvasPainter.textureResolution) { canvasPainter.textureResolution = newRes; rebuildNeeded = true; }
    }

    // ---------------------------------------------------------------------
    // زر إعادة البناء
    // ---------------------------------------------------------------------
    void DrawRebuildButton()
    {
        if (fluidSim == null) return;

        if (rebuildNeeded)
            GUILayout.Label("⚠ عدّلت قيمة تحتاج إعادة بناء المحاكاة لتظهر — اضغط الزر تحت.");

        GUIStyle style = rebuildNeeded ? warnStyle : GUI.skin.button;
        string label = rebuildNeeded
            ? "⚠ إعادة بناء المحاكاة الآن (Rebuild Fluid Simulation)"
            : "↻ إعادة بناء المحاكاة (Rebuild Fluid Simulation)";

        if (GUILayout.Button(label, style, GUILayout.Height(32)))
        {
            fluidSim.RebuildSimulation();
            if (fluidRenderer != null) fluidRenderer.RefreshArgs();
            rebuildNeeded = false;
        }
    }

    // ---------------------------------------------------------------------
    // Helpers صغيرة لتبسيط بناء الواجهة
    // ---------------------------------------------------------------------
    bool Foldout(string label, bool state)
    {
        if (GUILayout.Button((state ? "▾ " : "▸ ") + label, headerStyle))
            state = !state;
        return state;
    }

    float Slider(string label, float value, float min, float max, string format = "F2")
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(190));
        float newValue = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(120));
        GUILayout.Label(newValue.ToString(format), GUILayout.Width(55));
        GUILayout.EndHorizontal();
        return newValue;
    }

    int IntSlider(string label, int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(190));
        float newValue = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(120));
        int rounded = Mathf.RoundToInt(newValue);
        GUILayout.Label(rounded.ToString(), GUILayout.Width(55));
        GUILayout.EndHorizontal();
        return rounded;
    }

    bool Toggle(string label, bool value)
    {
        return GUILayout.Toggle(value, " " + label);
    }

    Color ColorSliders(string label, Color c)
    {
        GUILayout.Label(label);
        c.r = Slider("  R", c.r, 0f, 1f);
        c.g = Slider("  G", c.g, 0f, 1f);
        c.b = Slider("  B", c.b, 0f, 1f);
        return c;
    }

    System.Enum EnumToolbar(string label, System.Enum selected)
    {
        GUILayout.Label(label);
        string[] names = System.Enum.GetNames(selected.GetType());
        int current = System.Array.IndexOf(names, selected.ToString());
        int chosen = GUILayout.Toolbar(current, names);
        return (System.Enum)System.Enum.Parse(selected.GetType(), names[chosen]);
    }
}
