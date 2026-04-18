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
            List<EffectiveSlot> effectiveSlots,
            VRCExpressionsMenu installTarget,
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

            // Parameter declaration (default=1, 1-based slot value scheme)
            var maParams = menuObj.AddComponent<ModularAvatarParameters>();
            maParams.parameters.Add(new ParameterConfig
            {
                nameOrPrefix = AfkOperationEngine.SlotParameterName,
                syncType = ParameterSyncType.Int,
                defaultValue = 1,
                saved = true
            });

            // Menu items (effectiveSlots[0] is fallback slot = default)
            for (var i = 0; i < effectiveSlots.Count; i++)
            {
                var slot = effectiveSlots[i];
                var slotValue = i + 1;

                string itemName;
                if (slot.IsOriginal)
                {
                    itemName = string.IsNullOrEmpty(originalMenuName) ? "元の AFK" : originalMenuName;
                }
                else
                {
                    itemName = string.IsNullOrEmpty(slot.Source.slotName)
                        ? $"AFK {slotValue}"
                        : slot.Source.slotName;
                }

                var itemObj = new GameObject(itemName);
                itemObj.transform.SetParent(menuObj.transform, false);

                var menuItem = itemObj.AddComponent<ModularAvatarMenuItem>();
                menuItem.PortableControl.Type = PortableControlType.Toggle;
                menuItem.PortableControl.Parameter = AfkOperationEngine.SlotParameterName;
                menuItem.PortableControl.Value = slotValue;
                menuItem.isSynced = true;
                menuItem.isSaved = true;
                menuItem.isDefault = (i == 0);
            }

            AfkLog.Info($"Generated MA menu: {effectiveSlots.Count} item(s), default=1 (first slot).");
        }
    }
}
#endif
