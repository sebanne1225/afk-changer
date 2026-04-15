#if HAS_MODULAR_AVATAR
using System.Collections.Generic;
using nadena.dev.modular_avatar.core;
using UnityEngine;

namespace Sebanne.AfkManager.Editor.Core
{
    internal static class AfkMenuGenerator
    {
        internal static void Generate(
            GameObject avatarRoot,
            List<AfkSlot> slots,
            bool removeActionAfk)
        {
            var defaultSlot = removeActionAfk ? 1 : 0;

            var menuObj = new GameObject("AFK Manager");
            menuObj.transform.SetParent(avatarRoot.transform, false);

            // Parameter declaration
            var maParams = menuObj.AddComponent<ModularAvatarParameters>();
            maParams.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = AfkOperationEngine.SlotParameterName,
                syncType = ParameterSyncType.Int,
                defaultValue = defaultSlot,
                saved = true
            });

            // Menu items per slot
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

            AfkLog.Info($"Generated MA menu: {slots.Count} slot(s), default={defaultSlot}.");
        }
    }
}
#endif
