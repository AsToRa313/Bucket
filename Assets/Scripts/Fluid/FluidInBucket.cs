using UnityEngine;
using Unity.Mathematics;

// هاد الملف بربط السائل بالسطل المتحرك
public class FluidInBucket : MonoBehaviour
{
    [Header("=== المراجع ===")]
    public BucketPendulum bucketPendulum;
    public Seb.Fluid.Simulation.FluidSim fluidSim;
    public Seb.Fluid.Simulation.Spawner3D spawner;

    [Header("=== حجم السطل ===")]
    public Vector3 bucketSize = new Vector3(0.8f, 1f, 0.8f);

    [Header("=== إعدادات التسرب ===")]
    public bool enableDrip = true;
    public float holeRadius = 0.05f;
    [Range(0f, 1f)]
    public float paintViscosity = 0.5f; // لزوجة الدهان

    [Header("=== الدهان ===")]
    public Color paintColor = Color.red;

    // حدث عند خروج دهان
    public event System.Action<Vector3, Vector3, Color> OnPaintDrip;

    private Vector3 previousBucketPos;
    private float dripTimer = 0f;
    private float dripInterval => paintViscosity * 0.1f + 0.02f;

    void Start()
    {
        previousBucketPos = bucketPendulum.GetBucketPosition();
    }

    void Update()
    {
        // حرّك حدود السائل مع السطل
        SyncFluidWithBucket();

        // تحقق إذا بدنا نرش دهان
        if (enableDrip && bucketPendulum.isSimulating)
        {
            HandlePaintDrip();
        }

        previousBucketPos = bucketPendulum.GetBucketPosition();
    }

    void SyncFluidWithBucket()
    {
        // حرّك الـ FluidSim مع السطل
        Vector3 bucketPos = bucketPendulum.GetBucketPosition();
        fluidSim.transform.position = bucketPos;
        fluidSim.transform.localScale = bucketSize;

        // حوّل الجاذبية حسب زاوية السطل
        // لما السطل مائل، الجاذبية النسبية تتغير
        float angle = bucketPendulum.currentAngleDegrees * Mathf.Deg2Rad;
        float effectiveGravity = -9.81f * Mathf.Cos(angle);
        fluidSim.gravity = effectiveGravity;
    }

    void HandlePaintDrip()
    {
        dripTimer += Time.deltaTime;

        if (dripTimer >= dripInterval)
        {
            dripTimer = 0f;

            // احسب موقع الفتحة (أسفل السطل)
            Vector3 bucketPos = bucketPendulum.GetBucketPosition();
            Vector3 holePos = bucketPos + Vector3.down * (bucketSize.y * 0.5f);

            // سرعة الدهان = سرعة السطل + الجاذبية
            float bucketSpeed = bucketPendulum.GetBucketSpeed();
            Vector3 dripVelocity = new Vector3(
                bucketSpeed * Mathf.Sign(bucketPendulum.currentAngleDegrees),
                -2f, // للأسفل
                0f
            );

            // أطلق الحدث
            OnPaintDrip?.Invoke(holePos, dripVelocity, paintColor);
        }
    }
}
