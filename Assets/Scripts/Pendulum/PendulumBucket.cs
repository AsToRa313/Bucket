using UnityEngine;

/// <summary>
/// بندول كروي للسطل
/// ─ السطل ثابت تماماً في البداية (لا يتحرك حتى تحركيه أنتِ)
/// ─ اسحبيه بالماوس أو اضغطي Space لدفعه
/// ─ السائل بداخله يتفاعل مع الحركة
/// </summary>
[RequireComponent(typeof(LineRenderer))]
public class PendulumBucket : MonoBehaviour, IBucketPhysics
{
    [Header("نقطة التعليق")]
    public Transform pivot;

    [Header("الخيط")]
    public float ropeLength  = 4f;
    [Range(0f, 0.02f)]
    public float airDamping  = 0.008f;

    [Header("الجاذبية")]
    public float gravity     = 9.81f;

    [Header("البداية — ثابت")]
    [Tooltip("السطل يبدأ ثابتاً تماماً — لا يتحرك إلا لما تسحبيه")]
    public bool startStill   = true;

    [Header("السحب بالماوس")]
    public bool enableDrag   = true;
    [Tooltip("Space = دفعة قوية")]
    public KeyCode kickKey   = KeyCode.Space;
    public float   kickForce = 3f;

    // حالة داخلية
    Vector3      vel         = Vector3.zero;
    bool         isDragging  = false;
    float        dragDepth;
    Vector3      lastDragPos;
    LineRenderer rope;
    Camera       cam;

    // Public
    public Vector3 GetBucketPosition() => transform.position;
    public Vector3 GetVelocity()       => vel;
    public Vector3 GetVelocityVector() => vel;
    void Start()
    {
        cam  = Camera.main;
        rope = GetComponent<LineRenderer>();
        SetupRope();

        if (pivot == null)
        {
            Debug.LogWarning("⚠️ pivot فارغ!");
            return;
        }

        // ضع السطل مباشرة أسفل pivot بطول الخيط — ثابت تماماً
        transform.position = pivot.position + Vector3.down * ropeLength;
        transform.rotation = Quaternion.identity;
        vel                = Vector3.zero;

        Debug.Log($"✅ PendulumBucket: السطل عند {transform.position} — ثابت، في انتظار السحب");
    }

    void SetupRope()
    {
        rope.positionCount = 24;
        rope.startWidth    = 0.035f;
        rope.endWidth      = 0.015f;
        rope.material      = new Material(Shader.Find("Sprites/Default"));
        rope.startColor    = new Color(0.55f, 0.38f, 0.18f);
        rope.endColor      = new Color(0.40f, 0.27f, 0.10f);
        rope.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void FixedUpdate()
    {
        if (isDragging || pivot == null) return;
        if (startStill && vel.sqrMagnitude < 0.0001f) { UpdateRopeVisual(); return; }

        // جاذبية + تخامد
        vel += Vector3.down * gravity * Time.fixedDeltaTime;
        vel -= vel * airDamping * Time.fixedDeltaTime;

        Vector3 next = transform.position + vel * Time.fixedDeltaTime;
        Vector3 dir  = next - pivot.position;
        float   dist = dir.magnitude;

        // قيد الخيط
        if (dist > ropeLength)
        {
            dir  = dir.normalized;
            next = pivot.position + dir * ropeLength;
            float radV = Vector3.Dot(vel, dir);
            if (radV > 0f) vel -= dir * radV;
        }

        transform.position = next;

        // دوران السطل ليواجه الخيط بشكل واقعي (3D)
        Vector3 upDirection = (pivot.position - transform.position).normalized;
        if (upDirection.sqrMagnitude > 0.01f)
        {
            // نستخدم Quaternion.FromToRotation لجعل محور Y المحلي يشير للأعلى باتجاه pivot
            Quaternion targetRot = Quaternion.FromToRotation(Vector3.up, upDirection);
            // تنعيم الدوران قليلاً لتجنب القفزات المفاجئة
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.fixedDeltaTime * 10f);
        }

        // سكون
        bool nearRest = Vector3.Distance(transform.position, pivot.position + Vector3.down * ropeLength) < 0.005f;
        if (nearRest && vel.magnitude < 0.01f)
        {
            vel = Vector3.zero;
            transform.position = pivot.position + Vector3.down * ropeLength;
        }

        UpdateRopeVisual();
    }

    void Update()
    {
        if (!enableDrag || cam == null) return;

        // ── سحب بالماوس ──
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = cam.ScreenPointToRay(Input.mousePosition);
            // نقبل السحب لو الماوس قريب من السطل (بدون Raycast لضمان الشغل)
            Vector3 screenBucket = cam.WorldToScreenPoint(transform.position);
            Vector2 diff = new Vector2(Input.mousePosition.x - screenBucket.x,
                                       Input.mousePosition.y - screenBucket.y);
            if (diff.magnitude < 60f)
            {
                isDragging  = true;
                vel         = Vector3.zero;
                dragDepth   = screenBucket.z;
                lastDragPos = transform.position;
            }
        }

        if (isDragging && Input.GetMouseButton(0))
        {
            dragDepth += Input.GetAxis("Mouse ScrollWheel") * 3f;
            Vector3 screen = new Vector3(Input.mousePosition.x, Input.mousePosition.y, dragDepth);
            Vector3 world  = cam.ScreenToWorldPoint(screen);

            // قيد الخيط
            Vector3 d = (world - pivot.position);
            if (d.magnitude > ropeLength) d = d.normalized * ropeLength;
            Vector3 target = pivot.position + d;

            if (Time.deltaTime > 0f)
                vel = (target - lastDragPos) / Time.deltaTime * 0.6f;

            transform.position = target;
            lastDragPos        = target;
            UpdateRopeVisual();
        }

        if (Input.GetMouseButtonUp(0) && isDragging)
            isDragging = false;

        // ── Space: دفعة ──
        if (Input.GetKeyDown(kickKey))
        {
            vel += new Vector3(
                Random.Range(-kickForce, kickForce),
                0f,
                Random.Range(-kickForce * 0.3f, kickForce * 0.3f));
            Debug.Log("💥 دفعة!");
        }
    }

    void UpdateRopeVisual()
    {
        if (pivot == null) return;
        Vector3 start = pivot.position;
        Vector3 end   = transform.position + transform.up * 0.05f;
        float slack   = Mathf.Max(0f, ropeLength - Vector3.Distance(start, end)) * 0.3f;
        Vector3 mid   = (start + end) * 0.5f + Vector3.down * slack;

        int segs = rope.positionCount;
        for (int i = 0; i < segs; i++)
        {
            float t = i / (float)(segs - 1);
            float u = 1f - t;
            rope.SetPosition(i, u * u * start + 2f * u * t * mid + t * t * end);
        }
    }

    void OnDrawGizmos()
    {
        if (pivot == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(pivot.position, transform.position);
        Gizmos.color = new Color(1f, 1f, 0f, 0.2f);
        Gizmos.DrawWireSphere(pivot.position, ropeLength);
    }
}

