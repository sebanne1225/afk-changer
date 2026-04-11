using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

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

            if (targetController.layers.Length == 0)
            {
                AfkLog.Error("Target controller has no layers.");
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
            // Step 1: Remove content SubStateMachines
            RemoveContentSubStateMachines(targetSm, targetScan);

            // Step 2: Clean up skeleton states' dangling transitions
            CleanupSkeletonTransitions(targetSm, targetScan);

            // Step 3: Copy source content states into target root SM
            var sourceSm = sourceController.layers[0].stateMachine;
            var mapping = CopyContentStates(targetSm, sourceScan, sourceSm);

            // Step 4: Copy internal transitions between content states
            CopyInternalTransitions(sourceScan.ContentStates, mapping);

            // Step 5: Reconnect skeleton → content entry
            ReconnectSubSmEntry(targetScan, sourceScan, mapping);

            // Step 6: Attach entry behaviours (TrackingControl + PlayableLayerControl)
            var entryBlendDuration = GetExistingEntryBlendDuration(targetScan);
            AttachEntryBehaviours(sourceScan, mapping, entryBlendDuration);

            // Step 7: Create BlendOut state and reconnect exits through it
            var blendOutState = CreateBlendOutState(targetSm, targetSm.defaultState);
            ReconnectSubSmExitToBlendOut(sourceScan, mapping, blendOutState);

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

        private static void ReconnectSubSmExitToBlendOut(
            AfkScanResult sourceScan,
            Dictionary<AnimatorState, AnimatorState> mapping,
            AnimatorState blendOutState)
        {
            // Find real exits: content state transitions to non-content destinations
            foreach (var srcState in sourceScan.ContentStates)
            {
                if (!mapping.ContainsKey(srcState)) continue;
                var newSource = mapping[srcState];

                foreach (var t in srcState.transitions)
                {
                    if (t.isExit)
                    {
                        var newT = newSource.AddTransition(blendOutState);
                        CopyTransitionSettings(t, newT);
                        AfkLog.Info($"Exit: {newSource.name} → {blendOutState.name} (was isExit)");
                        continue;
                    }

                    if (t.destinationState == null) continue;
                    if (sourceScan.ContentStates.Contains(t.destinationState)) continue;

                    // Destination is outside content → real exit
                    var newExit = newSource.AddTransition(blendOutState);
                    CopyTransitionSettings(t, newExit);
                    AfkLog.Info($"Exit: {newSource.name} → {blendOutState.name}");
                }
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
            var entryBlendDuration = GetExistingEntryBlendDuration(targetScan);

            RemoveAfkStatesFlat(targetSm, targetScan);

            var sourceSm = sourceController.layers[0].stateMachine;
            var mapping = CopyContentStates(targetSm, sourceScan, sourceSm);

            CopyInternalTransitions(sourceScan.ContentStates, mapping);
            ReconnectEntryFlat(targetSm, targetScan, sourceScan, mapping);

            // Create BlendOut state and reconnect exits through it
            var blendOutState = CreateBlendOutState(targetSm, targetSm.defaultState);
            ReconnectExitFlat(sourceScan, mapping, blendOutState);

            // Attach entry behaviours (TrackingControl + PlayableLayerControl)
            AttachEntryBehaviours(sourceScan, mapping, entryBlendDuration);

            CopyMissingParameters(targetController, sourceController);

            var entryName = sourceScan.EntryState != null && mapping.ContainsKey(sourceScan.EntryState)
                ? mapping[sourceScan.EntryState].name
                : "(unknown)";
            AfkLog.Info($"Replaced AFK states (flat). Copied {mapping.Count} state(s). Entry: {entryName}");

            return true;
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
            AfkScanResult targetScan,
            AfkScanResult sourceScan,
            Dictionary<AnimatorState, AnimatorState> mapping)
        {
            if (sourceScan.EntryState == null || !mapping.ContainsKey(sourceScan.EntryState))
            {
                AfkLog.Warn("Could not determine source entry state for reconnection.");
                return;
            }

            var newEntry = mapping[sourceScan.EntryState];
            var created = false;

            // Re-create target's original per-state entry transitions, retargeted to new entry
            foreach (var entry in targetScan.EntryTransitions)
            {
                if (entry.SourceState == null) continue; // AnyState handled below
                // SourceState (e.g. WaitForActionOrAFK) still exists — only AFK states were removed
                var t = entry.SourceState.AddTransition(newEntry);
                CopyTransitionSettings(entry.Transition, t);
                AfkLog.Info($"Entry: {entry.SourceState.name} → {newEntry.name}");
                created = true;
            }

            // Re-create target's original AnyState entry transitions, retargeted to new entry
            foreach (var entry in targetScan.EntryTransitions)
            {
                if (!entry.IsFromAnyState) continue;
                var t = targetSm.AddAnyStateTransition(newEntry);
                CopyTransitionSettings(entry.Transition, t);
                AfkLog.Info($"Entry: AnyState → {newEntry.name}");
                created = true;
            }

            if (!created)
            {
                // Last resort: no target entries found (target had no AFK states)
                var fallback = targetSm.AddAnyStateTransition(newEntry);
                fallback.hasExitTime = false;
                fallback.duration = 0f;
                fallback.hasFixedDuration = true;
                fallback.canTransitionToSelf = false;
                fallback.AddCondition(AnimatorConditionMode.If, 0f, "AFK");
                AfkLog.Info("Created fallback AnyState → AFK entry transition (no existing target entries).");
            }
        }

        private static void ReconnectExitFlat(
            AfkScanResult sourceScan,
            Dictionary<AnimatorState, AnimatorState> mapping,
            AnimatorState blendOutState)
        {
            if (blendOutState == null)
            {
                AfkLog.Warn("No BlendOut state for exit reconnection.");
                return;
            }

            // Find real exits: content state transitions to non-content destinations
            foreach (var srcState in sourceScan.ContentStates)
            {
                if (!mapping.ContainsKey(srcState)) continue;
                var newSource = mapping[srcState];

                foreach (var t in srcState.transitions)
                {
                    if (t.isExit)
                    {
                        var newT = newSource.AddTransition(blendOutState);
                        CopyTransitionSettings(t, newT);
                        AfkLog.Info($"Exit: {newSource.name} → {blendOutState.name} (was isExit)");
                        continue;
                    }

                    if (t.destinationState == null) continue;
                    if (sourceScan.ContentStates.Contains(t.destinationState)) continue;

                    // Destination is outside content → real exit
                    var newExit = newSource.AddTransition(blendOutState);
                    CopyTransitionSettings(t, newExit);
                    AfkLog.Info($"Exit: {newSource.name} → {blendOutState.name}");
                }
            }
        }

        // =====================================================================
        // Behaviour attachment helpers
        // =====================================================================

        private static void AttachEntryBehaviours(
            AfkScanResult sourceScan,
            Dictionary<AnimatorState, AnimatorState> mapping,
            float blendDuration)
        {
            if (sourceScan.EntryState == null || !mapping.ContainsKey(sourceScan.EntryState))
            {
                AfkLog.Warn("Could not determine entry state for behaviour attachment.");
                return;
            }

            var entryState = mapping[sourceScan.EntryState];

            // Skip if the state already has these behaviours (source might have them)
            if (HasBehaviour<VRCPlayableLayerControl>(entryState) &&
                HasBehaviour<VRCAnimatorTrackingControl>(entryState))
            {
                AfkLog.Info($"Entry state '{entryState.name}' already has tracking/layer behaviours. Skipping.");
                return;
            }

            var behaviours = new List<StateMachineBehaviour>(entryState.behaviours);

            if (!HasBehaviour<VRCPlayableLayerControl>(entryState))
            {
                var plc = ScriptableObject.CreateInstance<VRCPlayableLayerControl>();
                plc.layer = VRC_PlayableLayerControl.BlendableLayer.Action;
                plc.goalWeight = 1f;
                plc.blendDuration = blendDuration;
                behaviours.Add(plc);
            }

            if (!HasBehaviour<VRCAnimatorTrackingControl>(entryState))
            {
                var tc = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
                tc.trackingHead = VRC_AnimatorTrackingControl.TrackingType.Animation;
                tc.trackingLeftHand = VRC_AnimatorTrackingControl.TrackingType.Animation;
                tc.trackingRightHand = VRC_AnimatorTrackingControl.TrackingType.Animation;
                tc.trackingHip = VRC_AnimatorTrackingControl.TrackingType.Animation;
                tc.trackingLeftFoot = VRC_AnimatorTrackingControl.TrackingType.Animation;
                tc.trackingRightFoot = VRC_AnimatorTrackingControl.TrackingType.Animation;
                tc.trackingLeftFingers = VRC_AnimatorTrackingControl.TrackingType.Animation;
                tc.trackingRightFingers = VRC_AnimatorTrackingControl.TrackingType.Animation;
                tc.trackingEyes = VRC_AnimatorTrackingControl.TrackingType.Animation;
                tc.trackingMouth = VRC_AnimatorTrackingControl.TrackingType.Animation;
                behaviours.Add(tc);
            }

            entryState.behaviours = behaviours.ToArray();
            AfkLog.Info($"Attached entry behaviours to '{entryState.name}' (blendDuration={blendDuration:F2}).");
        }

        private static AnimatorState CreateBlendOutState(
            AnimatorStateMachine targetSm,
            AnimatorState defaultState)
        {
            var offset = CalculatePositionOffset(targetSm);
            var blendOut = targetSm.AddState("AFK BlendOut", offset + new Vector3(200f, 50f, 0f));
            blendOut.motion = defaultState != null ? defaultState.motion : null;
            blendOut.writeDefaultValues = true;

            // PlayableLayerControl: weight → 0
            var plc = ScriptableObject.CreateInstance<VRCPlayableLayerControl>();
            plc.layer = VRC_PlayableLayerControl.BlendableLayer.Action;
            plc.goalWeight = 0f;
            plc.blendDuration = 0.5f;

            // TrackingControl: all → Tracking
            var tc = ScriptableObject.CreateInstance<VRCAnimatorTrackingControl>();
            tc.trackingHead = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            tc.trackingLeftHand = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            tc.trackingRightHand = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            tc.trackingHip = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            tc.trackingLeftFoot = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            tc.trackingRightFoot = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            tc.trackingLeftFingers = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            tc.trackingRightFingers = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            tc.trackingEyes = VRC_AnimatorTrackingControl.TrackingType.Tracking;
            tc.trackingMouth = VRC_AnimatorTrackingControl.TrackingType.Tracking;

            blendOut.behaviours = new StateMachineBehaviour[] { plc, tc };

            // Transition to default state (wait for blend to finish)
            if (defaultState != null)
            {
                var t = blendOut.AddTransition(defaultState);
                t.hasExitTime = true;
                t.exitTime = 1f;
                t.duration = 0f;
                t.hasFixedDuration = true;
                AfkLog.Info($"Created AFK BlendOut → {defaultState.name}");
            }
            else
            {
                var t = blendOut.AddExitTransition();
                t.hasExitTime = true;
                t.exitTime = 1f;
                t.duration = 0f;
                t.hasFixedDuration = true;
                AfkLog.Info("Created AFK BlendOut → (Exit)");
            }

            return blendOut;
        }

        private static float GetExistingEntryBlendDuration(AfkScanResult targetScan)
        {
            if (targetScan == null || targetScan.EntryState == null)
                return 1f;

            foreach (var b in targetScan.EntryState.behaviours)
            {
                if (b is VRCPlayableLayerControl plc)
                    return plc.blendDuration;
            }

            return 1f;
        }

        private static bool HasBehaviour<T>(AnimatorState state) where T : StateMachineBehaviour
        {
            foreach (var b in state.behaviours)
            {
                if (b is T) return true;
            }
            return false;
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
