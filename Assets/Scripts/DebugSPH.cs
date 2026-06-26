using UnityEngine;

public class SPHDebugger : MonoBehaviour
{
    public SPHSimulation simulation;
    
    Vector3[] positions;
    Vector3[] velocities;
    
    // لتتبع التغيير بمرور الوقت
    int   lastSurvivors    = -1;
    float lastBucketX      = 0f;
    int   framesDecreasing = 0;
    
    void Update()
    {
        if (simulation == null) return;
        
        int count = simulation.GetParticleCount();
        if (positions == null || positions.Length != count)
        {
            positions  = new Vector3[count];
            velocities = new Vector3[count];
        }
        
        simulation.GetPositionBuffer().GetData(positions);
        simulation.GetVelocityBuffer().GetData(velocities);
        
        Vector3 bp   = simulation.bucketTransform.position;
        Vector3 bRot = simulation.bucketTransform.eulerAngles;
        Vector3 half = simulation.bucketHalfSize;
        Quaternion bQ = simulation.bucketTransform.rotation;
        
        // احسب كل جزيء
        int survivors   = 0;
        int escapedX    = 0;
        int escapedY    = 0;
        int escapedZ    = 0;
        int highSpeed   = 0;
        int zeroSpeed   = 0;
        float totalSpeed = 0f;
        float maxSpeed   = 0f;
        int   maxIdx     = 0;
        
        // أكثر اتجاه هروب
        int escLeft=0, escRight=0, escUp=0, escDown=0, escFront=0, escBack=0;
        
        for (int i = 0; i < count; i++)
        {
            Vector3 p   = positions[i];
            Vector3 v   = velocities[i];
            float   spd = v.magnitude;
            
            totalSpeed += spd;
            if (spd > maxSpeed) { maxSpeed = spd; maxIdx = i; }
            if (spd > 5f)  highSpeed++;
            if (spd < 0.001f) zeroSpeed++;
            
            // تحويل للإطار المحلي للسطل (مثل الـ Compute Shader بالضبط)
            Vector3 local = Quaternion.Inverse(bQ) * (p - bp);
            
            bool okX = local.x >= -half.x && local.x <= half.x;
            bool okY = local.y >= -half.y && local.y <= half.y;
            bool okZ = local.z >= -half.z && local.z <= half.z;
            
            if (okX && okY && okZ)
            {
                survivors++;
            }
            else
            {
                if (!okX)
                {
                    escapedX++;
                    if (local.x < -half.x) escLeft++;
                    else                   escRight++;
                }
                if (!okY)
                {
                    escapedY++;
                    if (local.y < -half.y) escDown++;
                    else                   escUp++;
                }
                if (!okZ)
                {
                    escapedZ++;
                    if (local.z < -half.z) escBack++;
                    else                   escFront++;
                }
            }
        }
        
        float avgSpeed = count > 0 ? totalSpeed / count : 0f;
        
        // كشف لو الجزيئات عم تقل
        if (lastSurvivors > 0 && survivors < lastSurvivors)
            framesDecreasing++;
        else
            framesDecreasing = 0;
            
        float bucketMoveX = bp.x - lastBucketX;
        lastBucketX = bp.x;
        
        if (Time.frameCount % 30 == 0)
        {
            Debug.Log("═══════════════════════════════════════");
            
            // معلومات السطل
            Debug.Log($"[السطل] pos={bp:F3} | rot={bRot:F1} | half={half:F2}");
            Debug.Log($"[السطل] حركة هذا الفريم: Δx={bucketMoveX:F4}");
            Debug.Log($"[السطل] حدود X=[{bp.x-half.x:F2}..{bp.x+half.x:F2}] Y=[{bp.y-half.y:F2}..{bp.y+half.y:F2}] Z=[{bp.z-half.z:F2}..{bp.z+half.z:F2}]");
            
            // معلومات الجزيئات
            Debug.Log($"[جزيئات] داخل={survivors}/{count} | هاربة X={escapedX} Y={escapedY} Z={escapedZ}");
            Debug.Log($"[جزيئات] اتجاه الهروب: يسار={escLeft} يمين={escRight} فوق={escUp} تحت={escDown} أمام={escFront} خلف={escBack}");
            Debug.Log($"[سرعات] متوسط={avgSpeed:F3} | أقصى={maxSpeed:F3} | سريعة جداً={highSpeed} | صفر={zeroSpeed}");
            
            // أسرع جزيء
            Debug.Log($"[أسرع] #{maxIdx}: pos={positions[maxIdx]:F3} vel={velocities[maxIdx]:F3}");
            
            // أول 5 جزيئات
            Debug.Log("--- أول 5 جزيئات ---");
            for (int i = 0; i < Mathf.Min(5, count); i++)
            {
                Vector3 loc = Quaternion.Inverse(bQ) * (positions[i] - bp);
                bool inside = Mathf.Abs(loc.x)<=half.x && Mathf.Abs(loc.y)<=half.y && Mathf.Abs(loc.z)<=half.z;
                Debug.Log($"  [{i}] world={positions[i]:F3} local={loc:F3} vel={velocities[i]:F3} | {(inside?"✓داخل":"✗خارج")}");
            }
            
            // تحذير إذا عم يقلوا
            if (framesDecreasing > 5)
                Debug.LogWarning($"⚠️ الجزيئات عم تقل! {framesDecreasing} فريم متتالي | الناجين={survivors}");
                
            if (survivors == 0)
                Debug.LogError("💀 كل الجزيئات هربت!");
                
            lastSurvivors = survivors;
        }
    }
}