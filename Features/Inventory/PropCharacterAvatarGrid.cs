using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using Tools;
using UI;
using UnityEngine;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Use the game's COMPLETE store shelf button for each character, including
    /// its native quality frame, portrait slot, name and price area. All these
    /// elements share the native card's coordinate space. GList arranges the
    /// native-sized cards; the Setting scroll pane hosts the three sections.
    /// </summary>
    internal sealed class PropCharacterAvatarGrid : IDisposable {
        private const float Padding = 7f;
        private const float ColumnGap = 4f;
        private const float RowGap = 4f;
        private const float CaptionHeight = 33f;
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
                UIObjectFactory.SetPackageItemExtension(UICom_Label.URL,
                    typeof(UICom_Label));
                UIObjectFactory.SetPackageItemExtension(UICom_ItemType.URL,
                    typeof(UICom_ItemType));
                sample = UIButton_GoodsItem_Store.CreateInstance();
                if (sample == null || sample.width <= 0f || sample.height <= 0f ||
                    sample.com_Quality == null || sample.com_ItemName == null ||
                    sample.com_Price == null)
                    throw new InvalidOperationException("原版商店商品卡片未就绪");
                float nativeWidth = sample.width;
                float nativeHeight = sample.height;
                sample.Dispose();
                sample = null;

                layout = PropPopupLayout.Expand(pane, background, footer);
                if (layout == null || !TryGetViewport(pane, layout, description, preview, name,
                    nativeWidth, out float left, out float top, out float width, out float height))
                    return false;
                created = UISetting_Comp_Score_Desc.CreateInstance();
                if (created == null) return false;
                created.RemoveChildren(0, -1, true);
                created.SetSize(width, height);
                created.SetXY(left, top);
                ScrollPane scroll = created.scrollPane;
                if (scroll == null) throw new InvalidOperationException("原版滚动区域未提供滚动条");
                scroll.mouseWheelEnabled = true;
                scroll.touchEffect = true;
                scroll.bouncebackEffect = false;
                created.opaque = true;
                float available = Math.Min(width,
                    scroll.viewWidth > 0f ? scroll.viewWidth : width);
                float tileWidth = Math.Min(nativeWidth, Math.Max(90f, available - Padding * 2f));
                float scale = Math.Min(1f, tileWidth / nativeWidth);
                float tileHeight = nativeHeight * scale;
                int columns = Math.Max(1, (int)Math.Floor((available - Padding * 2f + ColumnGap) /
                    (tileWidth + ColumnGap)));
                var content = new GComponent {
                    name = "GiftGuide.CharacterSections", touchable = false
                };
                float y = 7f;
                y = AddSection(content, usages, 0, "契约材料", name,
                    available, columns, nativeWidth, nativeHeight, scale, tileWidth, tileHeight, y);
                y = AddSection(content, usages, 1, "潜能材料", name,
                    available, columns, nativeWidth, nativeHeight, scale, tileWidth, tileHeight, y);
                y = AddSection(content, usages, 2, "偏好礼物", name,
                    available, columns, nativeWidth, nativeHeight, scale, tileWidth, tileHeight, y);
                if (y <= 7f) { content.Dispose(); return false; }
                content.SetSize(available, y + 10f);
                created.AddChild(content);
                pane.AddChild(created);
                created.EnsureBoundsCorrect();
                ActivateNameScroll(content);
                _viewport = created;
                _pane = pane;
                _item = itemId;
                _layout = layout;
                layout = null;
                created = null;
                return true;
            } catch (Exception e) {
                _retry = DateTime.UtcNow.AddSeconds(5);
                SdkLog.Warn("GiftGuide", "角色材料商店列表暂不可用：" + e.Message);
                return false;
            } finally {
                if (sample != null && !sample.isDisposed) sample.Dispose();
                if (created != null && !created.isDisposed) {
                    created.RemoveFromParent();
                    created.Dispose();
                }
                layout?.Dispose();
            }
        }

        private static bool TryGetViewport(GComponent pane, PropPopupLayout layout,
            GTextField description, GButton preview, GTextField name, float nativeWidth,
            out float x, out float y, out float width, out float height) {
            x = y = width = height = 0f;
            if (pane.width < 300f || pane.height < 200f) return false;
            // Prefer the NEW right-hand strip. The original description,
            // icon/loader, nested contentPane and purchase buttons never move
            // and can never overlap a list in this separate horizontal area.
            float rightSpace = layout.Right - layout.RightRailLeft;
            float leftSpace = layout.LeftRailRight - layout.Left;
            float required = Math.Max(150f, nativeWidth * .78f);
            if (rightSpace >= required || leftSpace >= required) {
                bool onRight = rightSpace >= required && rightSpace >= leftSpace - 12f;
                x = onRight ? layout.RightRailLeft : layout.Left;
                width = onRight ? rightSpace : leftSpace;
                y = layout.Top;
                height = Math.Min(layout.Bottom - y, pane.height - y - 8f);
                if (height >= 100f) return true;
            }
            // Reduced-width screens: use only the spare space BETWEEN the
            // native description and footer row (never cover purchase controls).
            float rendered = description.textHeight;
            float descHeight = rendered > 0f
                ? Math.Max(24f, Math.Min(description.height, rendered + 12f))
                : description.height;
            float textBottom = description.y + descHeight;
            y = Math.Max(preview.y + preview.height,
                Math.Max(name.y + name.height, textBottom)) + 9f;
            x = layout.Left;
            width = layout.Right - layout.Left;
            height = layout.FooterTop(y) - y - 8f;
            return width >= 180f && height >= 66f && x >= 0f && y >= 0f &&
                x + width <= pane.width && y + height <= pane.height;
        }

        private static float AddSection(GComponent content, IReadOnlyList<CharacterMaterialUsage> all,
            int group, string heading, GTextField nameStyle, float available,
            int columns, float nativeWidth, float nativeHeight, float scale,
            float tileWidth, float tileHeight, float y) {
            var members = new List<CharacterMaterialUsage>();
            foreach (CharacterMaterialUsage item in all) {
                if (group == 0 && item.ContractMaterial ||
                    group == 1 && item.PotentialMaterial ||
                    group == 2 && item.GiftPreferred) members.Add(item);
            }
            if (members.Count == 0) return y;
            if (y > 7f) y += SectionGap;
            AddHeading(content, heading + " · " + members.Count + " 位角色",
                nameStyle, Padding, y, available - Padding * 2f);
            y += CaptionHeight + 3f;
            var list = new GList {
                name = "GiftGuide.StoreList." + group, touchable = false,
                layout = ListLayoutType.FlowHorizontal,
                columnGap = (int)ColumnGap,
                lineGap = (int)RowGap,
                columnCount = columns,
                autoResizeItem = false
            };
            int rows = (members.Count + columns - 1) / columns;
            float listHeight = rows * tileHeight + Math.Max(0, rows - 1) * RowGap;
            list.SetSize(available - Padding * 2f, listHeight);
            list.SetXY(Padding, y);
            content.AddChild(list);
            foreach (CharacterMaterialUsage usage in members) {
                // GList places wrappers by their declared DISPLAY size; the
                // complete native store card is uniformly scaled INSIDE its
                // wrapper only when a narrow screen forces that compromise.
                var wrapper = new GComponent { name = "GiftGuide.Item." + usage.HeroId,
                    touchable = false };
                wrapper.SetSize(tileWidth, tileHeight);
                UIButton_GoodsItem_Store item = BuildStoreCard(usage, group);
                item.SetXY(0f, 0f);
                item.SetScale(scale, scale);
                wrapper.AddChild(item);
                list.AddChild(wrapper);
            }
            list.EnsureBoundsCorrect();
            return y + listHeight;
        }

        private static UIButton_GoodsItem_Store BuildStoreCard(CharacterMaterialUsage usage, int group) {
            UIButton_GoodsItem_Store item = UIButton_GoodsItem_Store.CreateInstance();
            if (item == null || item.com_Quality == null || item.com_ItemName?.txt_Name == null ||
                item.com_Price?.txt_SalePrice == null || item.loader_Goods == null)
                throw new InvalidOperationException("商店角色卡片字段缺失");
            item.name = "GiftGuide.Character." + usage.HeroId + "." + group;
            item.touchable = false;
            item.grayed = false;
            item.isEmpty.selectedIndex = 0;
            item.sellOut.selectedIndex = 0;
            item.owned.selectedIndex = 0;
            item.isrecharge.selectedIndex = 0;
            item.isRecharge.selectedIndex = 0;
            item.isVideo.selectedIndex = 0;
            item.com_Quality.quality.selectedIndex = usage.ContractSigned
                ? (int)QualityType.Orange : (int)QualityType.Purple;
            if (item.com_Quality.isEmpty != null)
                item.com_Quality.isEmpty.selectedIndex = 0;

            // Preserve the exact original goods-loader rectangle and child
            // order while substituting the external-texture-aware loader.
            GLoader previous = item.loader_Goods;
            int order = item.GetChildIndex(previous);
            var portrait = new TextureLoader {
                name = "GiftGuide.CharacterTexture", touchable = false,
                autoSize = false, fill = FillType.ScaleNoBorder
            };
            portrait.SetSize(previous.width, previous.height);
            portrait.SetXY(previous.x, previous.y);
            item.RemoveChild(previous, true);
            item.AddChildAt(portrait, order);
            item.loader_Goods = portrait;
            portrait.url = usage.Portrait;

            item.txt_Remaining.visible = false;
            item.txt_soldout.visible = false;
            item.txt_timeTip.visible = false;
            item.txt_FirstRecharge.visible = false;
            item.txt_FirstRechargeNum.visible = false;
            item.txt_ItemNum.visible = false;
            item.com_Label.visible = false;
            item.com_ItemType.visible = false;
            item.com_Price.isDiscount.selectedIndex = 0;
            item.com_Price.txt_OriginalPrice.visible = false;
            item.com_Price.txt_DiscountPercent.visible = false;

            string displayName = string.IsNullOrWhiteSpace(usage.Title) || usage.Title == usage.Name
                ? usage.Name : usage.Title + " · " + usage.Name;
            GTextField name = item.com_ItemName.txt_Name;
            name.autoSize = AutoSizeType.None;
            name.text = displayName;

            string status = group == 0
                ? (usage.ContractSigned ? "已缔结契约" : usage.ContractMissing > 0
                    ? "缺少 " + usage.ContractMissing : "材料已齐")
                : group == 1
                    ? (usage.PotentialAwakened ? "已激发潜能" : usage.PotentialMissing > 0
                        ? "缺少 " + usage.PotentialMissing : "材料已齐")
                    : "偏好礼物";
            item.com_Price.txt_SalePrice.text = status;
            return item;
        }

        private static void ActivateNameScroll(GComponent content) {
            // In the native shop TryScrollTextField runs AFTER the card is on
            // stage. Running it while constructing an offstage card prevents
            // the game from starting its marquee for longer character titles.
            for (int i = 0; i < content.numChildren; i++) {
                if (!(content.GetChildAt(i) is GList list)) continue;
                for (int j = 0; j < list.numChildren; j++) {
                    if (!(list.GetChildAt(j) is GComponent wrapper) ||
                        wrapper.numChildren == 0 ||
                        !(wrapper.GetChildAt(0) is UIButton_GoodsItem_Store item)) continue;
                    GTextField name = item.com_ItemName.txt_Name;
                    AutoSizeType original = name.autoSize;
                    name.autoSize = AutoSizeType.Both;
                    name.TryScrollTextField(name.text);
                    name.autoSize = original;
                }
            }
        }

        private static void AddHeading(GComponent parent, string caption, GTextField style,
            float x, float y, float width) {
            var label = new GTextField {
                text = caption, autoSize = AutoSizeType.None, singleLine = true,
                verticalAlign = VertAlignType.Middle, touchable = false
            };
            TextFormat format = label.textFormat;
            if (style != null && !style.isDisposed) format.CopyFrom(style.textFormat);
            format.size = 20;
            format.bold = true;
            label.textFormat = format;
            label.SetSize(width, CaptionHeight);
            label.SetXY(x, y);
            parent.AddChild(label);
        }

        internal void Clear() {
            if (_viewport != null && !_viewport.isDisposed) {
                _viewport.RemoveFromParent();
                _viewport.Dispose();
            }
            _layout?.Dispose();
            _viewport = null;
            _layout = null;
            _pane = null;
            _item = 0;
        }
        public void Dispose() => Clear();
    }
}
