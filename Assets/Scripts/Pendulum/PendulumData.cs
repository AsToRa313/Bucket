using UnityEngine;

[CreateAssetMenu(fileName = "PendulumData", menuName = "Simulation/PendulumData")]
public class PendulumData : ScriptableObject
{
    public float totalMass;
    public float currentPaintMass;
    public float effectiveLength;
    public float fillRatio;
    public float theta;
    public float phi;
    public float angularVelocityTheta;
    public float angularVelocityPhi;
}