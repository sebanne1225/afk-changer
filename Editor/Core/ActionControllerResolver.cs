using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.AfkChanger.Editor.Core
{
    internal static class ActionControllerResolver
    {
        /// <summary>
        /// Attempts to extract the Action Layer AnimatorController from a GameObject
        /// via its VRCAvatarDescriptor.
        /// Returns null on failure with a descriptive error message.
        /// </summary>
        internal static AnimatorController TryResolve(GameObject obj, out string error)
        {
            error = null;

            if (obj == null)
            {
                error = "GameObject が null です。";
                return null;
            }

            var descriptor = obj.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null)
            {
                error = "VRC Avatar Descriptor が見つかりません。";
                return null;
            }

            var actionLayer = descriptor.baseAnimationLayers
                .FirstOrDefault(l => l.type == VRCAvatarDescriptor.AnimLayerType.Action);

            if (actionLayer.animatorController == null)
            {
                error = "Action Layer の AnimatorController が未設定です。";
                return null;
            }

            var controller = actionLayer.animatorController as AnimatorController;
            if (controller == null)
            {
                error = "Action Layer のコントローラーが AnimatorController ではありません。";
                return null;
            }

            return controller;
        }
    }
}
