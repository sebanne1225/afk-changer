using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.AfkChanger
{
    public enum AfkFxMode
    {
        None,
        Clean,
    }

    [AddComponentMenu("Sebanne/AFK Changer")]
    [RequireComponent(typeof(VRCAvatarDescriptor))]
    public sealed class AfkChangerComponent : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        [SerializeField]
        private RuntimeAnimatorController _sourceController;

        [SerializeField]
        private AfkFxMode _fxMode = AfkFxMode.None;

        public RuntimeAnimatorController SourceController => _sourceController;
        public AfkFxMode FxMode => _fxMode;
    }
}
