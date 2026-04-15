using System;
using System.Collections.Generic;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sebanne.AfkManager
{
    public enum AfkSourceInputType
    {
        AvatarPrefab,
        Controller,
    }

    [Serializable]
    public class AfkSlot
    {
        public string slotName;
        public AfkSourceInputType inputType = AfkSourceInputType.AvatarPrefab;
        public GameObject avatarPrefab;
        public RuntimeAnimatorController sourceController;
    }

    [AddComponentMenu("Sebanne/AFK Manager")]
    public sealed class AfkManagerComponent : MonoBehaviour, VRC.SDKBase.IEditorOnly
    {
        // === Action ===
        public bool removeActionAfk;
        public List<AfkSlot> actionSources = new();

        // === Menu ===
        public VRCExpressionsMenu menuInstallTarget;
        public int defaultSlotIndex = -1;
        public string originalAfkMenuName = "元の AFK";

        // === FX ===
        public bool removeFxAfk;
    }
}
