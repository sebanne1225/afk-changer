using System.Collections.Generic;
using UnityEditor.Animations;

namespace Sebanne.AfkChanger.Editor.Core
{
    internal sealed class AfkScanResult
    {
        internal HashSet<AnimatorState> AfkStates { get; } = new HashSet<AnimatorState>();
        internal List<AfkTransitionInfo> EntryTransitions { get; } = new List<AfkTransitionInfo>();
        internal List<AfkTransitionInfo> ExitTransitions { get; } = new List<AfkTransitionInfo>();
        internal AnimatorState EntryState { get; set; }
        internal bool HasAfkStates => AfkStates.Count > 0;

        /// <summary>
        /// Each AFK state mapped to the AnimatorStateMachine that directly owns it.
        /// </summary>
        internal Dictionary<AnimatorState, AnimatorStateMachine> StateOwnership { get; } =
            new Dictionary<AnimatorState, AnimatorStateMachine>();

        // --- Content / Skeleton separation ---

        /// <summary>
        /// True if AFK content lives inside SubStateMachine(s).
        /// When true, only ContentStates are replaced; skeleton states are preserved.
        /// </summary>
        internal bool HasSubStateMachineContent { get; set; }

        /// <summary>
        /// SubStateMachines to remove (contain AFK content).
        /// </summary>
        internal HashSet<AnimatorStateMachine> ContentSubStateMachines { get; } =
            new HashSet<AnimatorStateMachine>();

        /// <summary>
        /// States to replace. In SubSM pattern: all states in AFK SubSMs.
        /// In flat pattern: same as AfkStates.
        /// </summary>
        internal HashSet<AnimatorState> ContentStates { get; } = new HashSet<AnimatorState>();

        /// <summary>
        /// Transitions from skeleton (root SM) states into content (SubSM) states.
        /// Used for entry reconnection after replacement.
        /// </summary>
        internal List<AfkTransitionInfo> SkeletonToContentTransitions { get; } =
            new List<AfkTransitionInfo>();

        /// <summary>
        /// Transitions from content (SubSM) states to skeleton (root SM) states.
        /// Used for exit reconnection after replacement.
        /// </summary>
        internal List<AfkTransitionInfo> ContentToSkeletonTransitions { get; } =
            new List<AfkTransitionInfo>();
    }

    internal sealed class AfkTransitionInfo
    {
        internal AnimatorStateTransition Transition { get; }
        internal AnimatorState SourceState { get; }
        internal AnimatorState DestinationState { get; }
        internal bool IsFromAnyState { get; }

        internal AfkTransitionInfo(
            AnimatorStateTransition transition,
            AnimatorState sourceState,
            AnimatorState destinationState,
            bool isFromAnyState)
        {
            Transition = transition;
            SourceState = sourceState;
            DestinationState = destinationState;
            IsFromAnyState = isFromAnyState;
        }
    }
}
