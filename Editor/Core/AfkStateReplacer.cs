using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;

namespace Sebanne.AfkChanger.Editor.Core
{
    internal static class AfkStateReplacer
    {
        internal static bool Replace(
            AnimatorController targetController,
            AfkScanResult targetScan,
            AfkScanResult sourceScan,
            AnimatorController sourceController)
        {
            if (!sourceScan.HasAfkStates)
            {
                AfkLog.Error("Source controller has no AFK states.");
                return false;
            }

            if (sourceScan.ContentStates.Count == 0)
            {
                AfkLog.Error("Source controller has no AFK content states.");
                return false;
            }

            var targetSm = targetController.layers[0].stateMachine;

            if (targetScan.HasSubStateMachineContent && sourceScan.HasSubStateMachineContent)
                return ReplaceSubSm(targetController, targetSm, targetScan, sourceScan, sourceController);

            // Flat pattern fallback
            return ReplaceFlat(targetController, targetSm, targetScan, sourceScan, sourceController);
        }

        // =====================================================================
        // SubStateMachine pattern: replace content only, preserve skeleton
        // =====================================================================

        private static bool ReplaceSubSm(
            AnimatorController targetController,
            AnimatorStateMachine targetSm,
            AfkScanResult targetScan,
            AfkScanResult sourceScan,
            AnimatorController sourceController)
        {
            // Step 1: Remember exit targets from target's content→skeleton transitions
            var exitTargets = new Dictionary<string, AnimatorState>();
            foreach (var ct in targetScan.ContentToSkeletonTransitions)
            {
                if (ct.DestinationState != null)
                    exitTargets[ct.DestinationState.name] = ct.DestinationState;
            }

            // Step 2: Remove content SubStateMachines
            RemoveContentSubStateMachines(targetSm, targetScan);

            // Step 3: Clean up skeleton states' dangling transitions to deleted content
            CleanupSkeletonTransitions(targetSm, targetScan);

            // Step 4: Copy source content states into target root SM
            var sourceSm = sourceController.layers[0].stateMachine;
            var mapping = CopyContentStates(targetSm, sourceScan, sourceSm);

            // Step 5: Copy internal transitions between content states
            CopyInternalTransitions(sourceScan.ContentStates, mapping);

            // Step 6: Reconnect skeleton → content entry
            ReconnectSubSmEntry(targetScan, sourceScan, mapping);

            // Step 7: Reconnect content → skeleton exit
            ReconnectSubSmExit(sourceScan, mapping, exitTargets);

            // Step 8: Add missing parameters
            CopyMissingParameters(targetController, sourceController);

            var entryName = sourceScan.EntryState != null && mapping.ContainsKey(sourceScan.EntryState)
                ? mapping[sourceScan.EntryState].name
                : "(unknown)";
            AfkLog.Info($"Replaced AFK content (SubSM). Copied {mapping.Count} state(s). Entry: {entryName}");

            return true;
        }

        private static void RemoveContentSubStateMachines(
            AnimatorStateMachine rootSm,
            AfkScanResult targetScan)
        {
            foreach (var subSm in targetScan.ContentSubStateMachines)
            {
                AfkLog.Info($"Removing AFK SubStateMachine: {subSm.name}");
                rootSm.RemoveStateMachine(subSm);
            }

            // Clean up AnyState transitions that targeted content states or removed SubSMs
            var filtered = rootSm.anyStateTransitions
                .Where(t =>
                {
                    if (t.destinationState != null && targetScan.ContentStates.Contains(t.destinationState))
                        return false;
                    if (t.destinationStateMachine != null && targetScan.ContentSubStateMachines.Contains(t.destinationStateMachine))
                        return false;
                    return true;
                })
                .ToArray();
            if (filtered.Length != rootSm.anyStateTransitions.Length)
                rootSm.anyStateTransitions = filtered;
        }

        private static void CleanupSkeletonTransitions(
            AnimatorStateMachine rootSm,
            AfkScanResult targetScan)
        {
            // Remove only transitions from skeleton states to content states
            foreach (var cs in rootSm.states)
            {
                var hasContentTransition = false;
                foreach (var t in cs.state.transitions)
                {
                    if (t.destinationState != null && targetScan.ContentStates.Contains(t.destinationState))
                    {
                        hasContentTransition = true;
                        break;
                    }
                }

                if (!hasContentTransition) continue;

                var filtered = cs.state.transitions
                    .Where(t => t.destinationState == null || !targetScan.ContentStates.Contains(t.destinationState))
                    .ToArray();
                cs.state.transitions = filtered;
            }
        }

