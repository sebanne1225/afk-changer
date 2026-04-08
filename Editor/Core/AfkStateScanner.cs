using System.Collections.Generic;
using UnityEditor.Animations;
using UnityEngine;

namespace Sebanne.AfkChanger.Editor.Core
{
    internal static class AfkStateScanner
    {
        private const string AfkParameterName = "AFK";

        internal static AfkScanResult Scan(AnimatorController controller)
        {
            var result = new AfkScanResult();

            if (controller == null || controller.layers.Length == 0)
                return result;

            if (!HasAfkParameter(controller))
            {
                AfkLog.Info($"Controller '{controller.name}' has no AFK parameter.");
                return result;
            }

            var rootSm = controller.layers[0].stateMachine;

            // Build ownership map for all states in the entire hierarchy
            var ownership = new Dictionary<AnimatorState, AnimatorStateMachine>();
            CollectAllStates(rootSm, ownership);

            // Step 1: Find AFK==true transitions (entry candidates)
            var entryQueue = new Queue<AnimatorState>();

            var allSms = new List<AnimatorStateMachine>();
            CollectAllStateMachines(rootSm, allSms);

            // AnyState transitions from all SMs
            foreach (var sm in allSms)
            {
                foreach (var t in sm.anyStateTransitions)
                {
                    var destStates = ResolveDestination(t);
                    foreach (var dest in destStates)
                    {
                        if (!HasAfkTrueCondition(t)) continue;

                        result.EntryTransitions.Add(
                            new AfkTransitionInfo(t, null, dest, true));

                        if (!result.AfkStates.Contains(dest))
                            entryQueue.Enqueue(dest);
                    }
                }
            }

            // Per-state transitions from all states
            foreach (var kvp in ownership)
            {
                var state = kvp.Key;
                foreach (var t in state.transitions)
                {
                    var destStates = ResolveDestination(t);
                    foreach (var dest in destStates)
                    {
                        if (!HasAfkTrueCondition(t)) continue;

                        result.EntryTransitions.Add(
                            new AfkTransitionInfo(t, state, dest, false));

                        if (!result.AfkStates.Contains(dest))
                            entryQueue.Enqueue(dest);
                    }
                }
            }

            // Step 2: BFS to find all AFK states
            while (entryQueue.Count > 0)
            {
                var state = entryQueue.Dequeue();
                if (!result.AfkStates.Add(state))
                    continue;

                if (ownership.TryGetValue(state, out var parentSm))
                    result.StateOwnership[state] = parentSm;

                foreach (var t in state.transitions)
                {
                    if (t.isExit)
                    {
                        result.ExitTransitions.Add(
                            new AfkTransitionInfo(t, state, null, false));
                        continue;
                    }

                    if (t.destinationStateMachine != null)
                    {
                        var subEntryStates = ResolveSubStateMachineEntry(t.destinationStateMachine);
                        foreach (var subEntry in subEntryStates)
                        {
                            if (HasAfkFalseCondition(t))
                            {
                                result.ExitTransitions.Add(
                                    new AfkTransitionInfo(t, state, subEntry, false));
                            }
                            else if (!result.AfkStates.Contains(subEntry))
                            {
                                entryQueue.Enqueue(subEntry);
                            }
                        }
                        continue;
                    }

                    if (t.destinationState == null) continue;

                    if (HasAfkFalseCondition(t))
                    {
                        result.ExitTransitions.Add(
                            new AfkTransitionInfo(t, state, t.destinationState, false));
                        continue;
                    }

                    if (!result.AfkStates.Contains(t.destinationState))
                        entryQueue.Enqueue(t.destinationState);
                }
            }

            // Step 2.5: Content / Skeleton classification
            ClassifyContentAndSkeleton(result, rootSm, ownership);

            // Step 3: Determine primary entry state (from content states)
            DetermineEntryState(result);

            if (result.HasAfkStates)
            {
                var contentCount = result.ContentStates.Count;
                var skeletonCount = result.AfkStates.Count - contentCount;
                AfkLog.Info($"Scanned '{controller.name}': {contentCount} content + {skeletonCount} skeleton AFK state(s), " +
                            $"{result.EntryTransitions.Count} entry, {result.ExitTransitions.Count} exit, " +
                            $"subSM={result.HasSubStateMachineContent}.");
            }

            return result;
        }

