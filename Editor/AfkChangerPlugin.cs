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
                        var descriptor = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
                        if (descriptor == null)
                        {
                            AfkLog.Error("VRCAvatarDescriptor not found.");
                            return;
                        }

                        // --- Action Layer replacement ---
                        var sourceController = component.SourceController as AnimatorController;
                        if (sourceController != null)
                        {
                            var actionController = FindActionController(descriptor);
                            if (actionController != null)
                            {
                                var targetScan = AfkStateScanner.Scan(actionController);
                                var sourceScan = AfkStateScanner.Scan(sourceController);

                                if (!sourceScan.HasAfkStates)
                                {
                                    AfkLog.Error("No AFK states found in the source controller. " +
                                                 "Make sure the controller has transitions using the 'AFK' parameter.");
                                }
                                else
                                {
                                    if (!targetScan.HasAfkStates)
                                        AfkLog.Info("No existing AFK states in the Action controller. " +
                                                    "Source AFK states will be added.");

                                    AfkStateReplacer.Replace(actionController, targetScan, sourceScan, sourceController);
                                }
                            }
                            else
                            {
                                AfkLog.Warn("Action layer controller not found or not set. Skipping Action replacement.");
                            }
                        }

                        // --- FX Layer clean ---
                        if (component.FxMode == AfkFxMode.Clean)
                        {
                            var fxController = FindFxController(descriptor);
                            if (fxController != null)
                            {
                                var fxScans = AfkStateScanner.ScanFxLayers(fxController);
                                if (fxScans.Count > 0)
                                    AfkFxProcessor.Clean(fxController, fxScans);
                                else
                                    AfkLog.Info("No AFK states found in FX layer.");
                            }
                            else
                            {
                                AfkLog.Info("FX layer controller not found or not set. Skipping FX clean.");
                            }
                        }
                    }
                    finally
                    {
                        Object.DestroyImmediate(component);
                    }
                });
        }

        private static AnimatorController FindActionController(VRCAvatarDescriptor descriptor)
        {
            return FindLayerController(descriptor, VRCAvatarDescriptor.AnimLayerType.Action);
        }

        private static AnimatorController FindFxController(VRCAvatarDescriptor descriptor)
        {
            return FindLayerController(descriptor, VRCAvatarDescriptor.AnimLayerType.FX);
        }

        private static AnimatorController FindLayerController(
            VRCAvatarDescriptor descriptor,
            VRCAvatarDescriptor.AnimLayerType layerType)
        {
            foreach (var layer in descriptor.baseAnimationLayers)
            {
                if (layer.type != layerType)
                    continue;

                if (layer.isDefault || layer.animatorController == null)
                    return null;

                return layer.animatorController as AnimatorController;
            }

            return null;
        }
    }
}
