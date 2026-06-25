using UnityEngine;

public class DebugSPH : MonoBehaviour
{
    public SPHSimulation sph;
    public BucketPendulum bucket;

    void Update()
    {
        if (sph == null || bucket == null) return;

        // اقرأ أول 3 جزيئات
        var buffer = sph.GetParticleBuffer();
        if (buffer == null)
        {
            Debug.LogError("ParticleBuffer = NULL !");
            return;
        }

        // Struct مطابق تماماً
        ParticleDebug[] data = new ParticleDebug[3];
        buffer.GetData(data, 0, 0, 3);

        Debug.Log($"Bucket Pos: {bucket.GetBucketPosition()}");
        Debug.Log($"Particle 0: {data[0].position}");
        Debug.Log($"Particle 1: {data[1].position}");
        Debug.Log($"Particle 2: {data[2].position}");
    }

    struct ParticleDebug
    {
        public Vector3 position;
        public Vector3 velocity;
        public Vector2 density;
        public float _p0, _p1;
    }
}