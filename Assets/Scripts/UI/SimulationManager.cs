using UnityEngine;

// المدير العام - يتحكم بكل شي
public class SimulationManager : MonoBehaviour
{
    [Header("=== المراجع ===")]
    public SphericalPendulumMath pendulum;
    public FluidInBucket fluidInBucket;
    public CanvasPainter canvasPainter;

    [Header("=== إعدادات الـ Prefab ===")]
    public GameObject paintDripPrefab;

    void Start()
    {
        // اشترك بحدث الدهان
        if (fluidInBucket != null)
        {
            fluidInBucket.OnPaintDrip += SpawnPaintDrip;
        }
    }

    void SpawnPaintDrip(Vector3 position, Vector3 velocity, Color color)
    {
        if (paintDripPrefab == null) return;

        GameObject drip = Instantiate(paintDripPrefab, position, Quaternion.identity);
        var paintDrip = drip.GetComponent<PaintDrip>();
        if (paintDrip != null)
        {
            paintDrip.Initialize(velocity, color, canvasPainter);
        }
    }

    // أزرار الواجهة
    void OnGUI()
{
    GUILayout.BeginArea(new Rect(10, 10, 220, 300));
    GUILayout.Label("=== التحكم ===");

    if (GUILayout.Button("▶ ابدأ / استمر"))
        pendulum.enabled = true;

    if (GUILayout.Button("⏸ وقّف"))
        pendulum.enabled = false;

    if (GUILayout.Button("💾 احفظ اللوحة"))
        canvasPainter.SaveCanvas("my_painting");

    if (GUILayout.Button("🔄 امسح اللوحة"))
        canvasPainter.ClearCanvas();

    GUILayout.Space(10);
    if (pendulum.data != null)
    {
        GUILayout.Label($"كتلة الدهان: {pendulum.data.currentPaintMass:F2}");
        GUILayout.Label($"theta: {pendulum.data.theta * Mathf.Rad2Deg:F1}°");
        GUILayout.Label($"سرعة: {pendulum.GetBucketSpeed():F2} m/s");
    }

    GUILayout.EndArea();
}

    void OnDestroy()
    {
        if (fluidInBucket != null)
        {
            fluidInBucket.OnPaintDrip -= SpawnPaintDrip;
        }
    }
}