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
        // -1 = 元 AFK 削除 / 0 以上 = actionSources 内の挿入位置
        public int originalAfkOrder = 0;
        public List<AfkSlot> actionSources = new();

        // === Menu ===
        public VRCExpressionsMenu menuInstallTarget;
        public string originalAfkMenuName = "元の AFK";

        // === FX ===
        public bool removeFxAfk;
    }
}
