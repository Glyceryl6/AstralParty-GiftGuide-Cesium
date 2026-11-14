using System;
using System.Collections.Generic;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// 从旧 Inventory 移植的纯文本安全格式化函数。
    /// 不接入原版购买/物品详情窗口，后续界面移植再调用。
    /// </summary>
    internal static class PropUsageText {
        internal static void AddSection(List<string> output, IReadOnlyList<string> names,
            string heading, string color) {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (names == null || names.Count == 0) return;
            string start = "[color=#" + color + "]";
            output.Add(start + Escape(heading) + "[/color]");
            foreach (var name in names) output.Add(start + Escape(name) + "[/color]");
        }
        internal static string Escape(string value) {
            return (value ?? string.Empty).Replace("[", "［").Replace("]", "］");
        }
    }
}
