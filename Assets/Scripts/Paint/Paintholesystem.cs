using UnityEngine;
using System.Collections.Generic;

public class PaintHoleSystem : MonoBehaviour
{
    // =================== تعريف الثقب ===================
    [System.Serializable]
    public class PaintHole
    {
        public enum HoleSide
        {
            Bottom,     // أسفل
            Top,        // أعلى
            Left,       // يسار
            Right,      // يمين
            Front,      // أمام
            Back,       // خلف
            Custom      // موقع مخصص
        }

        // موقع الثقب
        public HoleSide side = HoleSide.Bottom;
        public Vector3 customLocalOffset = Vector3.zero;

        // خصائص الثقب
        [Range(0.005f, 0.1f)]
        public float radius = 0.02f;

        [Range(1f, 50f)]
        public float dropsPerSecond = 15f;

        public Color paintColor = Color.red;

        // حالة الثقب
        public bool isOpen = true;

        // داخلي
        [HideInInspector] public float timer = 0f;

        // احسب موقع الثقب بالعالم
        public Vector3 GetWorldPosition(Vector3 bucketPos, Vector3 bucketHalf)
        {
            Vector3 local = GetLocalOffset(bucketHalf);
            return bucketPos + local;
        }

        public Vector3 GetLocalOffset(Vector3 bucketHalf)
        {
            switch (side)
            {
                case HoleSide.Bottom: return new Vector3(0, -bucketHalf.y, 0);
                case HoleSide.Top:    return new Vector3(0,  bucketHalf.y, 0);
                case HoleSide.Left:   return new Vector3(-bucketHalf.x, 0, 0);
                case HoleSide.Right:  return new Vector3( bucketHalf.x, 0, 0);
                case HoleSide.Front:  return new Vector3(0, 0,  bucketHalf.z);
                case HoleSide.Back:   return new Vector3(0, 0, -bucketHalf.z);
                case HoleSide.Custom: return customLocalOffset;
                default:              return Vector3.zero;
            }
        }

        // اتجاه خروج الدهان من الثقب
        public Vector3 GetFlowDirection()
        {
            switch (side)
            {
                case HoleSide.Bottom: return Vector3.down;
                case HoleSide.Top:    return Vector3.up;
                case HoleSide.Left:   return Vector3.left;
                case HoleSide.Right:  return Vector3.right;
                case HoleSide.Front:  return Vector3.forward;
                case HoleSide.Back:   return Vector3.back;
                case HoleSide.Custom: return customLocalOffset.normalized;
                default:              return Vector3.down;
            }
        }
    }

    // =================== إعدادات النظام ===================

    [Header("=== المراجع ===")]
    public SphericalPendulumMath bucket;
    public CanvasPainter canvasPainter;

    [Header("=== حجم السطل ===")]
    public Vector3 bucketHalfSize = new Vector3(0.2f, 0.25f, 0.2f);

    [Header("=== كمية الدهان ===")]
    public float paintMassTotal = 2f;
    public float paintMassLeft;

    [Header("=== قوة التدفق ===")]
    [Range(0.5f, 10f)]
    public float flowBoost = 2f;

    [Header("=== الثقوب ===")]
    public List<PaintHole> holes = new List<PaintHole>();

    // =================== Start ===================

    void Start()
    {
        paintMassLeft = paintMassTotal;

        // إذا ما في ثقوب، أضف ثقب أسفل افتراضي
        if (holes.Count == 0)
        {
            holes.Add(new PaintHole
            {
                side           = PaintHole.HoleSide.Bottom,
                radius         = 0.02f,
                dropsPerSecond = 15f,
                paintColor     = Color.red,
                isOpen         = true
            });
        }
    }

    // =================== Update ===================

    void Update()
    {
        if (bucket == null || paintMassLeft <= 0f) return;

        int openHoles = CountOpenHoles();
        if (openHoles == 0) return;

        float massPerHolePerSec = (paintMassTotal / 30f) / openHoles;

        foreach (var hole in holes)
        {
            if (!hole.isOpen) continue;

            hole.timer += Time.deltaTime;
            float interval = 1f / hole.dropsPerSecond;

            if (hole.timer >= interval)
            {
                hole.timer = 0f;
                SpawnDrop(hole);

                // انقص الدهان
                paintMassLeft -= massPerHolePerSec * interval;
                paintMassLeft  = Mathf.Max(0f, paintMassLeft);
            }
        }
    }

    // =================== رش قطرة ===================

