using UnityEngine;
using System.Collections.Generic;

public class HoleVisualizer : MonoBehaviour
{
    [Header("المرجع")]
    public SPHSimulation1 simulation;

    [Header("شكل الحلقة")]
    [Tooltip("عدد النقاط في الحلقة (نعومة الدائرة)")]
    public int segments = 32;
    [Tooltip("سماكة خط الحلقة")]
    public float lineWidth = 0.01f;
    public Color ringColor = new Color(1f, 0.2f, 0.1f, 1f);

    readonly List<LineRenderer> rings = new List<LineRenderer>();
    Material ringMat;

    void Start()
    {
        if (simulation == null)
        {
            Debug.LogError("HoleVisualizer: simulation فارغ!");
            return;
        }
        ringMat = new Material(Shader.Find("Sprites/Default"));
        BuildRings();
    }

    void BuildRings()
    {
        var holes = simulation.holes;
        if (holes == null) return;

        for (int i = 0; i < holes.Length; i++)
        {
            var go = new GameObject($"HoleRing_{i}");

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.loop = true;
            // سيتم تحديث عدد النقاط لاحقاً بناءً على شكل الثقب (دائرة أو مستطيل)
            lr.positionCount = segments;
            lr.startWidth = lineWidth;
            lr.endWidth = lineWidth;
            lr.material = ringMat;
            lr.startColor = ringColor;
            lr.endColor = ringColor;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;

            rings.Add(lr);
        }
        UpdateRings();
    }

    void UpdateRings()
    {
        var holes = simulation.holes;
        if (holes == null) return;

        Transform bucket = simulation.bucketTransform != null
            ? simulation.bucketTransform
            : simulation.transform;

        Vector3 center = bucket.position;
        Quaternion rot = bucket.rotation;

        for (int i = 0; i < rings.Count && i < holes.Length; i++)
        {
            if (rings[i] == null) continue;
            // إرسال الثقب بالكامل للدالة الجديدة التي تتعامل مع الأشكال
            DrawHoleWorld(rings[i], center, rot, holes[i]);
        }
    }

    void DrawHoleWorld(LineRenderer lr, Vector3 center, Quaternion rot, SPHSimulation1.DrainHole hole)
    {
        Vector3 localCenter = hole.localPosition;

        if (hole.shape == SPHSimulation1.HoleShape.Circle)
        {
            lr.positionCount = segments;
            float radius = hole.size.x; // تم استبدال radius بـ size.x ليتوافق مع التعديل الجديد

            // النورمال = اتجاه الثقب من المركز
            Vector3 normal = localCenter.normalized;
            if (normal.sqrMagnitude < 0.0001f)
                normal = Vector3.up;

            Vector3 tangent = Vector3.Cross(normal, Vector3.up);
            if (tangent.sqrMagnitude < 0.0001f)
                tangent = Vector3.Cross(normal, Vector3.right);
            tangent.Normalize();
            Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

            for (int s = 0; s < segments; s++)
            {
                float a = (s / (float)segments) * Mathf.PI * 2f;
                Vector3 localOffset = (Mathf.Cos(a) * tangent + Mathf.Sin(a) * bitangent) * radius;
                Vector3 localPoint = localCenter + localOffset;
                // تحويل لعالمي: مركز + دوران*محلي (بدون scale، مطابق للفيزياء)
                Vector3 worldPoint = center + rot * localPoint;
                lr.SetPosition(s, worldPoint);
            }
        }
        else if (hole.shape == SPHSimulation1.HoleShape.Rectangle)
        {
            // المستطيل يحتاج 4 نقاط فقط (ولأننا فعلنا lr.loop = true سيتم إغلاق المربع تلقائياً)
            lr.positionCount = 4;

            // حساب الزوايا الأربع بناءً على الأبعاد (Half-extents) للمستطيل في الفضاء المحلي للسطل
            Vector3 c1 = localCenter + new Vector3(hole.size.x, 0, hole.size.z);
            Vector3 c2 = localCenter + new Vector3(hole.size.x, 0, -hole.size.z);
            Vector3 c3 = localCenter + new Vector3(-hole.size.x, 0, -hole.size.z);
            Vector3 c4 = localCenter + new Vector3(-hole.size.x, 0, hole.size.z);

            // تحويل الزوايا من فضاء السطل المحلي إلى فضاء العالم
            lr.SetPosition(0, center + rot * c1);
            lr.SetPosition(1, center + rot * c2);
            lr.SetPosition(2, center + rot * c3);
            lr.SetPosition(3, center + rot * c4);
        }
    }

    void LateUpdate()
    {
        // الحلقات تتبع السطل المتحرك كل إطار
        if (rings.Count > 0) UpdateRings();
    }

    // تحديث الحلقات لو تغيّرت الثقوب وقت اللعب (اختياري)
    public void Refresh()
    {
        foreach (var r in rings)
            if (r != null) Destroy(r.gameObject);
        rings.Clear();
        BuildRings();
    }

    void OnDestroy()
    {
        foreach (var r in rings)
            if (r != null) Destroy(r.gameObject);
        if (ringMat != null) Destroy(ringMat);
    }
}