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
            DrawRingWorld(rings[i], center, rot, holes[i].localPosition, holes[i].radius);
        }
    }

    void DrawRingWorld(LineRenderer lr, Vector3 center, Quaternion rot,
                       Vector3 localCenter, float radius)
    {
        // النورمال = اتجاه الثقب من المركز
        Vector3 normal = localCenter.normalized;
        if (normal.sqrMagnitude < 0.0001f)
            normal = Vector3.up;

        Vector3 tangent = Vector3.Cross(normal, Vector3.up);
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.Cross(normal, Vector3.right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent);

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