using UnityEngine;

// Owns only the inserted support transforms. VRMSpringBone owns their children.
[DefaultExecutionOrder(10000)]
public sealed class StudySkirtPose : MonoBehaviour
{
    public Transform hips, leftThigh, leftKnee, rightThigh, rightKnee;
    public Transform[] supports;
    public void Evaluate()
    {
        Vector3 l=hips.InverseTransformDirection(leftKnee.position-leftThigh.position);
        Vector3 r=hips.InverseTransformDirection(rightKnee.position-rightThigh.position);
        float la=Mathf.Clamp(Mathf.Atan2(l.z,-l.y)*Mathf.Rad2Deg,-65,95);
        float ra=Mathf.Clamp(Mathf.Atan2(r.z,-r.y)*Mathf.Rad2Deg,-65,95);
        for(int i=0;i<supports.Length;i++)
        {
            float theta=i*Mathf.PI*2/supports.Length;
            float side=Mathf.Sin(theta);
            float leftWeight=Mathf.Clamp01(1-side/.8f);
            float rightWeight=Mathf.Clamp01(1+side/.8f);
            float front=Mathf.Max(0,Mathf.Max(la*leftWeight,ra*rightWeight));
            float back=Mathf.Min(0,Mathf.Min(la*leftWeight,ra*rightWeight));
            float cosine=Mathf.Cos(theta);
            // The seated thighs span several front panels. A cosine falloff
            // left the diagonal panels behind the thighs and over-rotated the
            // centre panel past horizontal. Widen support only as flexion rises.
            float seated=Mathf.InverseLerp(35,75,front);
            float frontCoverage=Mathf.Lerp(Mathf.Max(0,cosine),
                Mathf.Clamp01((cosine+.25f)/.65f),seated);
            float lift=front*frontCoverage+back*Mathf.Max(0,-cosine);
            supports[i].localRotation=Quaternion.Euler(-Mathf.Clamp(lift,-70,85),0,0);
        }
    }
    void LateUpdate() => Evaluate();
}
