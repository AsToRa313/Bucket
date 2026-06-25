using UnityEngine;

public class SphericalPendulumMath : MonoBehaviour
{
    public enum BucketShape { Cylindrical, Conical, Cubic }

    [Header("Data Architecture")]
    public PendulumData data;

    [Header("Mathematical Parameters")]
    public Transform pivot;
    public float baseLength = 5f;
    public float gravity = 9.81f;
    public float airDamping = 0.001f;
    public float pivotFriction = 0.05f;

    [Header("Bucket Geometry")]
    public BucketShape shape = BucketShape.Cylindrical;
    public float bucketHeight = 0.4f;

    [Header("Variable Mass Dynamics")]
    public float emptyBucketMass = 0.5f;
    public float maxPaintMass = 2.0f;
    public float currentPaintMass = 2.0f;
    public float drainRate = 0.05f;

    [Header("Torsional Twist Settings")]
    public float torsionalStiffness = 0.8f;
    public float torsionalDamping = 0.05f;
    public float bucketRadius = 0.2f;

    [Header("Rope Visual Settings")]
    public int ropeSegments = 15;
    public float sagFactor = 0.6f;

    // ---- حالة داخلية ----
    private float twistAngle = 0f;
    private float twistVelocity = 0f;
    private Vector3 velocity = Vector3.zero;
    private float effectiveLength = 5f;
    private bool isDragging = false;
    private float dragDepth;
    private float theta = 0f;
    private float phi = 0f;
    private float thetaVelocity = 0f;
    private float phiVelocity = 0f;
    private float lastTheta = 0f;
    private float lastPhi = 0f;
    private Vector3 lastDragPosition;
    private LineRenderer ropeRenderer;

    // *** الدالة اللي بتحتاجها SPHSimulation ***
    public Vector3 GetBucketPosition() => transform.position;
    public float   GetBucketSpeed()    => velocity.magnitude;

    // للتوافق مع SimulationManager القديم
    public bool isSimulating => !isDragging && velocity.magnitude > 0.01f;

    void Start()
    {
        ropeRenderer = GetComponent<LineRenderer>();
        if (ropeRenderer == null)
            ropeRenderer = gameObject.AddComponent<LineRenderer>();

        SetupRopeRenderer();

        if (pivot != null)
        {
            baseLength = Vector3.Distance(transform.position, pivot.position);
            UpdateMassAndCOM();
            SyncAnglesFromPosition();
        }

        // ابدأ بدفعة خفيفة عشان يتحرك من البداية
        velocity = new Vector3(0.5f, 0f, 0f);
    }

    void SetupRopeRenderer()
    {
        ropeRenderer.material = new Material(Shader.Find("Sprites/Default"));
        ropeRenderer.startColor = Color.white;
        ropeRenderer.endColor   = Color.white;
        ropeRenderer.startWidth = 0.04f;
        ropeRenderer.endWidth   = 0.02f;
        ropeRenderer.positionCount = ropeSegments;
    }

    void FixedUpdate()
    {
        if (isDragging || pivot == null) return;

        UpdateMassAndCOM();

        float totalMass      = emptyBucketMass + currentPaintMass;
        float massDerivative = (currentPaintMass > 0) ? drainRate : 0f;

        float dragCoefficient = 1.0f;
        if      (shape == BucketShape.Cylindrical) dragCoefficient = 0.82f;
        else if (shape == BucketShape.Cubic)       dragCoefficient = 1.05f;
        else if (shape == BucketShape.Conical)     dragCoefficient = 0.50f;

        float totalDamping = ((airDamping * dragCoefficient) + massDerivative)
                             / totalMass + pivotFriction;

        // جاذبية + تخامد
        velocity += Vector3.down * gravity * Time.fixedDeltaTime;
        velocity -= velocity * totalDamping * Time.fixedDeltaTime;

        Vector3 nextPosition  = transform.position + velocity * Time.fixedDeltaTime;
        Vector3 offset        = nextPosition - pivot.position;
        float   currentDistance = offset.magnitude;

        if (currentDistance >= effectiveLength)
        {
            Vector3 ropeDirection  = offset.normalized;
            transform.position     = pivot.position + ropeDirection * effectiveLength;
            float radialVelocity   = Vector3.Dot(velocity, ropeDirection);
            if (radialVelocity > 0f)
                velocity -= ropeDirection * radialVelocity;
        }
        else
        {
            transform.position = nextPosition;
        }

        SyncAnglesFromPosition();

        // فيزياء الفتل
        float I_y              = 0.5f * totalMass * (bucketRadius * bucketRadius);
        float torsionalTorque  = (-torsionalStiffness * twistAngle)
                                 - (torsionalDamping * twistVelocity);
        float twistAcceleration = torsionalTorque / I_y;
        twistVelocity  += twistAcceleration * Time.fixedDeltaTime;
        twistAngle     += twistVelocity     * Time.fixedDeltaTime;

        // شرط النوم
        if (Mathf.Abs(theta) < 0.02f
            && velocity.magnitude < 0.05f
            && Mathf.Abs(twistVelocity) < 0.05f)
        {
            transform.position = pivot.position + Vector3.down * effectiveLength;
            velocity           = Vector3.zero;
            theta              = 0f;
            thetaVelocity      = 0f;
            phiVelocity        = 0f;
            twistAngle         = 0f;
            twistVelocity      = 0f;
        }

        UpdateRopeRenderer();
        UpdateSharedData();
    }

