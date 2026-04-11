using nadena.dev.ndmf;
using Sebanne.AfkChanger;
using Sebanne.AfkChanger.Editor.Core;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(Sebanne.AfkChanger.Editor.AfkChangerPlugin))]

namespace Sebanne.AfkChanger.Editor
{
    public sealed class AfkChangerPlugin : Plugin<AfkChangerPlugin>
    {
        public override string DisplayName => "AFK Changer";
        public override string QualifiedName => "com.sebanne.afk-changer";

        protected override void Configure()
        {
            InPhase(BuildPhase.Generating)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Replace AFK states", ctx =>
                {
                    var component = ctx.AvatarRootObject.GetComponent<AfkChangerComponent>();
                    if (component == null)
                        return;

                    try
                    {
                        var sourceController = component.SourceController as AnimatorController;
                        if (sourceController == null)
                        {
                            AfkLog.Warn("Source controller is not set or not an AnimatorController. Skipping.");
                            return;
                        }

                        var descriptor = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
                        if (descriptor == null)
                        {
                            AfkLog.Error("VRCAvatarDescriptor not found.");
                            return;
                        }

                        var actionController = FindActionController(descriptor);
                        if (actionController == null)
                        {
                            AfkLog.Warn("Action layer controller not found or not set. Skipping.");
                            return;
                        }

                        var targetScan = AfkStateScanner.Scan(actionController);
                        var sourceScan = AfkStateScanner.Scan(sourceController);

                        if (!sourceScan.HasAfkStates)
                        {
                            AfkLog.Error("No AFK states found in the source controller. " +
                                         "Make sure the controller has transitions using the 'AFK' parameter.");
                            return;
                        }

                        if (!targetScan.HasAfkStates)
                            AfkLog.Info("No existing AFK states in the Action controller. " +
                                        "Source AFK states will be added.");

                        AfkStateReplacer.Replace(actionController, targetScan, sourceScan, sourceController);
                    }
                    finally
                    {
                        Object.DestroyImmediate(component);
                    }
                });
        }

        private static AnimatorController FindActionController(VRCAvatarDescriptor descriptor)
        {
            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type != VRCAvatarDescriptor.AnimLayerType.Action)
                    continue;

                if (layer.isDefault || layer.animatorController == null)
                    return null;

                return layer.animatorController as AnimatorController;
            }

            return null;
        }
    }
}
