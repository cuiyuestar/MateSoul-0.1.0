using UnityEngine;

namespace MateSoul.Runtime
{
    [CreateAssetMenu(menuName = "MateSoul/Held item use animation")]
    public sealed class ItemUseAnimation : ScriptableObject
    {
        public const string LayerName = "ItemUse";
        public AnimationClip clip;
        public AudioClip biteSound;
        public string actionLabel;
        // Plays once right after a plain pickup, then the item settles into the normal hold.
        public bool playOnPickup;
        public string StateName => LayerName + "." + clip.name;
    }
}
