// using UnityEngine;

// public class SimulationManager : MonoBehaviour
// {
//     [Header("=== المراجع ===")]
//     public SphericalPendulumMath pendulum;
//     public CanvasPainter         canvasPainter;
//     public PaintHoleSystem       holeSystem;
//     public SPHSimulation         sphSimulation;

//     void OnGUI()
//     {
//         GUILayout.BeginArea(new Rect(10, 10, 220, 350));
//         GUILayout.Label("=== التحكم ===");

//         // ---- أزرار البندول ----
//         if (GUILayout.Button("▶ ابدأ"))
//             pendulum.enabled = true;

//         if (GUILayout.Button("⏸ وقّف"))
//             pendulum.enabled = false;

//         GUILayout.Space(5);

//         // ---- أزرار الثقوب ----
//         if (holeSystem != null)
//         {
//             if (GUILayout.Button("🟢 افتح كل الثقوب"))
//                 holeSystem.OpenAll();

//             if (GUILayout.Button("🔴 أقفل كل الثقوب"))
//                 holeSystem.CloseAll();
//         }

//         GUILayout.Space(5);

//         // ---- أزرار اللوحة ----
//         if (GUILayout.Button("💾 احفظ اللوحة"))
//             canvasPainter?.SaveCanvas("my_painting");

//         if (GUILayout.Button("🔄 امسح اللوحة"))
//             canvasPainter?.ClearCanvas();

//         GUILayout.Space(10);

//         // ---- معلومات البندول ----
//         if (pendulum != null)
//         {
//             GUILayout.Label($"سرعة السطل: {pendulum.GetBucketSpeed():F2} m/s");

//             if (pendulum.data != null)
//             {
//                 GUILayout.Label($"كتلة الدهان: {pendulum.data.currentPaintMass:F2}");
//                 GUILayout.Label($"الزاوية: {pendulum.data.theta * Mathf.Rad2Deg:F1}°");
//                 GUILayout.Label($"الامتلاء: {pendulum.data.fillRatio * 100f:F0}%");
//             }
//         }

//         // ---- معلومات الثقوب ----
//         if (holeSystem != null)
//         {
//             GUILayout.Space(5);
//             float fill = holeSystem.GetFillRatio();
//             GUILayout.Label($"الدهان المتبقي: {fill * 100f:F0}%");
//             GUILayout.Label($"الثقوب المفتوحة: {holeSystem.CountOpenHoles()}");
//         }

//         GUILayout.EndArea();
//     }
// }