    void SyncAnglesFromPosition()
    {
        Vector3 localPos = transform.position - pivot.position;
        lastTheta = theta;
        lastPhi   = phi;

        theta = Mathf.Acos(Mathf.Clamp(-localPos.normalized.y, -1f, 1f));
        phi   = Mathf.Atan2(localPos.z, localPos.x);

        if (Time.fixedDeltaTime > 0)
        {
            thetaVelocity = (theta - lastTheta) / Time.fixedDeltaTime;
            phiVelocity   = Mathf.DeltaAngle(lastPhi * Mathf.Rad2Deg,
                                              phi    * Mathf.Rad2Deg)
                            * Mathf.Deg2Rad / Time.fixedDeltaTime;
        }
    }

    void OnMouseDown()
    {
        isDragging       = true;
        velocity         = Vector3.zero;
        dragDepth        = Camera.main.WorldToScreenPoint(transform.position).z;
        lastDragPosition = transform.position;
    }

    void OnMouseDrag()
    {
        if (pivot == null) return;
        dragDepth += Input.GetAxis("Mouse ScrollWheel") * 5f;

        Vector3 mouseScreenPoint = new Vector3(
            Input.mousePosition.x, Input.mousePosition.y, dragDepth);
        Vector3 mouseWorldPoint = Camera.main.ScreenToWorldPoint(mouseScreenPoint);

        Vector3 offset      = (mouseWorldPoint - pivot.position).normalized * effectiveLength;
        Vector3 newPosition = pivot.position + offset;

        if (Time.deltaTime > 0)
            velocity = (newPosition - lastDragPosition) / Time.deltaTime;

        transform.position = newPosition;
        lastDragPosition   = newPosition;

        SyncAnglesFromPosition();
        UpdateRopeRenderer();
    }

    void OnMouseUp() { isDragging = false; }

    void UpdateMassAndCOM()
    {
        if (currentPaintMass > 0)
        {
            currentPaintMass -= drainRate * Time.fixedDeltaTime;
            if (currentPaintMass < 0) currentPaintMass = 0;
        }

        float fillRatio = maxPaintMass > 0 ? currentPaintMass / maxPaintMass : 0;
        float z_cm = 0f;

        switch (shape)
        {
            case BucketShape.Conical:
                z_cm = (3f / 4f) * bucketHeight * Mathf.Pow(fillRatio, 1f / 3f);
                break;
            default:
                z_cm = (bucketHeight * fillRatio) / 2f;
                break;
        }

        effectiveLength = baseLength + (bucketHeight - z_cm);
    }

    void UpdateRopeRenderer()
    {
        if (pivot == null || ropeRenderer == null) return;

        Vector3 p0             = pivot.position;
        Vector3 comPosition    = transform.position;
        float   offsetToHandle = effectiveLength - baseLength;

        Vector3 directionToPivot  = (p0 - comPosition).normalized;
        Vector3 straightHandlePos = comPosition + directionToPivot * offsetToHandle;

        Vector3 p1            = (p0 + straightHandlePos) * 0.5f;
        float   currentDist   = (straightHandlePos - p0).magnitude;

        if (currentDist < baseLength && !isDragging)
        {
            float slack = baseLength - currentDist;
            p1 += Vector3.down * slack * sagFactor;
        }

        Vector3 ropeDirectionAtBucket = (p1 - comPosition).normalized;
        if (ropeDirectionAtBucket.magnitude > 0.01f)
        {
            Quaternion baseRotation  = Quaternion.FromToRotation(Vector3.up,
                                                                  ropeDirectionAtBucket);
            Quaternion twistRotation = Quaternion.AngleAxis(
                                           twistAngle * Mathf.Rad2Deg, Vector3.up);
            transform.rotation = baseRotation * twistRotation;
        }

        Vector3 actualHandlePos = transform.position
                                  + transform.up * offsetToHandle;

        ropeRenderer.positionCount = ropeSegments;
        for (int i = 0; i < ropeSegments; i++)
        {
            float   t     = i / (float)(ropeSegments - 1);
            Vector3 point = Mathf.Pow(1f - t, 2) * p0
                          + 2f * (1f - t) * t * p1
                          + Mathf.Pow(t, 2) * actualHandlePos;
            ropeRenderer.SetPosition(i, point);
        }

        if (ropeRenderer.material != null)
        {
            ropeRenderer.material.mainTextureOffset =
                new Vector2(twistAngle * -0.5f, 0);
        }
    }

    void UpdateSharedData()
    {
        if (data == null) return;
        data.totalMass            = emptyBucketMass + currentPaintMass;
        data.currentPaintMass     = currentPaintMass;
        data.effectiveLength      = effectiveLength;
        data.fillRatio            = maxPaintMass > 0
                                    ? currentPaintMass / maxPaintMass : 0;
        data.theta                = theta;
        data.phi                  = phi;
        data.angularVelocityTheta = thetaVelocity;
        data.angularVelocityPhi   = phiVelocity;
    }

    void OnDrawGizmos()
    {
        if (pivot == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pivot.position, transform.position);
        Gizmos.color = new Color(0, 1, 1, 0.3f);
        Gizmos.DrawWireSphere(transform.position, 0.3f);
    }
}