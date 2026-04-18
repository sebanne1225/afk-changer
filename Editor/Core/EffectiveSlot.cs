using System;
using System.Collections.Generic;
using Sebanne.AfkManager;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.AfkManager.Editor.Core
{
    internal sealed class EffectiveSlot
    {
        internal bool IsOriginal;
        internal AfkSlot Source;
        internal AnimatorController Controller;
        internal AfkScanResult Scan;

        internal static List<EffectiveSlot> Build(
            int originalAfkOrder,
            List<AfkSlot> actionSources,
            AnimatorController avatarController)
        {
            var result = new List<EffectiveSlot>();

            if (actionSources != null)
            {
                for (var i = 0; i < actionSources.Count; i++)
                {
                    var slot = actionSources[i];
                    var controller = ResolveSlotController(slot);
                    if (controller == null) continue;

                    var scan = AfkStateScanner.Scan(controller);
                    if (!scan.HasAfkStates)
                    {
                        AfkLog.Error($"Slot {i}: No AFK states found in source controller. Skipping.");
                        continue;
                    }

                    result.Add(new EffectiveSlot
                    {
                        IsOriginal = false,
                        Source = slot,
                        Controller = controller,
                        Scan = scan,
                    });
                }
            }

            if (originalAfkOrder >= 0)
            {
                if (avatarController == null)
                {
                    AfkLog.Warn("Original AFK requested but avatar Action Controller not found. Skipping.");
                }
                else
                {
                    var clampedOrder = Math.Min(originalAfkOrder, actionSources?.Count ?? 0);
                    clampedOrder = Math.Min(clampedOrder, result.Count);

                    var original = new EffectiveSlot
                    {
                        IsOriginal = true,
                        Source = null,
                        Controller = avatarController,
                        Scan = AfkStateScanner.Scan(avatarController),
                    };
                    result.Insert(clampedOrder, original);
                }
            }

            return result;
        }

        private static AnimatorController ResolveSlotController(AfkSlot slot)
        {
            if (slot.inputType == AfkSourceInputType.AvatarPrefab)
            {
                if (slot.avatarPrefab == null) return null;
                var descriptor = slot.avatarPrefab.GetComponent<VRCAvatarDescriptor>();
                if (descriptor == null) return null;
                foreach (var layer in descriptor.baseAnimationLayers)
                {
                    if (layer.type != VRCAvatarDescriptor.AnimLayerType.Action) continue;
                    if (layer.isDefault || layer.animatorController == null) return null;
                    return layer.animatorController as AnimatorController;
                }
                return null;
            }

            return slot.sourceController as AnimatorController;
        }
    }
}
