using UnityEngine;

public class SimulationControlPanel : MonoBehaviour
{
    [Header("=== References (Leave empty for auto-find) ===")]
    public SphericalPendulumMath pendulum;
    public SPHSimulation1 fluidSim;
    public SPHRenderer fluidRenderer;
    public CanvasPainter canvasPainter;

    [Header("=== Panel Settings ===")]
    public KeyCode toggleKey = KeyCode.F1;
    public bool startVisible = true;

    [Range(0.5f, 3f)]
    public float uiScale = 1.0f; // غيرها من الـ Inspector لتكبير/تصغير الواجهة

    bool visible;
    Vector2 scroll;
    Rect panelRect = new Rect(0, 0, 500, 0);

    // Foldout states
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

    float pendingTheta;
    float pendingPhi;
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

    void SetupStyles()
    {
        if (stylesReady) return;

        headerStyle = new GUIStyle(GUI.skin.button);
        headerStyle.alignment = TextAnchor.MiddleLeft;
        headerStyle.fontStyle = FontStyle.Bold;
        headerStyle.fontSize = 14;

        warnStyle = new GUIStyle(GUI.skin.button);
        warnStyle.normal.textColor = new Color(1f, 0.55f, 0.1f);
        warnStyle.fontStyle = FontStyle.Bold;

        stylesReady = true;
    }

    void OnGUI()
    {
        SetupStyles();

        // Apply global scale
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(uiScale, uiScale, 1f));

        float scaledScreenWidth = Screen.width / uiScale;
        float scaledScreenHeight = Screen.height / uiScale;

        // Button positioned at the top right
        if (GUI.Button(new Rect(scaledScreenWidth - 160, 10, 150, 26), visible ? "✕ Close Panel" : "☰ Open Control Panel"))
            visible = !visible;

        if (!visible) return;

        // Panel positioned on the right
        panelRect.width = 520;
        panelRect.x = scaledScreenWidth - panelRect.width - 10;
        panelRect.y = 45;
        panelRect.height = Mathf.Min(scaledScreenHeight - 55, 800);

