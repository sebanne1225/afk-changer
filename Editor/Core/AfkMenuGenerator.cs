#if HAS_MODULAR_AVATAR
using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;

namespace Sebanne.AfkManager.Editor.Core
{
    internal static class AfkMenuGenerator
    {
        internal static void Generate(
            GameObject avatarRoot,
            List<AfkSlot> slots,
            bool removeActionAfk,
            VRCExpressionsMenu installTarget,
            int defaultSlot,
            string originalMenuName)
        {
            var menuObj = new GameObject("AFK Manager");
            menuObj.transform.SetParent(avatarRoot.transform, false);

            // Menu installer (MA discovery point)
            var menuInstaller = menuObj.AddComponent<ModularAvatarMenuInstaller>();
            if (installTarget != null)
                menuInstaller.installTargetMenu = installTarget;

            // Parent menu item (SubMenu containing children)
            var parentMenuItem = menuObj.AddComponent<ModularAvatarMenuItem>();
            parentMenuItem.PortableControl.Type = PortableControlType.SubMenu;
            parentMenuItem.MenuSource = SubmenuSource.Children;

            // Parameter declaration
            var maParams = menuObj.AddComponent<ModularAvatarParameters>();
            maParams.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = AfkOperationEngine.SlotParameterName,
                syncType = ParameterSyncType.Int,
                defaultValue = defaultSlot,
                saved = true
            });

            // Slot 0: original AFK (only when keeping original)
            if (!removeActionAfk)
            {
                var slot0Name = string.IsNullOrEmpty(originalMenuName) ? "元の AFK" : originalMenuName;
                var slot0Obj = new GameObject(slot0Name);
                slot0Obj.transform.SetParent(menuObj.transform, false);

                var slot0Item = slot0Obj.AddComponent<ModularAvatarMenuItem>();
                slot0Item.PortableControl.Type = PortableControlType.Toggle;
                slot0Item.PortableControl.Parameter = AfkOperationEngine.SlotParameterName;
                slot0Item.PortableControl.Value = 0;
                slot0Item.isSynced = true;
                slot0Item.isSaved = true;
                slot0Item.isDefault = defaultSlot == 0;
            }

            // Menu items per added slot
            for (var i = 0; i < slots.Count; i++)
            {
                var slotIndex = i + 1;
                var slotName = string.IsNullOrEmpty(slots[i].slotName)
                    ? $"AFK {slotIndex}"
                    : slots[i].slotName;

                var itemObj = new GameObject(slotName);
                itemObj.transform.SetParent(menuObj.transform, false);

                var menuItem = itemObj.AddComponent<ModularAvatarMenuItem>();
                menuItem.PortableControl.Type = PortableControlType.Toggle;
                menuItem.PortableControl.Parameter = AfkOperationEngine.SlotParameterName;
                menuItem.PortableControl.Value = slotIndex;
                menuItem.isSynced = true;
                menuItem.isSaved = true;
                menuItem.isDefault = slotIndex == defaultSlot;
            }

            var totalItems = removeActionAfk ? slots.Count : slots.Count + 1;
            AfkLog.Info($"Generated MA menu: {totalItems} item(s), default={defaultSlot}.");
        }
    }
}
#endif