        private static void ClassifyContentAndSkeleton(
            AfkScanResult result,
            AnimatorStateMachine rootSm,
            Dictionary<AnimatorState, AnimatorStateMachine> ownership)
        {
            // Find SubStateMachines that contain any BFS-detected AFK state
            var afkSubSms = new HashSet<AnimatorStateMachine>();
            foreach (var state in result.AfkStates)
            {
                if (!ownership.TryGetValue(state, out var parentSm)) continue;
                if (parentSm != rootSm)
                    afkSubSms.Add(parentSm);
            }

            if (afkSubSms.Count > 0)
            {
                // SubSM pattern: content = ALL states in AFK SubSMs
                result.HasSubStateMachineContent = true;

                foreach (var subSm in afkSubSms)
                {
                    result.ContentSubStateMachines.Add(subSm);
                    foreach (var cs in subSm.states)
                        result.ContentStates.Add(cs.state);
                }

                // Collect boundary transitions
                // Skeleton → Content: root SM states that transition to content states
                foreach (var state in result.AfkStates)
                {
                    if (!ownership.TryGetValue(state, out var parentSm)) continue;
                    if (parentSm != rootSm) continue; // only skeleton states

                    foreach (var t in state.transitions)
                    {
                        if (t.destinationState != null && result.ContentStates.Contains(t.destinationState))
                        {
                            result.SkeletonToContentTransitions.Add(
                                new AfkTransitionInfo(t, state, t.destinationState, false));
                        }
                    }
                }

                // Content → Skeleton: content states that transition to root SM states
                foreach (var state in result.ContentStates)
                {
                    foreach (var t in state.transitions)
                    {
                        if (t.destinationState == null) continue;
                        if (result.ContentStates.Contains(t.destinationState)) continue;

                        // Destination is outside content → skeleton or external
                        if (ownership.TryGetValue(t.destinationState, out var destParent) && destParent == rootSm)
                        {
                            result.ContentToSkeletonTransitions.Add(
                                new AfkTransitionInfo(t, state, t.destinationState, false));
                        }
                    }
                }

                AfkLog.Info($"SubSM pattern: {result.ContentSubStateMachines.Count} AFK SubSM(s), " +
                            $"{result.ContentStates.Count} content state(s), " +
                            $"{result.SkeletonToContentTransitions.Count} skeleton→content, " +
                            $"{result.ContentToSkeletonTransitions.Count} content→skeleton.");
            }
            else
            {
                // Flat pattern: all AFK states are content
                result.HasSubStateMachineContent = false;
                foreach (var state in result.AfkStates)
                    result.ContentStates.Add(state);
            }
        }

        private static void DetermineEntryState(AfkScanResult result)
        {
            if (result.HasSubStateMachineContent && result.SkeletonToContentTransitions.Count > 0)
            {
                // Use the first skeleton→content transition's destination as entry
                result.EntryState = result.SkeletonToContentTransitions[0].DestinationState;
                return;
            }

            if (result.EntryTransitions.Count > 0)
            {
                // Prefer AnyState entry targeting content
                foreach (var entry in result.EntryTransitions)
                {
                    if (entry.IsFromAnyState && result.ContentStates.Contains(entry.DestinationState))
                    {
                        result.EntryState = entry.DestinationState;
                        return;
                    }
                }

                // Fallback to first entry targeting content
                foreach (var entry in result.EntryTransitions)
                {
                    if (result.ContentStates.Contains(entry.DestinationState))
                    {
                        result.EntryState = entry.DestinationState;
                        return;
                    }
                }
            }
        }

        // --- Helpers ---

        private static void CollectAllStates(
            AnimatorStateMachine sm,
            Dictionary<AnimatorState, AnimatorStateMachine> ownership)
        {
            foreach (var cs in sm.states)
                ownership[cs.state] = sm;

            foreach (var childSm in sm.stateMachines)
                CollectAllStates(childSm.stateMachine, ownership);
        }

        private static void CollectAllStateMachines(
            AnimatorStateMachine sm,
            List<AnimatorStateMachine> result)
        {
            result.Add(sm);
            foreach (var childSm in sm.stateMachines)
                CollectAllStateMachines(childSm.stateMachine, result);
        }

        private static List<AnimatorState> ResolveDestination(AnimatorStateTransition t)
        {
            var result = new List<AnimatorState>();

            if (t.destinationState != null)
                result.Add(t.destinationState);
            else if (t.destinationStateMachine != null)
                result.AddRange(ResolveSubStateMachineEntry(t.destinationStateMachine));

            return result;
        }

        private static List<AnimatorState> ResolveSubStateMachineEntry(AnimatorStateMachine subSm)
        {
            var result = new List<AnimatorState>();

            foreach (var et in subSm.entryTransitions)
            {
                if (et.destinationState != null)
                    result.Add(et.destinationState);
            }

            if (result.Count == 0 && subSm.defaultState != null)
                result.Add(subSm.defaultState);

            return result;
        }

        private static bool HasAfkParameter(AnimatorController controller)
        {
            foreach (var p in controller.parameters)
            {
                if (p.name == AfkParameterName && p.type == AnimatorControllerParameterType.Bool)
                    return true;
            }
            return false;
        }

        private static bool HasAfkTrueCondition(AnimatorStateTransition transition)
        {
            foreach (var c in transition.conditions)
            {
                if (c.parameter == AfkParameterName && c.mode == AnimatorConditionMode.If)
                    return true;
            }
            return false;
        }

        private static bool HasAfkFalseCondition(AnimatorStateTransition transition)
        {
            foreach (var c in transition.conditions)
            {
                if (c.parameter == AfkParameterName && c.mode == AnimatorConditionMode.IfNot)
                    return true;
            }
            return false;
        }
    }
}
