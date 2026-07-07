using UnityEngine;

public class SphericalPendulumMath : MonoBehaviour, IBucketPhysics
{
    // أبقينا فقط الشكل الأسطواني لتنظيف الواجهة تماماً
    public enum BucketShape { Cylindrical }

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

    [Header("Rope Physics (Elasticity)")]
    public bool isElastic = false;
    public float springConstant = 150f;
    public float springDamping = 5f;
    public float maxElasticLength = 7f;

    [Header("Torsional Twist Settings")]
    public float torsionalStiffness = 0.8f;
    public float torsionalDamping = 0.05f;
    public float bucketRadius = 0.2f;

    [Header("Rope Visual Settings")]
    public int ropeSegments = 15;
    public float sagFactor = 0.6f;
    [Tooltip("المسافة من مركز السطل إلى النقطة التي يلمس فيها الحبل. اجعلها 0.3 لتطابق المجسم")]
    public float ropeAttachmentOffset = 0.3f;

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

    public Vector3 GetBucketPosition() => transform.position;
    public float GetBucketSpeed() => velocity.magnitude;
    public Vector3 GetVelocityVector() => velocity;
    public float GetFillRatio() => currentPaintMass / maxPaintMass;
    public bool isSimulating => !isDragging && velocity.magnitude > 0.01f;
    public float GetThetaDegrees() => theta * Mathf.Rad2Deg;
    public float GetPhiDegrees() => phi * Mathf.Rad2Deg;

    public void SetSphericalAngles(float thetaDegrees, float phiDegrees)
    {
        if (pivot == null) return;

        float t = thetaDegrees * Mathf.Deg2Rad;
        float p = phiDegrees * Mathf.Deg2Rad;

        float y = -Mathf.Cos(t);
        float horizontal = Mathf.Sin(t);
        float x = horizontal * Mathf.Cos(p);
        float z = horizontal * Mathf.Sin(p);

        Vector3 direction = new Vector3(x, y, z).normalized;
        transform.position = pivot.position + direction * baseLength;
        velocity = Vector3.zero;
        isDragging = false;

        SyncAnglesFromPosition();
        UpdateRopeRenderer();
    }

    void Start()
    {
        ropeRenderer = GetComponent<LineRenderer>();
        if (ropeRenderer == null)
            ropeRenderer = gameObject.AddComponent<LineRenderer>();

        SetupRopeRenderer();

        currentPaintMass = maxPaintMass;

        if (pivot != null)
        {
            baseLength = Vector3.Distance(transform.position, pivot.position);
            UpdateMassAndCOM();
            SyncAnglesFromPosition();
        }

        velocity = new Vector3(0.5f, 0f, 0f);
    }

    void SetupRopeRenderer()
    {
        ropeRenderer.material = new Material(Shader.Find("Sprites/Default"));
        ropeRenderer.startColor = Color.white;
        ropeRenderer.endColor = Color.white;
        ropeRenderer.startWidth = 0.04f;
        ropeRenderer.endWidth = 0.02f;
        ropeRenderer.positionCount = ropeSegments;
        ropeRenderer.useWorldSpace = true;
    }

