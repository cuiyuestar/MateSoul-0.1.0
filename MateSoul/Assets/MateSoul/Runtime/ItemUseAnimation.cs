using UnityEngine;

namespace MateSoul.Runtime
{
    [CreateAssetMenu(menuName = "MateSoul/Held item use animation")]
    public sealed class ItemUseAnimation : ScriptableObject
    {
        public const string LayerName = "ItemUse";
        public AnimationClip clip;
        public string actionLabel;
        public string StateName => LayerName + "." + clip.name;
    }
}