        private static void ReconnectSubSmEntry(
            AfkScanResult targetScan,
            AfkScanResult sourceScan,
            Dictionary<AnimatorState, AnimatorState> mapping)
        {
            // Source's skeleton→content transitions define the entry pattern
            foreach (var entry in sourceScan.SkeletonToContentTransitions)
            {
                if (entry.DestinationState == null) continue;
                if (!mapping.ContainsKey(entry.DestinationState)) continue;

                var newDest = mapping[entry.DestinationState];

                // Find the corresponding skeleton state in the TARGET
                // by matching name with the source's skeleton state
                AnimatorState targetSkeletonState = null;
                foreach (var targetEntry in targetScan.SkeletonToContentTransitions)
                {
                    if (targetEntry.SourceState != null &&
                        targetEntry.SourceState.name == entry.SourceState.name)
                    {
                        targetSkeletonState = targetEntry.SourceState;
                        break;
                    }
                }

                if (targetSkeletonState == null)
                {
                    AfkLog.Warn($"Could not find target skeleton state matching '{entry.SourceState.name}' for entry reconnection.");
                    continue;
                }

                var newTransition = targetSkeletonState.AddTransition(newDest);
                CopyTransitionSettings(entry.Transition, newTransition);
                AfkLog.Info($"Entry: {targetSkeletonState.name} → {newDest.name}");
            }
        }

        private static void ReconnectSubSmExit(
            AfkScanResult sourceScan,
            Dictionary<AnimatorState, AnimatorState> mapping,
            Dictionary<string, AnimatorState> exitTargets)
        {
            foreach (var exit in sourceScan.ContentToSkeletonTransitions)
            {
                if (exit.SourceState == null || !mapping.ContainsKey(exit.SourceState))
                    continue;
                if (exit.DestinationState == null) continue;

                var newSource = mapping[exit.SourceState];

                // Find the target skeleton state by name
                AnimatorState targetExitDest = null;
                if (exitTargets.TryGetValue(exit.DestinationState.name, out var found))
                    targetExitDest = found;

                if (targetExitDest == null)
                {
                    // Fallback: try first available exit target
                    if (exitTargets.Count > 0)
                    {
                        foreach (var kvp in exitTargets)
                        {
                            targetExitDest = kvp.Value;
                            break;
                        }
                    }
                }

                if (targetExitDest == null)
                {
                    AfkLog.Warn($"No exit target found for content exit from '{exit.SourceState.name}'.");
                    continue;
                }

                var newTransition = newSource.AddTransition(targetExitDest);
                CopyTransitionSettings(exit.Transition, newTransition);
                AfkLog.Info($"Exit: {newSource.name} → {targetExitDest.name}");
            }
        }

        // =====================================================================
        // Flat pattern: replace all AFK states (original behavior)
        // =====================================================================

        private static bool ReplaceFlat(
            AnimatorController targetController,
            AnimatorStateMachine targetSm,
            AfkScanResult targetScan,
            AfkScanResult sourceScan,
            AnimatorController sourceController)
        {
            var exitTarget = FindExitTarget(targetScan, targetSm);

            RemoveAfkStatesFlat(targetSm, targetScan);

            var sourceSm = sourceController.layers[0].stateMachine;
            var mapping = CopyContentStates(targetSm, sourceScan, sourceSm);

            CopyInternalTransitions(sourceScan.ContentStates, mapping);
            ReconnectEntryFlat(targetSm, sourceScan, mapping);
            ReconnectExitFlat(sourceScan, mapping, exitTarget);

            CopyMissingParameters(targetController, sourceController);

            var entryName = sourceScan.EntryState != null && mapping.ContainsKey(sourceScan.EntryState)
                ? mapping[sourceScan.EntryState].name
                : "(unknown)";
            AfkLog.Info($"Replaced AFK states (flat). Copied {mapping.Count} state(s). Entry: {entryName}");

            return true;
        }

        private static AnimatorState FindExitTarget(AfkScanResult targetScan, AnimatorStateMachine sm)
        {
            if (targetScan != null && targetScan.ExitTransitions.Count > 0)
            {
                foreach (var exit in targetScan.ExitTransitions)
                {
                    if (exit.DestinationState != null &&
                        !targetScan.AfkStates.Contains(exit.DestinationState))
                        return exit.DestinationState;
                }
            }
            return sm.defaultState;
        }

