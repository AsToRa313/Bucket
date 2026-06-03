using UnityEngine;

public class PaintSpawner : MonoBehaviour
{
    public GameObject paintDropPrefab;
    public SphericalPendulumMath bucketPhysics; 
    public Rema fluidDynamics;                 

    public float spawnRate = 0.05f;
    private float timer = 0f;

    void Update()
    {
        if (bucketPhysics == null || fluidDynamics == null || bucketPhysics.data == null || bucketPhysics.data.currentPaintMass <= 0) return;

        // حسابات الموائ
        fluidDynamics.CalculateFluidPhysics(bucketPhysics.shape, bucketPhysics.bucketHeight, bucketPhysics.data.currentPaintMass);

        if (fluidDynamics.currentDrainRate <= 0f) return;

        // تعديل معدل نقصان الكتلة 
        bucketPhysics.drainRate = fluidDynamics.currentDrainRate;

        timer += Time.deltaTime;

        float dynamicSpawnRate = fluidDynamics.actualFlowVelocity > 0 ? (0.1f / fluidDynamics.actualFlowVelocity) : spawnRate;
        dynamicSpawnRate = Mathf.Clamp(dynamicSpawnRate, 0.01f, 0.5f);

        if (timer >= dynamicSpawnRate)
        {
            timer = 0f;
            SpawnDrop();
        }
    }

    void SpawnDrop()
    {
        GameObject drop = Instantiate(paintDropPrefab, transform.position, Quaternion.identity);
        PaintDrop dropScript = drop.GetComponent<PaintDrop>();

        if (dropScript != null)
        {
            Vector3 bucketVelocity = bucketPhysics.data.linearVelocity;
            Vector3 flowVelocity = -transform.up * fluidDynamics.actualFlowVelocity;
            dropScript.initialVelocity = bucketVelocity + flowVelocity;
        }
    }
}