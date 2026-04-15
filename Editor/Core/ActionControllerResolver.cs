using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.AfkManager.Editor.Core
{
    internal static class ActionControllerResolver
    {
        /// <summary>
        /// Attempts to extract an AnimatorController for the specified layer type
        /// from a GameObject via its VRCAvatarDescriptor.
        /// Returns null on failure with a descriptive error message.
        /// </summary>
        internal static AnimatorController TryResolve(
            GameObject obj,
            VRCAvatarDescriptor.AnimLayerType layerType,
            out string error)
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

            var layer = descriptor.baseAnimationLayers
                .FirstOrDefault(l => l.type == layerType);

            if (layer.animatorController == null)
            {
                error = $"{layerType} Layer の AnimatorController が未設定です。";
                return null;
            }

            var controller = layer.animatorController as AnimatorController;
            if (controller == null)
            {
                error = $"{layerType} Layer のコントローラーが AnimatorController ではありません。";
                return null;
            }

            return controller;
        }
    }
}