        private static void RemoveAfkStatesFlat(AnimatorStateMachine rootSm, AfkScanResult targetScan)
        {
            if (targetScan == null || !targetScan.HasAfkStates)
                return;

            var allSms = new List<AnimatorStateMachine>();
            CollectAllStateMachines(rootSm, allSms);

            foreach (var sm in allSms)
            {
                var filtered = sm.anyStateTransitions
                    .Where(t => t.destinationState == null || !targetScan.AfkStates.Contains(t.destinationState))
                    .ToArray();
                if (filtered.Length != sm.anyStateTransitions.Length)
                    sm.anyStateTransitions = filtered;
            }

            foreach (var sm in allSms)
            {
                foreach (var cs in sm.states)
                {
                    if (targetScan.AfkStates.Contains(cs.state)) continue;

                    var filtered = cs.state.transitions
                        .Where(t => t.destinationState == null || !targetScan.AfkStates.Contains(t.destinationState))
                        .ToArray();
                    if (filtered.Length != cs.state.transitions.Length)
                        cs.state.transitions = filtered;
                }
            }

            foreach (var afkState in targetScan.AfkStates)
            {
                if (targetScan.StateOwnership.TryGetValue(afkState, out var parentSm))
                    parentSm.RemoveState(afkState);
                else
                    rootSm.RemoveState(afkState);
            }
        }

        private static void ReconnectEntryFlat(
            AnimatorStateMachine targetSm,
            AfkScanResult sourceScan,
            Dictionary<AnimatorState, AnimatorState> mapping)
        {
            if (sourceScan.EntryState == null || !mapping.ContainsKey(sourceScan.EntryState))
            {
                AfkLog.Warn("Could not determine source entry state for reconnection.");
                return;
            }

            var newEntry = mapping[sourceScan.EntryState];
            var createdAnyStateEntry = false;

            foreach (var entry in sourceScan.EntryTransitions)
            {
                if (!mapping.ContainsKey(entry.DestinationState)) continue;
                var newDest = mapping[entry.DestinationState];

                if (entry.IsFromAnyState)
                {
                    var t = targetSm.AddAnyStateTransition(newDest);
                    CopyTransitionSettings(entry.Transition, t);
                    t.canTransitionToSelf = false;
                    createdAnyStateEntry = true;
                }
            }

            if (!createdAnyStateEntry)
            {
                var fallback = targetSm.AddAnyStateTransition(newEntry);
                fallback.hasExitTime = false;
                fallback.duration = 0f;
                fallback.hasFixedDuration = true;
                fallback.canTransitionToSelf = false;
                fallback.AddCondition(AnimatorConditionMode.If, 0f, "AFK");
                AfkLog.Info("Created fallback AnyState → AFK entry transition.");
            }
        }

        private static void ReconnectExitFlat(
            AfkScanResult sourceScan,
            Dictionary<AnimatorState, AnimatorState> mapping,
            AnimatorState exitTarget)
        {
            if (exitTarget == null)
            {
                AfkLog.Warn("No exit target found.");
                return;
            }

            foreach (var exit in sourceScan.ExitTransitions)
            {
                if (exit.SourceState == null || !mapping.ContainsKey(exit.SourceState)) continue;

                var newSource = mapping[exit.SourceState];
                var t = newSource.AddTransition(exitTarget);
                CopyTransitionSettings(exit.Transition, t);
            }
        }

        // =====================================================================
        // Shared helpers
        // =====================================================================

