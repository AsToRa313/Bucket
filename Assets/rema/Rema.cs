using UnityEngine;

public class Rema : MonoBehaviour
{
    [Header("Data Architecture")]
    public PendulumData sharedData;

    [Header("Fluid Properties")]
    public float paintDensity = 1200f;      // kg/m³
    public float surfaceTension = 0.035f;   // N/m
    public float contactAngleDeg = 45f;

    [Header("Hole Geometry")]
    public float holeRadius = 0.005f;       // 5 mm

    [Range(0f, 1f)]
    public float Cv = 0.97f;                // Coefficient of Velocity

    [Range(0f, 1f)]
    public float Cc = 0.62f;                // Coefficient of Contraction

    [Header("Bucket Information")]
    public float maxPaintMass = 2f;

    [Header("Clogging Settings")]
    public float dryingRate = 0.00001f;
    public float minimumHoleRadius = 0.0001f;

    [Header("Outputs")]
    public float effectiveHoleRadius;

    public float actualFlowVelocity;
    public float currentDrainRate;

    private void Start()
    {
        effectiveHoleRadius = holeRadius;
    }

    public void CalculateFluidPhysics(
        SphericalPendulumMath.BucketShape shape,
        float bucketHeight,
        float currentPaintMass)
    {
        //--------------------------------------------------
        // Safety Checks
        //--------------------------------------------------

        if (sharedData == null || currentPaintMass <= 0f)
        {
            actualFlowVelocity = 0f;
            currentDrainRate = 0f;
            return;
        }

        //--------------------------------------------------
        // 1. Calculate Fluid Height (h)
        //--------------------------------------------------

        float h = 0f;

        switch (shape)
        {
            case SphericalPendulumMath.BucketShape.Cylindrical:

                h = bucketHeight * sharedData.fillRatio;
                break;

            case SphericalPendulumMath.BucketShape.Conical:

                h = bucketHeight *
                    Mathf.Pow(sharedData.fillRatio, 1f / 3f);
                break;

            case SphericalPendulumMath.BucketShape.Cubic:

                h = bucketHeight * sharedData.fillRatio;
                break;
        }

        //--------------------------------------------------
        // 2. Effective Gravity
        //--------------------------------------------------

        float gEff;

        if (sharedData.effectiveGravity > 0f)
        {
            gEff = sharedData.effectiveGravity;
        }
        else
        {
            float speed = sharedData.linearVelocity.magnitude;

            gEff =
                9.81f +
                (speed * speed) /
                Mathf.Max(sharedData.effectiveLength, 0.1f);
        }

        //--------------------------------------------------
        // 3. Hydrodynamic Pressure
        //--------------------------------------------------

        float hydrodynamicPressure =
            paintDensity *
            gEff *
            h;

        //--------------------------------------------------
        // 4. Young-Laplace Capillary Pressure
        //--------------------------------------------------

        float thetaRad =
            contactAngleDeg * Mathf.Deg2Rad;

        float capillaryPressure =
            (2f *
             surfaceTension *
             Mathf.Cos(thetaRad))
             /
             Mathf.Max(effectiveHoleRadius, minimumHoleRadius);

        //--------------------------------------------------
        // 5. Clogging Simulation
        //--------------------------------------------------

        if (hydrodynamicPressure < capillaryPressure * 1.5f)
        {
            effectiveHoleRadius -=
                dryingRate * Time.deltaTime;
        }
        else
        {
            effectiveHoleRadius +=
                dryingRate * 0.2f * Time.deltaTime;
        }

        effectiveHoleRadius =
            Mathf.Clamp(
                effectiveHoleRadius,
                minimumHoleRadius,
                holeRadius
            );

        //--------------------------------------------------
        // 6. Check Flow Blockage
        //--------------------------------------------------

        if (hydrodynamicPressure <= capillaryPressure)
        {
            actualFlowVelocity = 0f;
            currentDrainRate = 0f;

            sharedData.fluidExitVelocity = 0f;
            sharedData.dynamicDrainRate = 0f;
            sharedData.currentHoleRadius = effectiveHoleRadius;

            return;
        }

        //--------------------------------------------------
        // 7. Torricelli Equation
        //--------------------------------------------------

        float idealVelocity =
            Mathf.Sqrt(2f * gEff * h);

        actualFlowVelocity =
            Cv * idealVelocity;

        //--------------------------------------------------
        // 8. Flow Rate
        //--------------------------------------------------

        float holeArea =
            Mathf.PI *
            effectiveHoleRadius *
            effectiveHoleRadius;

        currentDrainRate =
            Cc *
            holeArea *
            actualFlowVelocity *
            paintDensity;

        //--------------------------------------------------
        // 9. Update Shared Data
        //--------------------------------------------------

        sharedData.fluidExitVelocity =
            actualFlowVelocity;

        sharedData.dynamicDrainRate =
            currentDrainRate;

        sharedData.currentHoleRadius =
            effectiveHoleRadius;
    }
}