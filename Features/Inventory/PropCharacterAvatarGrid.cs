using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using Tools;
using UI;
using UnityEngine;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Native shop cards inside the expanded PropDetail/Purchase GLabel.
    /// The quality frame, TextureLoader, item name and rich price text all
    /// come from one UIButton_GoodsItem_Store, in their authored coordinates.
    /// </summary>
    internal sealed class PropCharacterAvatarGrid : IDisposable {
        private const float Padding = 7f;
        private const int ColumnGap = 5;
        private const int RowGap = 6;
        private const float HeadingHeight = 27f;
        private const float SectionGap = 10f;

        private UISetting_Comp_Score_Desc? _viewport;
        private GComponent? _pane;
        private PropPopupLayout? _layout;
        private int _item;
        private DateTime _retry;

        internal bool Alive => _viewport != null && !_viewport.isDisposed && _viewport.parent != null;
        internal bool IsFor(GComponent pane, int item) => ReferenceEquals(_pane, pane) && _item == item && Alive;

        internal bool TryShow(GComponent pane, int itemId, GTextField description, GButton preview,
            GTextField name, GLabel background, IReadOnlyList<GObject> footer,
            IReadOnlyList<CharacterMaterialUsage> usages) {
            if (usages.Count == 0 || DateTime.UtcNow < _retry || !UiPackageWarmup.Ready()) return false;
            UISetting_Comp_Score_Desc? created = null;
            PropPopupLayout? layout = null;
            UIButton_GoodsItem_Store? sample = null;
            try {
                UIObjectFactory.SetPackageItemExtension(UISetting_Comp_Score_Desc.URL,
                    typeof(UISetting_Comp_Score_Desc));
                UIObjectFactory.SetPackageItemExtension(UIButton_GoodsItem_Store.URL,
                    typeof(UIButton_GoodsItem_Store));
                UIObjectFactory.SetPackageItemExtension(UICom_GoodsQuality_Store.URL,
                    typeof(UICom_GoodsQuality_Store));
                UIObjectFactory.SetPackageItemExtension(UICom_GoodsItemName.URL,
                    typeof(UICom_GoodsItemName));
                UIObjectFactory.SetPackageItemExtension(UICom_Price.URL,
                    typeof(UICom_Price));
                UIObjectFactory.SetPackageItemExtension(UICom_Label.URL, typeof(UICom_Label));
                UIObjectFactory.SetPackageItemExtension(UICom_ItemType.URL, typeof(UICom_ItemType));
                sample = UIButton_GoodsItem_Store.CreateInstance();
                if (sample == null || sample.com_Quality == null || sample.com_ItemName == null ||
                    sample.com_Price == null || sample.loader_Goods == null ||
                    sample.width < 20f || sample.height < 20f)
                    throw new InvalidOperationException("原版商店卡片尚未就绪");

                layout = PropPopupLayout.Expand(pane, background, footer);
                if (layout == null || layout.GuideWidth < 130f || layout.GuideHeight < 95f)
                    return false;
                created = UISetting_Comp_Score_Desc.CreateInstance();
                if (created == null) return false;
                created.RemoveChildren(0, -1, true);
                created.SetSize(layout.GuideWidth, layout.GuideHeight);
                created.SetXY(layout.GuideLeft, layout.GuideTop);
                ScrollPane scroll = created.scrollPane;
                if (scroll == null) throw new InvalidOperationException("滚动区域尚未就绪");
                scroll.mouseWheelEnabled = true;
                scroll.touchEffect = true;
                scroll.bouncebackEffect = false;
                // The original popup's native background is visible behind it;
                // there is no second side panel or drawn panel background.
                created.opaque = false;

                float available = Math.Min(layout.GuideWidth,
                    scroll.viewWidth > 0f ? scroll.viewWidth : layout.GuideWidth);
                float cardWidth = sample.width;
                float cardHeight = sample.height;
                float inner = Math.Max(1f, available - 2f * Padding);
                // StorePanel uses FlowHorizontal and its normal full-size
                // cards; shrink only if the window genuinely cannot fit one.
                float scale = Math.Min(1f, inner / cardWidth);
                float cellWidth = cardWidth * scale;
                float cellHeight = cardHeight * scale;
                int columns = Math.Max(1, (int)Math.Floor((inner + ColumnGap) /
                    (cellWidth + ColumnGap)));
                var content = new GComponent {
                    name = "GiftGuide.CharacterSections", touchable = false
                };
                float y = Padding;
                y = AddSection(content, usages, 0, "契约", name, description,
                    inner, columns, cellWidth, cellHeight, scale, y);
                y = AddSection(content, usages, 1, "潜能", name, description,
                    inner, columns, cellWidth, cellHeight, scale, y);
                y = AddSection(content, usages, 2, "偏好礼物", name, description,
                    inner, columns, cellWidth, cellHeight, scale, y);
                if (y <= Padding) { content.Dispose(); return false; }
                content.SetSize(available, y + Padding);
                created.AddChild(content);
                pane.AddChild(created);
                created.EnsureBoundsCorrect();
                StartNativeNameScroll(content);
                _viewport = created;
                _pane = pane;
                _item = itemId;
                _layout = layout;
                created = null;
                layout = null;
                return true;
            } catch (Exception ex) {
                _retry = DateTime.UtcNow.AddSeconds(5);
                SdkLog.Warn("GiftGuide", "角色列表暂不可用：" + ex.Message);
                return false;
            } finally {
                if (sample != null && !sample.isDisposed) sample.Dispose();
                if (created != null && !created.isDisposed) {
                    created.RemoveFromParent(); created.Dispose();
                }
                layout?.Dispose();
            }
        }

        private static float AddSection(GComponent content, IReadOnlyList<CharacterMaterialUsage> all,
            int group, string heading, GTextField nameStyle, GTextField descriptionStyle,
            float width, int columns,
            float cellWidth, float cellHeight, float scale, float y) {
            var members = new List<CharacterMaterialUsage>();
            foreach (CharacterMaterialUsage usage in all) {
                if (group == 0 && usage.ContractMaterial ||
                    group == 1 && usage.PotentialMaterial ||
                    group == 2 && usage.GiftPreferred) members.Add(usage);
            }
            if (members.Count == 0) return y;
            if (y > Padding) y += SectionGap;
            GTextField title = new GTextField {
                text = heading + "（" + members.Count + "）",
                touchable = false, autoSize = AutoSizeType.None,
                verticalAlign = VertAlignType.Middle, align = AlignType.Left
            };
            TextFormat format = title.textFormat;
            format.CopyFrom(nameStyle.textFormat);
            format.size = Math.Max(16, Math.Min(21, format.size));
            format.bold = true;
            title.textFormat = format;
            title.SetXY(Padding, y);
            title.SetSize(width, HeadingHeight);
            content.AddChild(title);
            y += HeadingHeight + 3f;
            var list = new GList {
                name = "GiftGuide.NativeStoreItems." + group,
                layout = ListLayoutType.FlowHorizontal,
                columnGap = ColumnGap, lineGap = RowGap,
                columnCount = columns, autoResizeItem = false,
                touchable = false
            };
            int rows = (members.Count + columns - 1) / columns;
            float listHeight = rows * cellHeight + Math.Max(0, rows - 1) * RowGap;
            list.SetSize(width, listHeight);
            list.SetXY(Padding, y);
            content.AddChild(list);
            foreach (CharacterMaterialUsage usage in members) {
                UIButton_GoodsItem_Store card = BuildStoreCard(usage, group, descriptionStyle);
                if (scale < .999f) {
                    // Keep the native card's internal fonts and coordinates.
                    // The entire card, not individual labels, is scaled.
                    card.SetScale(scale, scale);
                }
                list.AddChild(card);
            }
            list.EnsureBoundsCorrect();
            return y + listHeight;
        }

        private static UIButton_GoodsItem_Store BuildStoreCard(CharacterMaterialUsage usage,
            int group, GTextField descriptionStyle) {
            UIButton_GoodsItem_Store card = UIButton_GoodsItem_Store.CreateInstance();
            if (card == null || card.com_Quality == null || card.loader_Goods == null ||
                card.com_ItemName?.txt_Name == null || card.com_Price?.txt_SalePrice == null)
                throw new InvalidOperationException("原版商店卡片字段缺失");
            card.name = "GiftGuide.Character." + usage.HeroId + "." + group;
            // Do not attach any shop item callback to a character card.
            card.touchable = false;
            card.opaque = false;
            if (card.isEmpty != null) card.isEmpty.selectedIndex = 0;
            if (card.sellOut != null) card.sellOut.selectedIndex = 0;
            if (card.owned != null) card.owned.selectedIndex = 0;
            if (card.isRecharge != null) card.isRecharge.selectedIndex = 0;
            if (card.isVideo != null) card.isVideo.selectedIndex = 0;
            if (card.com_Quality.quality != null)
                card.com_Quality.quality.selectedIndex = usage.ContractSigned
                    ? (int)QualityType.Orange : (int)QualityType.Purple;
            if (card.com_Quality.isEmpty != null)
                card.com_Quality.isEmpty.selectedIndex = 0;
            card.loader_Goods.url = usage.Portrait;
            card.loader_Goods.visible = true;
            card.com_Quality.visible = true;
            string display = string.IsNullOrWhiteSpace(usage.Title) || usage.Title == usage.Name
                ? usage.Name : usage.Title + " · " + usage.Name;
            card.com_ItemName.txt_Name.text = display;
            card.com_ItemName.txt_Name.visible = true;
            card.com_ItemName.visible = true;
            string status = group == 0
                ? (usage.ContractSigned ? "" : usage.ContractMissing > 0 ? "缺少 " + usage.ContractMissing : "材料已齐")
                : group == 1
                    ? (usage.PotentialAwakened ? "" : usage.PotentialMissing > 0
                        ? "缺少 " + usage.PotentialMissing : "材料已齐")
                    : "偏好礼物";
            // The shelf's numeric price font may not contain the Chinese
            // glyphs for "缺少". Use the popup's own localized text font while
            // retaining the native GRichTextField and its authored position.
            TextFormat priceFont = card.com_Price.txt_SalePrice.textFormat;
            priceFont.CopyFrom(descriptionStyle.textFormat);
            priceFont.size = Math.Max(13, Math.Min(17, priceFont.size));
            card.com_Price.txt_SalePrice.textFormat = priceFont;
            card.com_Price.txt_SalePrice.text = status;
            card.com_Price.txt_SalePrice.visible = status.Length > 0;
            card.com_Price.visible = status.Length > 0;
            // Hide merchandise-only labels, discounts and action widgets, but
            // do not hide the native quality, name or price components.
            for (int i = 0; i < card.numChildren; i++) {
                GObject child = card.GetChildAt(i);
                if (!ReferenceEquals(child, card.com_Quality) &&
                    !ReferenceEquals(child, card.loader_Goods) &&
                    !ReferenceEquals(child, card.com_ItemName) &&
                    !ReferenceEquals(child, card.com_Price)) child.visible = false;
            }
            return card;
        }

        private static void StartNativeNameScroll(GComponent content) {
            for (int i = 0; i < content.numChildren; i++) {
                if (!(content.GetChildAt(i) is GList list)) continue;
                for (int j = 0; j < list.numChildren; j++) {
                    if (!(list.GetChildAt(j) is UIButton_GoodsItem_Store card) ||
                        card.com_ItemName?.txt_Name == null) continue;
                    GTextField label = card.com_ItemName.txt_Name;
                    AutoSizeType old = label.autoSize;
                    label.autoSize = AutoSizeType.Both;
                    label.TryScrollTextField(label.text);
                    label.autoSize = old;
                }
            }
        }

        internal void Clear() {
            if (_viewport != null && !_viewport.isDisposed) {
                _viewport.RemoveFromParent(); _viewport.Dispose();
            }
            _layout?.Dispose();
            _layout = null;
            _viewport = null;
            _pane = null;
            _item = 0;
        }
        public void Dispose() => Clear();
    }
}
