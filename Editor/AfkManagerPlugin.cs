using System.Collections.Generic;
using nadena.dev.ndmf;
using Sebanne.AfkManager;
using Sebanne.AfkManager.Editor.Core;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

[assembly: ExportsPlugin(typeof(Sebanne.AfkManager.Editor.AfkManagerPlugin))]

namespace Sebanne.AfkManager.Editor
{
    public sealed class AfkManagerPlugin : Plugin<AfkManagerPlugin>
    {
        public override string DisplayName => "AFK Manager";
        public override string QualifiedName => "com.sebanne.afk-manager";

        protected override void Configure()
        {
            // Pass 1: Generating — MA component generation (menu + parameters)
            InPhase(BuildPhase.Generating)
                .Run("Generate AFK Menu", ctx =>
                {
                    var component = ctx.AvatarRootObject.GetComponent<AfkManagerComponent>();
                    if (component == null) return;
                    if (!NeedsModularAvatar(component)) return;

#if HAS_MODULAR_AVATAR
                    AfkMenuGenerator.Generate(
                        ctx.AvatarRootObject,
                        component.actionSources,
                        component.removeActionAfk);
#else
                    AfkLog.Error("Modular Avatar is required for multi-slot AFK configuration. " +
                                 "Install Modular Avatar to use this feature.");
#endif
                });

            // Pass 2: Transforming.AfterPlugin("MA") — actual AFK processing
            InPhase(BuildPhase.Transforming)
                .AfterPlugin("nadena.dev.modular-avatar")
                .Run("Process AFK", ctx =>
                {
                    var component = ctx.AvatarRootObject.GetComponent<AfkManagerComponent>();
                    if (component == null) return;

                    try
                    {
                        var descriptor = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
                        if (descriptor == null)
                        {
                            AfkLog.Error("VRCAvatarDescriptor not found.");
                            return;
                        }

                        ProcessAction(component, descriptor);
                        ProcessFx(component, descriptor);
                    }
                    finally
                    {
                        Object.DestroyImmediate(component);
                    }
                });
        }

        // =====================================================================
        // Action processing
        // =====================================================================

        private static void ProcessAction(AfkManagerComponent component, VRCAvatarDescriptor descriptor)
        {
            var actionController = FindLayerController(descriptor, VRCAvatarDescriptor.AnimLayerType.Action);
            if (actionController == null)
            {
                if (component.removeActionAfk || component.actionSources.Count > 0)
                    AfkLog.Warn("Action layer controller not found or not set. Skipping Action processing.");
                return;
            }

            // GoGoLoco / multi-level nested SubSM guard
            if (AfkStateScanner.HasNestedSubStateMachines(actionController.layers[0].stateMachine))
            {
                AfkLog.Error("Multi-level nested SubStateMachine detected in Action controller. " +
                             "AFK Manager does not support this structure (e.g. GoGoLoco). Skipping Action processing.");
                return;
            }

            var targetScan = AfkStateScanner.Scan(actionController);
            var ctx = AfkOperationContext.ForAction(actionController, targetScan);

            // Resolve all sources
            var resolved = ResolveAllSlots(component.actionSources);
            var needsMA = NeedsModularAvatar(component);

            if (component.removeActionAfk && resolved.Count == 1 && !needsMA)
            {
                // Single-slot Replace
                AfkOperationEngine.Replace(ctx, resolved[0].scan, resolved[0].controller);
            }
            else if (component.removeActionAfk && resolved.Count >= 2)
            {
                // Delete all, then Add each slot
                AfkOperationEngine.Delete(ctx);
                AfkOperationEngine.EnsureSlotParameter(actionController);

                var blendOut = ctx.NeedsBlendOut
                    ? AfkOperationEngine.CreateSharedBlendOut(ctx)
                    : null;

                for (var i = 0; i < resolved.Count; i++)
                    AfkOperationEngine.Add(ctx, resolved[i].scan, resolved[i].controller, i + 1, blendOut);
            }
            else if (component.removeActionAfk)
            {
                // Delete only
                if (targetScan.HasAfkStates)
                    AfkOperationEngine.Delete(ctx);
                else
                    AfkLog.Info("No existing AFK states to delete in Action layer.");
            }
            else if (resolved.Count > 0)
            {
                // Add: keep original, add new slots alongside
                AfkOperationEngine.EnsureSlotParameter(actionController);
                AfkOperationEngine.AddSlotConditionToExistingEntries(ctx, 0);

                var blendOut = ctx.NeedsBlendOut
                    ? AfkOperationEngine.CreateSharedBlendOut(ctx)
                    : null;

                for (var i = 0; i < resolved.Count; i++)
                    AfkOperationEngine.Add(ctx, resolved[i].scan, resolved[i].controller, i + 1, blendOut);
            }
        }

        // =====================================================================
        // FX processing
        // =====================================================================

        private static void ProcessFx(AfkManagerComponent component, VRCAvatarDescriptor descriptor)
        {
            if (!component.removeFxAfk) return;

            var fxController = FindLayerController(descriptor, VRCAvatarDescriptor.AnimLayerType.FX);
            if (fxController == null)
            {
                AfkLog.Info("FX layer controller not found or not set. Skipping FX processing.");
                return;
            }

            var fxScans = AfkStateScanner.ScanFxLayers(fxController);
            if (fxScans.Count == 0)
            {
                AfkLog.Info("No AFK states found in FX layer.");
                return;
            }

            foreach (var layerScan in fxScans)
            {
                var ctx = AfkOperationContext.ForFxLayer(fxController, layerScan.LayerIndex, layerScan.ScanResult);
                AfkOperationEngine.Delete(ctx);
                AfkLog.Info($"Cleaned FX layer '{layerScan.LayerName}': " +
                            $"removed {layerScan.ScanResult.AfkStates.Count} AFK state(s).");
            }
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private struct ResolvedSlot
        {
            public AnimatorController controller;
            public AfkScanResult scan;
        }

        private static List<ResolvedSlot> ResolveAllSlots(List<AfkSlot> slots)
        {
            var resolved = new List<ResolvedSlot>();

            for (var i = 0; i < slots.Count; i++)
            {
                var controller = ResolveSlotController(slots[i]);
                if (controller == null) continue;

                var scan = AfkStateScanner.Scan(controller);
                if (!scan.HasAfkStates)
                {
                    AfkLog.Error($"Slot {i}: No AFK states found in source controller. Skipping.");
                    continue;
                }

                resolved.Add(new ResolvedSlot { controller = controller, scan = scan });
            }

            return resolved;
        }

        private static bool NeedsModularAvatar(AfkManagerComponent component)
        {
            var sourceCount = component.actionSources.Count;
            return sourceCount >= 2 || (!component.removeActionAfk && sourceCount >= 1);
        }

        private static AnimatorController ResolveSlotController(AfkSlot slot)
        {
            if (slot.inputType == AfkSourceInputType.AvatarPrefab)
            {
                if (slot.avatarPrefab == null) return null;
                var descriptor = slot.avatarPrefab.GetComponent<VRCAvatarDescriptor>();
                if (descriptor == null) return null;
                return FindLayerController(descriptor, VRCAvatarDescriptor.AnimLayerType.Action);
            }

            return slot.sourceController as AnimatorController;
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
