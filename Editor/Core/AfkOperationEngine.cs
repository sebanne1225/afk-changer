using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDKBase;

namespace Sebanne.AfkManager.Editor.Core
{
    internal static class AfkOperationEngine
    {
        internal const string SlotParameterName = "AfkManagerSlot";

        // =====================================================================
        // Public API
        // =====================================================================

        internal static void Delete(AfkOperationContext ctx)
        {
            if (!ctx.TargetScan.HasAfkStates) return;

            if (ctx.TargetScan.HasSubStateMachineContent)
                RemoveContentSubStateMachines(ctx.RootStateMachine, ctx.TargetScan);

            RemoveAfkStates(ctx.RootStateMachine, ctx.TargetScan);

            AfkLog.Info($"Deleted {ctx.TargetScan.AfkStates.Count} AFK state(s).");
        }

        internal static bool Replace(
            AfkOperationContext ctx,
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

            if (ctx.Controller.layers.Length == 0)
            {
                AfkLog.Error("Target controller has no layers.");
                return false;
            }

            if (ctx.TargetScan.HasSubStateMachineContent && sourceScan.HasSubStateMachineContent)
                return ReplaceSubSm(ctx, sourceScan, sourceController);

            return ReplaceFlat(ctx, sourceScan, sourceController);
        }

        internal static bool Add(
            AfkOperationContext ctx,
            AfkScanResult sourceScan,
            AnimatorController sourceController,
            int slotIndex,
            AnimatorState sharedBlendOut)
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

            var rootSm = ctx.RootStateMachine;

            // Step 1: Copy source content states
            var sourceSm = sourceController.layers[0].stateMachine;
            var mapping = CopyContentStates(rootSm, sourceScan, sourceSm);

            // Step 2: Copy internal transitions
            CopyInternalTransitions(sourceScan.ContentStates, mapping);

            // Step 3: Create AnyState entry with slot conditions
            if (sourceScan.EntryState != null && mapping.ContainsKey(sourceScan.EntryState))
            {
                var newEntry = mapping[sourceScan.EntryState];
                var t = rootSm.AddAnyStateTransition(newEntry);
                t.hasExitTime = false;
                t.duration = 0f;
                t.hasFixedDuration = true;
                t.canTransitionToSelf = false;
                t.AddCondition(AnimatorConditionMode.If, 0f, "AFK");
                t.AddCondition(AnimatorConditionMode.Equals, slotIndex, SlotParameterName);
                AfkLog.Info($"Entry: AnyState → {newEntry.name} (AFK=true, {SlotParameterName}={slotIndex})");
            }
            else
            {
                AfkLog.Warn("Could not determine source entry state for Add.");
            }

            // Step 4: Reconnect exits to shared BlendOut
            if (sharedBlendOut != null)
                ReconnectExitFlat(sourceScan, mapping, sharedBlendOut);

            // Step 5: Attach entry behaviours
            if (ctx.NeedsBehaviours)
                AttachEntryBehaviours(sourceScan, mapping, ctx.EntryBlendDuration);

            // Step 6: Copy missing parameters
            CopyMissingParameters(ctx.Controller, sourceController);

            var entryName = sourceScan.EntryState != null && mapping.ContainsKey(sourceScan.EntryState)
                ? mapping[sourceScan.EntryState].name
                : "(unknown)";
            AfkLog.Info($"Added AFK slot {slotIndex}. Copied {mapping.Count} state(s). Entry: {entryName}");

            return true;
        }

        internal static void AddSlotConditionToExistingEntries(AfkOperationContext ctx, int slotValue)
        {
            var rootSm = ctx.RootStateMachine;

            // Recreate AnyState entries with added slot condition
            foreach (var entry in ctx.TargetScan.EntryTransitions)
            {
                if (!entry.IsFromAnyState) continue;
                if (entry.DestinationState == null) continue;

                var dest = entry.DestinationState;
                var oldTransition = entry.Transition;

                // Remove old AnyState transition
                var remaining = rootSm.anyStateTransitions
                    .Where(t => t != oldTransition)
                    .ToArray();
                rootSm.anyStateTransitions = remaining;

                // Recreate with added slot condition
                var newT = rootSm.AddAnyStateTransition(dest);
                CopyTransitionSettings(oldTransition, newT);
                newT.AddCondition(AnimatorConditionMode.Equals, slotValue, SlotParameterName);
                AfkLog.Info($"Tagged AnyState → {dest.name} with {SlotParameterName}={slotValue}");
            }

            // Recreate per-state entries with added slot condition
            foreach (var entry in ctx.TargetScan.EntryTransitions)
            {
                if (entry.IsFromAnyState) continue;
                if (entry.SourceState == null || entry.DestinationState == null) continue;

                var source = entry.SourceState;
                var dest = entry.DestinationState;
                var oldTransition = entry.Transition;

                // Remove old transition
                var remaining = source.transitions
                    .Where(t => t != oldTransition)
                    .ToArray();
                source.transitions = remaining;

                // Recreate with added slot condition
                var newT = source.AddTransition(dest);
                CopyTransitionSettings(oldTransition, newT);
                newT.AddCondition(AnimatorConditionMode.Equals, slotValue, SlotParameterName);
                AfkLog.Info($"Tagged {source.name} → {dest.name} with {SlotParameterName}={slotValue}");
            }
        }