        GUILayout.BeginArea(panelRect, GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label("Simulation Control Panel", headerStyle);
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

    void DrawLiveReadouts()
    {
        GUILayout.Box("", GUILayout.Height(1));
        GUILayout.Label("📊 Live Readouts");
        if (pendulum != null)
        {
            GUILayout.Label($"  Bucket Speed: {pendulum.GetBucketSpeed():F2} m/s   |   Fill: {pendulum.GetFillRatio() * 100f:F0}%");
            GUILayout.Label($"  Theta: {pendulum.GetThetaDegrees():F1}°   |   Phi: {pendulum.GetPhiDegrees():F1}°");
        }
        if (fluidSim != null)
        {
            GUILayout.Label($"  Current Particles: {fluidSim.GetParticleCount()}");
        }
    }

    void DrawPendulumSection()
    {
        GUILayout.Label("🔗 Pendulum", headerStyle);

        secPendulumMotion = Foldout("Base Motion", secPendulumMotion);
        if (secPendulumMotion)
        {
            pendulum.baseLength = Slider("Base Length", pendulum.baseLength, 0.5f, 15f);
            pendulum.gravity = Slider("Gravity", pendulum.gravity, 0f, 30f);
            pendulum.airDamping = Slider("Air Damping", pendulum.airDamping, 0f, 0.05f, "F4");
            pendulum.pivotFriction = Slider("Pivot Friction", pendulum.pivotFriction, 0f, 1f);
        }

        secPendulumGeometry = Foldout("Bucket Geometry", secPendulumGeometry);
        if (secPendulumGeometry)
        {
            pendulum.shape = (SphericalPendulumMath.BucketShape)EnumToolbar("Shape", pendulum.shape);
            pendulum.bucketHeight = Slider("Bucket Height", pendulum.bucketHeight, 0.05f, 3f);
            pendulum.bucketRadius = Slider("Bucket Radius", pendulum.bucketRadius, 0.02f, 2f);
        }

        secPendulumMass = Foldout("Paint Mass", secPendulumMass);
        if (secPendulumMass)
        {
            pendulum.emptyBucketMass = Slider("Empty Bucket Mass", pendulum.emptyBucketMass, 0.05f, 5f);
            pendulum.maxPaintMass = Slider("Max Paint Mass", pendulum.maxPaintMass, 0.1f, 10f);
            pendulum.currentPaintMass = Slider("Current Paint Mass", pendulum.currentPaintMass, 0f, pendulum.maxPaintMass);
            pendulum.drainRate = Slider("Drain Rate", pendulum.drainRate, 0f, 1f);
        }

        secPendulumRope = Foldout("Rope & Torsion", secPendulumRope);
        if (secPendulumRope)
        {
            pendulum.torsionalStiffness = Slider("Torsional Stiffness", pendulum.torsionalStiffness, 0f, 5f);
            pendulum.torsionalDamping = Slider("Torsional Damping", pendulum.torsionalDamping, 0f, 1f);
            pendulum.ropeSegments = IntSlider("Rope Segments", pendulum.ropeSegments, 2, 60);
            pendulum.sagFactor = Slider("Sag Factor", pendulum.sagFactor, 0f, 2f);
        }

        secPendulumAngle = Foldout("⚡ Swing Angle (Theta / Phi)", secPendulumAngle);
        if (secPendulumAngle)
        {
            GUILayout.Label("Set initial swing angle manually:");
            pendingTheta = Slider("Theta (° from bottom)", pendingTheta, 0f, 90f);
            pendingPhi = Slider("Phi (° around axis)", pendingPhi, -180f, 180f);
            if (GUILayout.Button("↺ Apply Angle"))
                pendulum.SetSphericalAngles(pendingTheta, pendingPhi);
        }
    }

    void DrawFluidSection()
    {
        GUILayout.Label("💧 Fluid (SPH)", headerStyle);

        secFluidSolver = Foldout("Particles & Solver", secFluidSolver);
        if (secFluidSolver)
        {
            int newCount = IntSlider("Particle Count (Rebuild)", fluidSim.numParticles, 100, 100000);
            if (newCount != fluidSim.numParticles) { fluidSim.numParticles = newCount; rebuildNeeded = true; }

            float newSmoothing = Slider("Smoothing Radius (Rebuild)", fluidSim.smoothingRadius, 0.02f, 0.3f, "F3");
            if (!Mathf.Approximately(newSmoothing, fluidSim.smoothingRadius)) { fluidSim.smoothingRadius = newSmoothing; rebuildNeeded = true; }

            fluidSim.iterations = IntSlider("Iterations", fluidSim.iterations, 1, 8);
            fluidSim.stiffness = Slider("Stiffness", fluidSim.stiffness, 0f, 5f);
            fluidSim.nearStiffness = Slider("Near Stiffness", fluidSim.nearStiffness, 0f, 8f);
            fluidSim.velocityDamping = Slider("Velocity Damping", fluidSim.velocityDamping, 0.9f, 1f, "F3");
            fluidSim.gravity = Slider("Gravity", fluidSim.gravity, 0f, 30f);
            fluidSim.maxInertiaAccel = Slider("Max Inertia Accel", fluidSim.maxInertiaAccel, 0f, 10f);
            fluidSim.collisionDamping = Slider("Collision Damping", fluidSim.collisionDamping, 0f, 1f);
            fluidSim.wallRestitution = Slider("Wall Restitution", fluidSim.wallRestitution, 0f, 0.8f);
            fluidSim.autoRestDensity = Toggle("Auto Rest Density", fluidSim.autoRestDensity);
            if (!fluidSim.autoRestDensity)
                fluidSim.restDensity = Slider("Rest Density", fluidSim.restDensity, 0.1f, 50f);
        }

        secFluidBucket = Foldout("Bucket Dimensions & Fill", secFluidBucket);
        if (secFluidBucket)
        {
            float r = Slider("Bucket Radius (Rebuild)", fluidSim.bucketRadius, 0.02f, 2f);
            if (!Mathf.Approximately(r, fluidSim.bucketRadius)) { fluidSim.bucketRadius = r; rebuildNeeded = true; }

            float h = Slider("Bucket Height (Rebuild)", fluidSim.bucketHeight, 0.05f, 3f);
            if (!Mathf.Approximately(h, fluidSim.bucketHeight)) { fluidSim.bucketHeight = h; rebuildNeeded = true; }

            float fill = Slider("Initial Fill Ratio (Rebuild)", fluidSim.initialFillRatio, 0f, 1f);
            if (!Mathf.Approximately(fill, fluidSim.initialFillRatio)) { fluidSim.initialFillRatio = fill; rebuildNeeded = true; }
        }

        secFluidVortex = Foldout("Vortex & Drain", secFluidVortex);
        if (secFluidVortex)
        {
            fluidSim.enableVortex = Toggle("Enable Vortex", fluidSim.enableVortex);
            if (fluidSim.enableVortex)
            {
                fluidSim.vortexRange = Slider("Vortex Range", fluidSim.vortexRange, 0f, 15f);
                fluidSim.vortexPull = Slider("Vortex Pull", fluidSim.vortexPull, 0f, 10f);
                fluidSim.vortexSpin = Slider("Vortex Spin", fluidSim.vortexSpin, 0f, 10f);
            }
        }

        secFluidDroplets = Foldout("Two Droplets Demo", secFluidDroplets);
        if (secFluidDroplets)
        {
            bool two = Toggle("Enable Two Droplets (Rebuild)", fluidSim.twoDropletsMode);
            if (two != fluidSim.twoDropletsMode) { fluidSim.twoDropletsMode = two; rebuildNeeded = true; }

            if (fluidSim.twoDropletsMode)
            {
                float dr = Slider("Droplet Radius (Rebuild)", fluidSim.dropletRadius, 0.05f, 1f);
                if (!Mathf.Approximately(dr, fluidSim.dropletRadius)) { fluidSim.dropletRadius = dr; rebuildNeeded = true; }

                float ds = Slider("Droplet Separation (Rebuild)", fluidSim.dropletSeparation, 0f, 3f);
                if (!Mathf.Approximately(ds, fluidSim.dropletSeparation)) { fluidSim.dropletSeparation = ds; rebuildNeeded = true; }

                float dh = Slider("Droplet Height (Rebuild)", fluidSim.dropletHeight, 0f, 5f);
                if (!Mathf.Approximately(dh, fluidSim.dropletHeight)) { fluidSim.dropletHeight = dh; rebuildNeeded = true; }

                fluidSim.dropletApproachSpeed = Slider("Approach Speed", fluidSim.dropletApproachSpeed, 0f, 5f);
                fluidSim.dropletColor1 = ColorSliders("Droplet 1 Color", fluidSim.dropletColor1);
                fluidSim.dropletColor2 = ColorSliders("Droplet 2 Color", fluidSim.dropletColor2);
            }
        }

        secFluidRender = Foldout("Paint Color & Rendering", secFluidRender);
        if (secFluidRender)
        {
            fluidSim.useFixedColors = Toggle("Color Diagnostic Mode (Rainbow)", fluidSim.useFixedColors);

            Color newPaint = ColorSliders("Current Paint Color", fluidSim.paintColor);
            if (newPaint != fluidSim.paintColor)
                fluidSim.SetPaintColor(newPaint);

            if (fluidRenderer != null)
            {
                fluidRenderer.particleSize = Slider("Particle Size", fluidRenderer.particleSize, 0.01f, 0.2f);
                fluidRenderer.speedScale = Slider("Color Speed Scale", fluidRenderer.speedScale, 0.1f, 10f);
                fluidRenderer.useUnifiedColor = Toggle("Use Unified Paint Color", fluidRenderer.useUnifiedColor);
                if (!fluidRenderer.useUnifiedColor)
                {
                    fluidRenderer.colorSlow = ColorSliders("Slow Color", fluidRenderer.colorSlow);
                    fluidRenderer.colorFast = ColorSliders("Fast Color", fluidRenderer.colorFast);
                }
            }
        }
    }

    void DrawCanvasSection()
    {
        GUILayout.Label("🎨 Canvas Painting", headerStyle);
        secCanvas = Foldout("Painting Settings", secCanvas);
        if (!secCanvas) return;

        canvasPainter.splashRadius = Slider("Splash Radius", canvasPainter.splashRadius, 0.001f, 0.1f, "F3");
        canvasPainter.paintOpacity = Slider("Paint Opacity", canvasPainter.paintOpacity, 0f, 1f);
        canvasPainter.velocityStretch = Slider("Velocity Stretch", canvasPainter.velocityStretch, 0f, 10f);
        canvasPainter.dryTime = Slider("Dry Time", canvasPainter.dryTime, 0.5f, 30f);
        canvasPainter.wetBuildup = Slider("Wet Buildup", canvasPainter.wetBuildup, 0f, 1f);
        canvasPainter.wetDiffusion = Slider("Wet Diffusion", canvasPainter.wetDiffusion, 0f, 0.5f);
        canvasPainter.enablePooling = Toggle("Enable Pooling", canvasPainter.enablePooling);
        if (canvasPainter.enablePooling)
        {
            canvasPainter.poolAddPerDrop = Slider("Pool Add Per Drop", canvasPainter.poolAddPerDrop, 0f, 2f);
            canvasPainter.poolSaturation = Slider("Pool Saturation", canvasPainter.poolSaturation, 0.1f, 3f);
            canvasPainter.poolSpread = Slider("Pool Spread", canvasPainter.poolSpread, 0f, 1f);
        }

        int newRes = IntSlider("Canvas Texture Res (Rebuild)", canvasPainter.textureResolution, 128, 2048);
        if (newRes != canvasPainter.textureResolution) { canvasPainter.textureResolution = newRes; rebuildNeeded = true; }
    }

    void DrawRebuildButton()
    {
        if (fluidSim == null) return;

        if (rebuildNeeded)
            GUILayout.Label("⚠ Rebuild required to apply changes — Click below.");

        GUIStyle style = rebuildNeeded ? warnStyle : GUI.skin.button;
        string label = rebuildNeeded
            ? "⚠ Rebuild Fluid Simulation Now"
            : "↻ Rebuild Fluid Simulation";

        if (GUILayout.Button(label, style, GUILayout.Height(32)))
        {
            fluidSim.RebuildSimulation();
            if (fluidRenderer != null) fluidRenderer.RefreshArgs();
            rebuildNeeded = false;
        }
    }

    // Helpers
    bool Foldout(string label, bool state)
    {
        if (GUILayout.Button((state ? "▾ " : "▸ ") + label, headerStyle))
            state = !state;
        return state;
    }

    float Slider(string label, float value, float min, float max, string format = "F2")
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(210)); // زدت العرض شوي ليناسب الإنجليزي
        float newValue = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(120));
        GUILayout.Label(newValue.ToString(format), GUILayout.Width(55));
        GUILayout.EndHorizontal();
        return newValue;
    }

    int IntSlider(string label, int value, int min, int max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(210));
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