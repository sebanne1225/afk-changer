using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;

namespace Sebanne.AfkChanger.Editor.Core
{
    internal static class AfkFxProcessor
    {
        internal static void Clean(
            AnimatorController fxController,
            List<AfkFxLayerScanResult> layerScans)
        {
            foreach (var layerScan in layerScans)
            {
                var sm = fxController.layers[layerScan.LayerIndex].stateMachine;
                CleanLayer(sm, layerScan.ScanResult);
                AfkLog.Info($"Cleaned FX layer '{layerScan.LayerName}': " +
                            $"removed {layerScan.ScanResult.AfkStates.Count} AFK state(s).");
            }
        }

        private static void CleanLayer(AnimatorStateMachine rootSm, AfkScanResult scan)
        {
            if (scan == null || !scan.HasAfkStates)
                return;

            var allSms = new List<AnimatorStateMachine>();
            CollectAllStateMachines(rootSm, allSms);

            // Remove AnyState transitions targeting AFK states
            foreach (var sm in allSms)
            {
                var filtered = sm.anyStateTransitions
                    .Where(t => t.destinationState == null || !scan.AfkStates.Contains(t.destinationState))
                    .ToArray();
                if (filtered.Length != sm.anyStateTransitions.Length)
                    sm.anyStateTransitions = filtered;
            }

            // Remove per-state transitions targeting AFK states
            foreach (var sm in allSms)
            {
                foreach (var cs in sm.states)
                {
                    if (scan.AfkStates.Contains(cs.state)) continue;

                    var filtered = cs.state.transitions
                        .Where(t => t.destinationState == null || !scan.AfkStates.Contains(t.destinationState))
                        .ToArray();
                    if (filtered.Length != cs.state.transitions.Length)
                        cs.state.transitions = filtered;
                }
            }

            // Remove AFK states themselves
            foreach (var afkState in scan.AfkStates)
            {
                if (scan.StateOwnership.TryGetValue(afkState, out var parentSm))
                    parentSm.RemoveState(afkState);
                else
                    rootSm.RemoveState(afkState);
            }
        }

        private static void CollectAllStateMachines(
            AnimatorStateMachine sm,
            List<AnimatorStateMachine> result)
        {
            result.Add(sm);
            foreach (var childSm in sm.stateMachines)
                CollectAllStateMachines(childSm.stateMachine, result);
        }
    }
}
