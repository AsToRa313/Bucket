using UnityEngine;

[CreateAssetMenu(fileName = "NewPendulumData", menuName = "VR Project/Pendulum Data")]
public class PendulumData : ScriptableObject
{
    [Header("Mass & Geometry (مبرمج 1)")]
    public float totalMass;           // الكتلة الكلية m(t)
    public float currentPaintMass;    // كتلة الطلاء اللحظية
    public float effectiveLength;     // الطول الفعال L_eff
    public float fillRatio;           // نسبة الامتلاء (من 0 إلى 1)

    [Header("Kinematics (مبرمج 1)")]
    public Vector3 currentPosition;   // الموقع الديكارتي (x, y, z)
    public Vector3 linearVelocity;    // السرعة الخطية v_bucket
    public float theta;               // الزاوية القطبية
    public float phi;                 // الزاوية السمتية
    public float angularVelocityTheta; // سرعة تغير ثيتا
    public float angularVelocityPhi;   // سرعة تغير فاي

    [Header("Environment & Physics (مبرمج 1)")]
    public Vector3 gravityVector;     // متجه الجاذبية
    public float airDamping;          // معامل التخامد الهوائي

    [Header("Outputs for Others (للمبرمج 2 و 3)")]
    public float effectiveGravity;    // الجاذبية الفعالة g_eff
    public Vector3 tangentialAcceleration; // التسارع المماسي

    [Header("Fluid Dynamics Outputs (المبرمج 2 - )")]
    public float fluidExitVelocity;   // سرعة خروج الطلاء الفعلية من الفتحة (تورشيللي الفعلية)
    public float dynamicDrainRate;    // معدل تصريف الطلاء الفعلي (Q) المتأثر باللزوجة والانسداد
    public float currentHoleRadius;   // نصف قطر الفتحة اللحظي (يتناقص مع محاكاة جفاف الحواف والانسداد)
}