    void FixedUpdate()
    {
        if (isDragging || pivot == null) return;

        UpdateMassAndCOM();

        float totalMass = emptyBucketMass + currentPaintMass;
        float massDerivative = (currentPaintMass > 0) ? drainRate : 0f;

        // تم تثبيت معامل السحب للشكل الأسطواني مباشرة لحذف الحسابات الزائدة
        float dragCoefficient = 0.82f;

        float totalDamping = ((airDamping * dragCoefficient) + massDerivative) / totalMass + pivotFriction;
        float simulatedGravity = gravity * (baseLength / effectiveLength);

        velocity += Vector3.down * simulatedGravity * Time.fixedDeltaTime;
        velocity -= velocity * totalDamping * Time.fixedDeltaTime;

        Vector3 nextPosition = transform.position + velocity * Time.fixedDeltaTime;
        Vector3 offset = nextPosition - pivot.position;
        float currentDistance = offset.magnitude;
        Vector3 ropeDirection = (currentDistance > 0.0001f) ? offset.normalized : Vector3.down;

        if (isElastic)
        {
            if (currentDistance > baseLength)
            {
                float stretch = currentDistance - baseLength;
                float springForce = stretch * springConstant;

                float radialVelocity = Vector3.Dot(velocity, ropeDirection);
                float dampForce = radialVelocity * springDamping;

                float totalRestoringAccel = (springForce + dampForce) / totalMass;
                velocity -= ropeDirection * (totalRestoringAccel * Time.fixedDeltaTime);
            }

            nextPosition = transform.position + velocity * Time.fixedDeltaTime;
            float nextDistance = (nextPosition - pivot.position).magnitude;

            if (nextDistance > maxElasticLength)
            {
                Vector3 nextRopeDirection = (nextPosition - pivot.position).normalized;
                nextPosition = pivot.position + nextRopeDirection * maxElasticLength;

                float radialVelocity = Vector3.Dot(velocity, nextRopeDirection);
                if (radialVelocity > 0f)
                    velocity -= nextRopeDirection * radialVelocity;
            }

            transform.position = nextPosition;
        }
        else
        {
            if (currentDistance >= baseLength)
            {
                transform.position = pivot.position + ropeDirection * baseLength;
                float radialVelocity = Vector3.Dot(velocity, ropeDirection);
                if (radialVelocity > 0f)
                    velocity -= ropeDirection * radialVelocity;
            }
            else
            {
                transform.position = nextPosition;
            }
        }

        SyncAnglesFromPosition();

        float I_y = 0.5f * totalMass * (bucketRadius * bucketRadius);
        float torsionalTorque = (-torsionalStiffness * twistAngle) - (torsionalDamping * twistVelocity);
        float twistAcceleration = torsionalTorque / I_y;
        twistVelocity += twistAcceleration * Time.fixedDeltaTime;
        twistAngle += twistVelocity * Time.fixedDeltaTime;

        if (Mathf.Abs(theta) < 0.02f
            && velocity.magnitude < 0.05f
            && Mathf.Abs(twistVelocity) < 0.05f
            && (!isElastic || Mathf.Abs(currentDistance - baseLength) < 0.05f))
        {
            transform.position = pivot.position + Vector3.down * baseLength;
            velocity = Vector3.zero;
            theta = 0f;
            thetaVelocity = 0f;
            phiVelocity = 0f;
            twistAngle = 0f;
            twistVelocity = 0f;
        }

        UpdateRopeRenderer();
        UpdateSharedData();
    }

    void UpdateMassAndCOM()
    {
        if (currentPaintMass <= 0f)
        {
            currentPaintMass = 0f;
            effectiveLength = baseLength + bucketHeight;
            return;
        }

        currentPaintMass -= drainRate * Time.fixedDeltaTime;

        if (currentPaintMass <= 0f)
        {
            currentPaintMass = 0f;
            effectiveLength = baseLength + bucketHeight;
            return;
        }

        float fillRatio = maxPaintMass > 0 ? currentPaintMass / maxPaintMass : 0;

        // تم تبسيط حساب مركز الثقل مباشرة للأسطوانة بدون Switch
        float z_cm = (bucketHeight * fillRatio) / 2f;

        effectiveLength = baseLength + (bucketHeight - z_cm);
    }

    void SyncAnglesFromPosition()
    {
        Vector3 localPos = transform.position - pivot.position;
        lastTheta = theta;
        lastPhi = phi;

        theta = Mathf.Acos(Mathf.Clamp(-localPos.normalized.y, -1f, 1f));
        phi = Mathf.Atan2(localPos.z, localPos.x);

        if (Time.fixedDeltaTime > 0)
        {
            thetaVelocity = (theta - lastTheta) / Time.fixedDeltaTime;
            phiVelocity = Mathf.DeltaAngle(lastPhi * Mathf.Rad2Deg, phi * Mathf.Rad2Deg) * Mathf.Deg2Rad / Time.fixedDeltaTime;
        }
    }