    void SpawnDrop(PaintHole hole)
    {
        Vector3 holePos     = hole.GetWorldPosition(bucket.GetBucketPosition(),
                                                     bucketHalfSize);
        Vector3 flowDir     = hole.GetFlowDirection();
        Vector3 bucketVel   = bucket.GetVelocityVector();
        Vector3 dropVel     = bucketVel + flowDir * flowBoost;

        // للثقوب الجانبية: أضف جاذبية عشان تنزل على اللوحة
        if (hole.side != PaintHole.HoleSide.Bottom)
            dropVel += Vector3.down * 1.5f;

        // رمي شعاع لإيجاد اللوحة
        Ray ray = new Ray(holePos, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, 20f)
            && hit.collider.CompareTag("Canvas"))
        {
            float speed   = bucket.GetBucketSpeed();
            float dotSize = Mathf.Lerp(0.008f, 0.04f, speed / 5f);

            // للثقوب الجانبية: البقعة أكبر وأفقية أكثر
            if (hole.side != PaintHole.HoleSide.Bottom)
                dotSize *= 1.5f;

            canvasPainter.Splat(hit.point, Vector3.down * bucket.GetBucketSpeed(), hole.paintColor);
        }
    }

    // =================== دوال عامة ===================

    // افتح/أقفل ثقب معين
    public void ToggleHole(int index)
    {
        if (index < 0 || index >= holes.Count) return;
        holes[index].isOpen = !holes[index].isOpen;
    }

    // افتح كل الثقوب
    public void OpenAll()
    {
        foreach (var h in holes) h.isOpen = true;
    }

    // أقفل كل الثقوب
    public void CloseAll()
    {
        foreach (var h in holes) h.isOpen = false;
    }

    // عدد الثقوب المفتوحة
    public int CountOpenHoles()
    {
        int count = 0;
        foreach (var h in holes) if (h.isOpen) count++;
        return count;
    }

    // نسبة الامتلاء
    public float GetFillRatio() =>
        paintMassTotal > 0 ? paintMassLeft / paintMassTotal : 0f;

    // =================== Gizmos ===================

    void OnDrawGizmos()
    {
        if (bucket == null) return;

        Vector3 bucketPos = bucket.GetBucketPosition();

        // ارسم حدود السطل
        Gizmos.color = new Color(0, 1, 1, 0.15f);
        Gizmos.DrawWireCube(bucketPos, bucketHalfSize * 2f);

        // ارسم كل ثقب
        foreach (var hole in holes)
        {
            Vector3 holePos = hole.GetWorldPosition(bucketPos, bucketHalfSize);
            Vector3 flowDir = hole.GetFlowDirection();

            // لون حسب الحالة
            Gizmos.color = hole.isOpen ? hole.paintColor : Color.gray;
            Gizmos.DrawWireSphere(holePos, hole.radius);

            // ارسم سهم اتجاه التدفق
            Gizmos.color = new Color(
                hole.paintColor.r,
                hole.paintColor.g,
                hole.paintColor.b,
                0.5f
            );
            Gizmos.DrawLine(holePos, holePos + flowDir * 0.3f);
            Gizmos.DrawWireSphere(holePos + flowDir * 0.3f, 0.02f);
        }

        // ارسم شريط الامتلاء
        if (Application.isPlaying)
        {
            float ratio   = GetFillRatio();
            Vector3 start = bucketPos + Vector3.left * 0.5f;
            Vector3 end   = bucketPos + Vector3.left * 0.5f + Vector3.up * ratio;
            Gizmos.color  = Color.Lerp(Color.red, Color.green, ratio);
            Gizmos.DrawLine(start, end);
        }
    }

    // =================== GUI ===================

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        float ratio = GetFillRatio();

        GUILayout.BeginArea(new Rect(Screen.width - 220, 10, 210, 400));
        GUILayout.Label("=== الثقوب ===");

        // شريط الدهان
        GUILayout.Label($"الدهان: {paintMassLeft:F2} / {paintMassTotal:F1}");
        Rect barRect = GUILayoutUtility.GetRect(200, 15);
        GUI.Box(barRect, "");
        Rect fillRect = new Rect(barRect.x, barRect.y,
                                  barRect.width * ratio, barRect.height);
        GUI.color   = Color.Lerp(Color.red, Color.green, ratio);
        GUI.Box(fillRect, "");
        GUI.color   = Color.white;

        GUILayout.Space(5);

        // أزرار كل ثقب
        for (int i = 0; i < holes.Count; i++)
        {
            var hole = holes[i];
            GUILayout.BeginHorizontal();

            string status = hole.isOpen ? "🟢" : "🔴";
            string label  = $"{status} {hole.side} ({hole.paintColor.r:F0}r)";

            if (GUILayout.Button(label, GUILayout.Width(150)))
                ToggleHole(i);

            GUILayout.EndHorizontal();
        }

        GUILayout.Space(5);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("افتح كل")) OpenAll();
        if (GUILayout.Button("أقفل كل")) CloseAll();
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}