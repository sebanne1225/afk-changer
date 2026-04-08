using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.AfkChanger
{
    [AddComponentMenu("Sebanne/AFK Changer")]
    [RequireComponent(typeof(VRCAvatarDescriptor))]
    public sealed class AfkChangerComponent : MonoBehaviour
    {
        [SerializeField]
        private RuntimeAnimatorController _sourceController;

        public RuntimeAnimatorController SourceController => _sourceController;
    }
}
