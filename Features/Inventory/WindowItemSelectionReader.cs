using System;
using System.Collections.Generic;
using System.Reflection;
using FairyGUI;
using GameLogic;
using UI;
using Tools;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// The detail window's private itemId can be stale after ShowByWin; a
    /// visible item (icon + name + optional description) is authoritative.
    /// Never attribute a gift to an ambiguous or out-of-date item.
    /// </summary>
    internal static class WindowItemSelectionReader {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private static readonly FieldInfo? PurchaseGoodsField = typeof(PurchaseWindow).GetField("goodsData", AnyInstance);
        private static readonly FieldInfo? BoxRewardsField = typeof(UIBoxProp_Com_OpenChest).GetField("ChestItemList", AnyInstance);
        private static readonly FieldInfo? DetailItemIdField = typeof(PropDetailWindow).GetField("itemId", AnyInstance);
        private static readonly Dictionary<string, int> ByVisibleItem = new();

        internal static int FromPurchase(PurchaseWindow window) {
            if (!(window.contentPane is UIPurchaseWindow pane)) return 0;
            string name = pane.txt_itemName?.text ?? "";
            string icon = IconUrl(pane.btn_itemLoader);
            string desc = pane.txt_itemDes?.text ?? "";
            try {
                int id = (PurchaseGoodsField?.GetValue(window) as BaseGoodsData)?.itemConfig?.Id ?? 0;
                if (Matches(id, name, icon, desc)) return id;
            } catch { /* native field not exposed in every runtime build */ }
            return ResolveVisible(name, icon, desc);
        }

        internal static int FromDetail(PropDetailWindow window) {
            if (!(window.contentPane is UIPropDetailWindow pane)) return 0;
            string name = pane.txt_itemName?.text ?? "";
            string icon = IconUrl(pane.btn_item);
            string desc = pane.txt_itemDes?.text ?? "";
            // A stale ID in PropDetailWindow is not sufficient by itself.
            try {
                int id = DetailItemIdField?.GetValue(window) is int i ? i : 0;
                if (Matches(id, name, icon, desc)) return id;
            } catch { }
            return ResolveVisible(name, icon, desc);
        }

        internal static int FromBoxProp(BoxPropWindow window) {
            if (!(window.contentPane is UIBoxPropWindow pane) ||
                pane.type.selectedIndex != 1 || pane.com_Chest?.list_Item == null) return 0;
            GList list = pane.com_Chest.list_Item;
            int selected = list.selectedIndex;
            try {
                // Use the same reward order as the original chest window.
                // Never identify the chest itself as the selected material.
                var rewards = BoxRewardsField?.GetValue(pane.com_Chest)
                    as List<KeyValuePair<int, int>>;
                if (rewards == null || selected < 0 || selected >= rewards.Count) return 0;
                int itemId = rewards[selected].Key;
                ItemInfoConfigure info = itemId.GetItemInfoConfigure();
                if (info == null) return 0;
                // A recycled list item may still display the previous reward.
                if (selected >= list.numChildren ||
                    !(list.GetChildAt(selected) is UICom_Item item) ||
                    !string.Equals(item.loader_Icon?.url, info.ShowIcon,
                        StringComparison.Ordinal)) return 0;
                return itemId;
            } catch { return 0; }
        }

        private static string IconUrl(GButton button) {
            if (button == null) return "";
            if (button is UICom_LitItem native) return native.loader_Icon?.url ?? "";
            // The package may have been loaded before UICom_LitItem binding.
            return button.numChildren > 4 && button.GetChildAt(4) is GLoader loader
                ? loader.url ?? "" : "";
        }

        private static bool Matches(int id, string name, string icon, string description) {
            if (id <= 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(icon)) return false;
            try {
                ItemInfoConfigure info = id.GetItemInfoConfigure();
                return info != null && VisibleMatches(info, name, icon) && DescriptionMatches(info, description);
            } catch { return false; }
        }

        private static bool VisibleMatches(ItemInfoConfigure info, string name, string icon) =>
            string.Equals(info.ShowIcon, icon, StringComparison.Ordinal) &&
            string.Equals(info.NameID.GetLocal(UIStringType.Item), name, StringComparison.Ordinal);

        private static bool DescriptionMatches(ItemInfoConfigure info, string description) =>
            string.IsNullOrEmpty(description) ||
            string.Equals(info.DescriptionID.GetLocal(UIStringType.Item), description, StringComparison.Ordinal);

        private static int ResolveVisible(string name, string icon, string description) {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(icon)) return 0;
            string key = name + "\u001f" + icon + "\u001f" + description;
            if (ByVisibleItem.TryGetValue(key, out int cached)) return cached;
            int exact = 0, approximate = 0;
            bool duplicateExact = false, duplicateApproximate = false;
            try {
                ItemConfigure items = StaticConfigure.Item;
                if (items == null || items.Infos == null || items.Infos.Count > 8192) return 0;
                for (int i = 0, count = items.Infos.Count; i < count; i++) {
                    ItemInfoConfigure info = items.Infos[i];
                    if (info == null || !VisibleMatches(info, name, icon)) continue;
                    if (approximate > 0 && approximate != info.Id) duplicateApproximate = true;
                    else approximate = info.Id;
                    if (!DescriptionMatches(info, description)) continue;
                    if (exact > 0 && exact != info.Id) duplicateExact = true;
                    else exact = info.Id;
                }
            } catch { return 0; }
            // Description text may contain localized variables; use icon+name
            // only when it still identifies exactly one item.
            int resolved = !duplicateExact && exact != 0 ? exact :
                (!duplicateApproximate && approximate != 0 ? approximate : 0);
            if (ByVisibleItem.Count > 128) ByVisibleItem.Clear();
            ByVisibleItem[key] = resolved;
            return resolved;
        }
    }
}