        internal static void EnsureSlotParameter(AnimatorController controller)
        {
            foreach (var p in controller.parameters)
            {
                if (p.name == SlotParameterName && p.type == AnimatorControllerParameterType.Int)
                    return;
            }

            controller.AddParameter(SlotParameterName, AnimatorControllerParameterType.Int);
            AfkLog.Info($"Added parameter: {SlotParameterName} (Int)");
        }

        internal static AnimatorState CreateSharedBlendOut(AfkOperationContext ctx)
        {
            return CreateBlendOutState(ctx.RootStateMachine, ctx.RootStateMachine.defaultState);
        }

        // =====================================================================
        // Replace: SubStateMachine pattern
        // =====================================================================

        private static bool ReplaceSubSm(
            AfkOperationContext ctx,
            AfkScanResult sourceScan,
            AnimatorController sourceController)
        {
            var rootSm = ctx.RootStateMachine;

            // Step 1: Remove content SubStateMachines (skeleton preserved)
            RemoveContentSubStateMachines(rootSm, ctx.TargetScan);

            // Step 2: Clean up skeleton states' dangling transitions
            CleanupSkeletonTransitions(rootSm, ctx.TargetScan);

            // Step 3: Copy source content states into target root SM
            var sourceSm = sourceController.layers[0].stateMachine;
            var mapping = CopyContentStates(rootSm, sourceScan, sourceSm);

            // Step 4: Copy internal transitions between content states
            CopyInternalTransitions(sourceScan.ContentStates, mapping);

            // Step 5: Reconnect skeleton → content entry
            ReconnectSubSmEntry(ctx.TargetScan, sourceScan, mapping);

            // Step 6: Attach entry behaviours
            if (ctx.NeedsBehaviours)
                AttachEntryBehaviours(sourceScan, mapping, ctx.EntryBlendDuration);

            // Step 7: Create BlendOut state and reconnect exits
            if (ctx.NeedsBlendOut)
            {
                var blendOutState = CreateBlendOutState(rootSm, rootSm.defaultState);
                ReconnectSubSmExitToBlendOut(sourceScan, mapping, blendOutState);
            }

            // Step 8: Add missing parameters
            CopyMissingParameters(ctx.Controller, sourceController);

            var entryName = sourceScan.EntryState != null && mapping.ContainsKey(sourceScan.EntryState)
                ? mapping[sourceScan.EntryState].name
                : "(unknown)";
            AfkLog.Info($"Replaced AFK content (SubSM). Copied {mapping.Count} state(s). Entry: {entryName}");

            return true;
        }

        // =====================================================================
        // Replace: Flat pattern
        // =====================================================================

        private static bool ReplaceFlat(
            AfkOperationContext ctx,
            AfkScanResult sourceScan,
            AnimatorController sourceController)
        {
            var rootSm = ctx.RootStateMachine;

            // Step 1: Remove existing AFK states
            RemoveAfkStates(rootSm, ctx.TargetScan);

            // Step 2: Copy source content states
            var sourceSm = sourceController.layers[0].stateMachine;
            var mapping = CopyContentStates(rootSm, sourceScan, sourceSm);

            // Step 3: Copy internal transitions
            CopyInternalTransitions(sourceScan.ContentStates, mapping);

            // Step 4: Reconnect entry transitions
            ReconnectEntryFlat(rootSm, ctx.TargetScan, sourceScan, mapping);

            // Step 5: Create BlendOut and reconnect exits
            if (ctx.NeedsBlendOut)
            {
                var blendOutState = CreateBlendOutState(rootSm, rootSm.defaultState);
                ReconnectExitFlat(sourceScan, mapping, blendOutState);
            }

            // Step 6: Attach entry behaviours
            if (ctx.NeedsBehaviours)
                AttachEntryBehaviours(sourceScan, mapping, ctx.EntryBlendDuration);

            // Step 7: Add missing parameters
            CopyMissingParameters(ctx.Controller, sourceController);

            var entryName = sourceScan.EntryState != null && mapping.ContainsKey(sourceScan.EntryState)
                ? mapping[sourceScan.EntryState].name
                : "(unknown)";
            AfkLog.Info($"Replaced AFK states (flat). Copied {mapping.Count} state(s). Entry: {entryName}");

            return true;
        }