    void OnMouseDown()
    {
        isDragging = true;
        velocity = Vector3.zero;
        dragDepth = Camera.main.WorldToScreenPoint(transform.position).z;
        lastDragPosition = transform.position;
    }

    void OnMouseDrag()
    {
        if (pivot == null) return;
        dragDepth += Input.GetAxis("Mouse ScrollWheel") * 5f;

        Vector3 mouseScreenPoint = new Vector3(Input.mousePosition.x, Input.mousePosition.y, dragDepth);
        Vector3 mouseWorldPoint = Camera.main.ScreenToWorldPoint(mouseScreenPoint);

        Vector3 offset = mouseWorldPoint - pivot.position;
        float currentMaxLimit = isElastic ? maxElasticLength : baseLength;
        if (offset.magnitude > currentMaxLimit)
        {
            offset = offset.normalized * currentMaxLimit;
        }

        Vector3 newPosition = pivot.position + offset;

        if (Time.deltaTime > 0)
            velocity = (newPosition - lastDragPosition) / Time.deltaTime;

        transform.position = newPosition;
        lastDragPosition = newPosition;

        SyncAnglesFromPosition();
        UpdateRopeRenderer();
    }

    void OnMouseUp() { isDragging = false; }

    void UpdateRopeRenderer()
    {
        if (pivot == null || ropeRenderer == null) return;

        Vector3 p0 = pivot.position;
        Vector3 comPosition = transform.position;
        float offsetToHandle = ropeAttachmentOffset;

        Vector3 directionToPivot = (p0 - comPosition).normalized;
        Vector3 straightHandlePos = comPosition + directionToPivot * offsetToHandle;

        Vector3 p1 = (p0 + straightHandlePos) * 0.5f;
        float currentDist = (straightHandlePos - p0).magnitude;

        if (currentDist < baseLength && !isDragging)
        {
            float slack = baseLength - currentDist;
            p1 += Vector3.down * slack * sagFactor;
        }

        Vector3 ropeDirectionAtBucket = (p1 - comPosition).normalized;
        if (ropeDirectionAtBucket.magnitude > 0.01f)
        {
            Quaternion baseRotation = Quaternion.FromToRotation(Vector3.up, ropeDirectionAtBucket);
            Quaternion twistRotation = Quaternion.AngleAxis(twistAngle * Mathf.Rad2Deg, Vector3.up);
            transform.rotation = baseRotation * twistRotation;
        }

        Vector3 actualHandlePos = transform.position + transform.up * offsetToHandle;

        ropeRenderer.positionCount = ropeSegments;
        for (int i = 0; i < ropeSegments; i++)
        {
            float t = i / (float)(ropeSegments - 1);
            Vector3 point = Mathf.Pow(1f - t, 2) * p0 + 2f * (1f - t) * t * p1 + Mathf.Pow(t, 2) * actualHandlePos;
            ropeRenderer.SetPosition(i, point);
        }

        if (ropeRenderer.material != null)
        {
            ropeRenderer.material.mainTextureOffset = new Vector2(twistAngle * -0.5f, 0);
        }
    }

    void UpdateSharedData()
    {
        if (data == null) return;
        data.totalMass = emptyBucketMass + currentPaintMass;
        data.currentPaintMass = currentPaintMass;
        data.effectiveLength = effectiveLength;
        data.fillRatio = maxPaintMass > 0 ? currentPaintMass / maxPaintMass : 0;
        data.theta = theta;
        data.phi = phi;
        data.angularVelocityTheta = thetaVelocity;
        data.angularVelocityPhi = phiVelocity;
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