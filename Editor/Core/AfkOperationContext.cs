using UnityEditor.Animations;
using VRC.SDK3.Avatars.Components;

namespace Sebanne.AfkManager.Editor.Core
{
    internal sealed class AfkOperationContext
    {
        internal AnimatorController Controller { get; }
        internal AnimatorStateMachine RootStateMachine { get; }
        internal AfkScanResult TargetScan { get; }
        internal bool NeedsBlendOut { get; }
        internal bool NeedsBehaviours { get; }
        internal float EntryBlendDuration { get; }

        private AfkOperationContext(
            AnimatorController controller,
            AnimatorStateMachine rootStateMachine,
            AfkScanResult targetScan,
            bool needsBlendOut,
            bool needsBehaviours,
            float entryBlendDuration)
        {
            Controller = controller;
            RootStateMachine = rootStateMachine;
            TargetScan = targetScan;
            NeedsBlendOut = needsBlendOut;
            NeedsBehaviours = needsBehaviours;
            EntryBlendDuration = entryBlendDuration;
        }

        internal static AfkOperationContext ForAction(AnimatorController controller, AfkScanResult scan)
        {
            return new AfkOperationContext(
                controller,
                controller.layers[0].stateMachine,
                scan,
                needsBlendOut: true,
                needsBehaviours: true,
                entryBlendDuration: ExtractEntryBlendDuration(scan));
        }

        internal static AfkOperationContext ForFxLayer(
            AnimatorController controller, int layerIndex, AfkScanResult scan)
        {
            return new AfkOperationContext(
                controller,
                controller.layers[layerIndex].stateMachine,
                scan,
                needsBlendOut: false,
                needsBehaviours: false,
                entryBlendDuration: 0f);
        }

        private static float ExtractEntryBlendDuration(AfkScanResult scan)
        {
            if (scan?.EntryState == null) return 1f;

            foreach (var b in scan.EntryState.behaviours)
            {
                if (b is VRCPlayableLayerControl plc)
                    return plc.blendDuration;
            }

            return 1f;
        }
    }
}
