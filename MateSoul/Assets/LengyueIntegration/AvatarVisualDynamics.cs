using System;
using MateSoul.Runtime;
using MateSoul.Wardrobe;
using UnityEngine;
using VRM;

namespace LengyueIntegration
{
    // Evaluate secondaries after game contact rigs and social attention have finished.
    [DefaultExecutionOrder(14000)]
    public sealed class AvatarVisualDynamics : MonoBehaviour
    {
        [SerializeField] StudySkirtPose skirt;
        [SerializeField] VRMSpringBone[] hairSprings = Array.Empty<VRMSpringBone>();
        [SerializeField] VRMSpringBone[] outfitSprings = Array.Empty<VRMSpringBone>();
        [SerializeField] SkinnedMeshRenderer face, lashes;
        [SerializeField] LengyueWardrobeController wardrobe;
        [SerializeField] AvatarSeatingRig seating;
        [SerializeField] SkinnedMeshRenderer[] garments;
        int[] lashIndices;
        int[] seatedShapes;

        public void Configure(StudySkirtPose support, VRMSpringBone[] hair,
            VRMSpringBone[] cloth, SkinnedMeshRenderer faceMesh, SkinnedMeshRenderer lashMesh,
            LengyueWardrobeController outfits, AvatarSeatingRig seatRig, SkinnedMeshRenderer[] clothMeshes)
        {
            skirt=support; hairSprings=hair; outfitSprings=cloth;
            face=faceMesh; lashes=lashMesh; wardrobe=outfits;
            seating=seatRig; garments=clothMeshes;
        }

        void Awake()
        {
            if(!face || !lashes || !face.sharedMesh || !lashes.sharedMesh)
                throw new InvalidOperationException("Face and eyelash morph bindings are required.");
            seatedShapes=new int[garments.Length];
            for(int i=0;i<garments.Length;i++)
            {
                seatedShapes[i]=garments[i].sharedMesh.GetBlendShapeIndex("Game_SeatedThighClearance");
                if(seatedShapes[i]<0) throw new InvalidOperationException("Seated skirt corrective is required.");
            }
            lashIndices=new int[face.sharedMesh.blendShapeCount];
            for(int i=0;i<lashIndices.Length;i++)
            {
                lashIndices[i]=lashes.sharedMesh.GetBlendShapeIndex(face.sharedMesh.GetBlendShapeName(i));
                if(lashIndices[i]<0)
                    throw new InvalidOperationException("Eyelash shape missing: "+face.sharedMesh.GetBlendShapeName(i));
            }
        }

        void LateUpdate()
        {
            bool initial=!wardrobe || wardrobe.CurrentOutfitId==wardrobe.InitialOutfitId;
            if(initial && skirt) skirt.Evaluate();
            foreach(var spring in hairSprings) spring.ManualUpdate(Time.deltaTime);
            if(initial)
                foreach(var spring in outfitSprings) spring.ManualUpdate(Time.deltaTime);
            for(int i=0;i<garments.Length;i++)
                garments[i].SetBlendShapeWeight(seatedShapes[i],initial?seating.Weight*100:0);
            for(int i=0;i<lashIndices.Length;i++)
                lashes.SetBlendShapeWeight(lashIndices[i],face.GetBlendShapeWeight(i));
        }
    }
}
