using System.Collections.Generic;
using System.Linq;
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
        public override string QualifiedName => "com.sebanne.afk-changer";

        protected override void Configure()
        {
            // Pass 1: Generating — MA component generation (menu + parameters)
            InPhase(BuildPhase.Generating)
                .Run("Generate AFK Menu", ctx =>
                {
                    var component = ctx.AvatarRootObject.GetComponent<AfkManagerComponent>();
                    if (component == null) return;

                    var descriptor = ctx.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
                    if (descriptor == null) return;

                    var avatarController = FindLayerController(descriptor, VRCAvatarDescriptor.AnimLayerType.Action);
                    var effectiveSlots = EffectiveSlot.Build(
                        component.originalAfkOrder, component.actionSources, avatarController);

                    if (effectiveSlots.Count < 2) return;

#if HAS_MODULAR_AVATAR
                    AfkMenuGenerator.Generate(
                        ctx.AvatarRootObject,
                        effectiveSlots,
                        component.menuInstallTarget,
                        component.originalAfkMenuName);
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
                if (component.originalAfkOrder == -1 || component.actionSources.Count > 0)
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

            var effectiveSlots = EffectiveSlot.Build(
                component.originalAfkOrder, component.actionSources, actionController);

            var targetScan = AfkStateScanner.Scan(actionController);
            var ctx = AfkOperationContext.ForAction(actionController, targetScan);

            // Count == 0: Delete only (original removed, no added sources)
            if (effectiveSlots.Count == 0)
            {
                if (targetScan.HasAfkStates)
                    AfkOperationEngine.Delete(ctx);
                else
                    AfkLog.Info("No existing AFK states to delete in Action layer.");
                return;
            }

            // Count == 1 && IsOriginal: no-op (keep original as-is)
            if (effectiveSlots.Count == 1 && effectiveSlots[0].IsOriginal)
                return;

            // Count == 1 && !IsOriginal: Replace (single source, original removed)
            if (effectiveSlots.Count == 1)
            {
                var slot = effectiveSlots[0];
                AfkOperationEngine.Replace(ctx, slot.Scan, slot.Controller);
                return;
            }

            // Count >= 2: multi-slot mode
            var needsDelete = !effectiveSlots.Any(s => s.IsOriginal);
            if (needsDelete)
                AfkOperationEngine.Delete(ctx);

            AfkOperationEngine.EnsureSlotParameter(actionController, 1);

            var blendOut = ctx.NeedsBlendOut
                ? AfkOperationEngine.CreateSharedBlendOut(ctx)
                : null;

            for (var i = 0; i < effectiveSlots.Count; i++)
            {
                var slot = effectiveSlots[i];
                var slotValue = i + 1;
                var isFallback = (i == 0);

                if (slot.IsOriginal)
                    AfkOperationEngine.AddSlotConditionToExistingEntries(ctx, slotValue, isFallback);
                else
                    AfkOperationEngine.Add(ctx, slot.Scan, slot.Controller, slotValue, blendOut, isFallback);
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