        private static Dictionary<AnimatorState, AnimatorState> CopyContentStates(
            AnimatorStateMachine targetSm,
            AfkScanResult sourceScan,
            AnimatorStateMachine sourceSm)
        {
            var mapping = new Dictionary<AnimatorState, AnimatorState>();

            var positionLookup = new Dictionary<AnimatorState, Vector3>();
            CollectAllStatePositions(sourceSm, positionLookup);

            var positionOffset = CalculatePositionOffset(targetSm);

            foreach (var sourceState in sourceScan.ContentStates)
            {
                var sourcePosition = positionLookup.ContainsKey(sourceState)
                    ? positionLookup[sourceState]
                    : new Vector3(200f, 0f, 0f);
                var newState = targetSm.AddState(sourceState.name, sourcePosition + positionOffset);

                newState.motion = sourceState.motion;
                newState.speed = sourceState.speed;
                newState.speedParameter = sourceState.speedParameter;
                newState.speedParameterActive = sourceState.speedParameterActive;
                newState.cycleOffset = sourceState.cycleOffset;
                newState.cycleOffsetParameter = sourceState.cycleOffsetParameter;
                newState.cycleOffsetParameterActive = sourceState.cycleOffsetParameterActive;
                newState.iKOnFeet = sourceState.iKOnFeet;
                newState.writeDefaultValues = sourceState.writeDefaultValues;
                newState.mirror = sourceState.mirror;
                newState.mirrorParameter = sourceState.mirrorParameter;
                newState.mirrorParameterActive = sourceState.mirrorParameterActive;
                newState.tag = sourceState.tag;

                if (sourceState.behaviours != null && sourceState.behaviours.Length > 0)
                {
                    var cloned = new StateMachineBehaviour[sourceState.behaviours.Length];
                    for (var i = 0; i < sourceState.behaviours.Length; i++)
                        cloned[i] = Object.Instantiate(sourceState.behaviours[i]);
                    newState.behaviours = cloned;
                }

                mapping[sourceState] = newState;
            }

            return mapping;
        }

        private static void CopyInternalTransitions(
            HashSet<AnimatorState> contentStates,
            Dictionary<AnimatorState, AnimatorState> mapping)
        {
            foreach (var sourceState in contentStates)
            {
                if (!mapping.ContainsKey(sourceState)) continue;
                var newSource = mapping[sourceState];

                foreach (var t in sourceState.transitions)
                {
                    if (t.destinationState == null) continue;
                    if (!contentStates.Contains(t.destinationState)) continue;
                    if (!mapping.ContainsKey(t.destinationState)) continue;

                    var newDest = mapping[t.destinationState];
                    var newT = newSource.AddTransition(newDest);
                    CopyTransitionSettings(t, newT);
                }
            }
        }

        private static void CopyMissingParameters(
            AnimatorController target,
            AnimatorController source)
        {
            var existing = new HashSet<string>();
            foreach (var p in target.parameters)
                existing.Add(p.name);

            foreach (var p in source.parameters)
            {
                if (existing.Contains(p.name)) continue;

                target.AddParameter(p.name, p.type);

                var targetParams = target.parameters;
                for (var i = 0; i < targetParams.Length; i++)
                {
                    if (targetParams[i].name != p.name) continue;
                    targetParams[i].defaultFloat = p.defaultFloat;
                    targetParams[i].defaultInt = p.defaultInt;
                    targetParams[i].defaultBool = p.defaultBool;
                    break;
                }
                target.parameters = targetParams;
            }
        }

        private static void CopyTransitionSettings(
            AnimatorStateTransition source,
            AnimatorStateTransition dest)
        {
            dest.hasExitTime = source.hasExitTime;
            dest.exitTime = source.exitTime;
            dest.duration = source.duration;
            dest.offset = source.offset;
            dest.hasFixedDuration = source.hasFixedDuration;
            dest.interruptionSource = source.interruptionSource;
            dest.orderedInterruption = source.orderedInterruption;
            dest.canTransitionToSelf = source.canTransitionToSelf;

            foreach (var c in source.conditions)
                dest.AddCondition(c.mode, c.threshold, c.parameter);
        }

        private static Vector3 CalculatePositionOffset(AnimatorStateMachine sm)
        {
            var maxY = 0f;
            foreach (var cs in sm.states)
            {
                if (cs.position.y > maxY)
                    maxY = cs.position.y;
            }
            foreach (var csm in sm.stateMachines)
            {
                if (csm.position.y > maxY)
                    maxY = csm.position.y;
            }
            return new Vector3(0f, maxY + 100f, 0f);
        }

        private static void CollectAllStateMachines(
            AnimatorStateMachine sm,
            List<AnimatorStateMachine> result)
        {
            result.Add(sm);
            foreach (var childSm in sm.stateMachines)
                CollectAllStateMachines(childSm.stateMachine, result);
        }

        private static void CollectAllStatePositions(
            AnimatorStateMachine sm,
            Dictionary<AnimatorState, Vector3> positions)
        {
            foreach (var cs in sm.states)
                positions[cs.state] = cs.position;

            foreach (var childSm in sm.stateMachines)
                CollectAllStatePositions(childSm.stateMachine, positions);
        }
    }
}
