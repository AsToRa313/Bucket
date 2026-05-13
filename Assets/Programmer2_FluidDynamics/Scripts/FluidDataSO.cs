using UnityEngine;

// هذا السطر يسمح لك بإنشاء ملف البيانات من قائمة يمين الماوس في Unity
[CreateAssetMenu(fileName = "NewFluidData", menuName = "Fluid System/Fluid Data")]
public class FluidDataSO : ScriptableObject
{
    [Header("Physical Properties")]
    public string fluidName = "Default Paint";
    public float density = 1200f;         // الكثافة (rho) [cite: 15]
    public float surfaceTension = 0.072f; // التوتر السطحي [cite: 17]
    public float initialViscosity = 0.5f; // اللزوجة الابتدائية 

    [Header("Visual Properties")]
    public Color paintColor = Color.red;  // لون الطلاء (سيستخدمه المبرمج الخامس) [cite: 40]

    [Header("Flow Constants")]
    public float baseDischargeCoeff = 0.62f; // معامل التصريف الأساسي [cite: 16]
}