        // =====================================================================
        // Delete primitives
        // =====================================================================

        private static void RemoveContentSubStateMachines(
            AnimatorStateMachine rootSm,
            AfkScanResult scan)
        {
            foreach (var subSm in scan.ContentSubStateMachines)
            {
                AfkLog.Info($"Removing AFK SubStateMachine: {subSm.name}");
                rootSm.stateMachines = rootSm.stateMachines
                    .Where(s => s.stateMachine != subSm).ToArray();
            }

            var filtered = rootSm.anyStateTransitions
                .Where(t =>
                {
                    if (t.destinationState != null && scan.ContentStates.Contains(t.destinationState))
                        return false;
                    if (t.destinationStateMachine != null && scan.ContentSubStateMachines.Contains(t.destinationStateMachine))
                        return false;
                    return true;
                })
                .ToArray();
            if (filtered.Length != rootSm.anyStateTransitions.Length)
                rootSm.anyStateTransitions = filtered;
        }

        private static void CleanupSkeletonTransitions(
            AnimatorStateMachine rootSm,
            AfkScanResult scan)
        {
            foreach (var cs in rootSm.states)
            {
                var hasContentTransition = false;
                foreach (var t in cs.state.transitions)
                {
                    if (t.destinationState != null && scan.ContentStates.Contains(t.destinationState))
                    {
                        hasContentTransition = true;
                        break;
                    }
                }

                if (!hasContentTransition) continue;

                var filtered = cs.state.transitions
                    .Where(t => t.destinationState == null || !scan.ContentStates.Contains(t.destinationState))
                    .ToArray();
                cs.state.transitions = filtered;
            }
        }

        private static void RemoveAfkStates(AnimatorStateMachine rootSm, AfkScanResult scan)
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

            // Remove AFK states themselves (direct array manipulation to avoid Undo errors on NDMF clones)
            foreach (var afkState in scan.AfkStates)
            {
                if (scan.StateOwnership.TryGetValue(afkState, out var parentSm))
                    parentSm.states = parentSm.states.Where(s => s.state != afkState).ToArray();
                else
                    rootSm.states = rootSm.states.Where(s => s.state != afkState).ToArray();
            }
        }

        // =====================================================================
        // Reconnect primitives
        // =====================================================================

        private static void ReconnectSubSmEntry(
            AfkScanResult targetScan,
            AfkScanResult sourceScan,
            Dictionary<AnimatorState, AnimatorState> mapping)
        {
            foreach (var entry in sourceScan.SkeletonToContentTransitions)
            {
                if (entry.DestinationState == null) continue;
                if (!mapping.ContainsKey(entry.DestinationState)) continue;

                var newDest = mapping[entry.DestinationState];

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

                    var newExit = newSource.AddTransition(blendOutState);
                    CopyTransitionSettings(t, newExit);
                    AfkLog.Info($"Exit: {newSource.name} → {blendOutState.name}");
                }
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

            foreach (var entry in targetScan.EntryTransitions)
            {
                if (entry.SourceState == null) continue;
                var t = entry.SourceState.AddTransition(newEntry);
                CopyTransitionSettings(entry.Transition, t);
                AfkLog.Info($"Entry: {entry.SourceState.name} → {newEntry.name}");
                created = true;
            }

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

                    var newExit = newSource.AddTransition(blendOutState);
                    CopyTransitionSettings(t, newExit);
                    AfkLog.Info($"Exit: {newSource.name} → {blendOutState.name}");
                }
            }
        }

        // =====================================================================
        // Behaviour primitives
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

            var plc = ScriptableObject.CreateInstance<VRCPlayableLayerControl>();
            plc.layer = VRC_PlayableLayerControl.BlendableLayer.Action;
            plc.goalWeight = 0f;
            plc.blendDuration = 0.5f;

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

        private static bool HasBehaviour<T>(AnimatorState state) where T : StateMachineBehaviour
        {
            foreach (var b in state.behaviours)
            {
                if (b is T) return true;
            }
            return false;
        }

        // =====================================================================
        // Copy primitives
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

        // =====================================================================
        // Helpers
        // =====================================================================

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
