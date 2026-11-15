using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using Tools;
using UI;
using UnityEngine;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Independent, scrollable character catalog. Its native quality frames use
    /// the same resource and dimensions as the store; portrait, name and missing
    /// count are children of one GList cell, never free-floating window objects.
    /// </summary>
    internal sealed class PropCharacterAvatarGrid : IDisposable {
        private const float Padding = 7f;
        private const int ColumnGap = 4;
        private const int RowGap = 5;
        private const float CaptionHeight = 27f;
        private const float SectionGap = 9f;
        private const float TextGap = 3f;
        private const float NameHeight = 39f;
        private const float StatusHeight = 24f;

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
                if (sample?.com_Quality == null || sample.loader_Goods == null ||
                    sample.com_Quality.width < 10f || sample.com_Quality.height < 10f)
                    throw new InvalidOperationException("原版商店品质组件尚未就绪");

                // Only expand the native background horizontally. Other popup
                // children (including loader_icon, _contentPane and buttons)
                // keep their authored positions and untouched parent relations.
                layout = PropPopupLayout.Expand(pane, background, footer);
                if (layout == null || !TryGetViewport(pane, layout, description, preview, name,
                    out float left, out float top, out float width, out float height)) return false;
                created = UISetting_Comp_Score_Desc.CreateInstance();
                if (created == null) return false;
                created.RemoveChildren(0, -1, true);
                created.SetSize(width, height);
                created.SetXY(left, top);
                ScrollPane scroll = created.scrollPane;
                if (scroll == null) throw new InvalidOperationException("列表滚动条不可用");
                scroll.mouseWheelEnabled = true;
                scroll.touchEffect = true;
                scroll.bouncebackEffect = false;
                created.opaque = true;
                float available = Math.Min(width, scroll.viewWidth > 0f ? scroll.viewWidth : width);
                float naturalWidth = Math.Max(sample.com_Quality.width,
                    Math.Max(110f, sample.loader_Goods.width));
                // Fit several native-sized quality frames whenever the newly
                // extended rail allows it; never scale the whole store card.
                float inner = Math.Max(1f, available - Padding * 2f);
                int columns = Math.Max(1, Math.Min(4, (int)Math.Floor((inner + ColumnGap) /
                    (naturalWidth + ColumnGap))));
                // A narrow 2-column grid is preferable to one enormous
                // store card per row when both cards remain legible.
                if (columns == 1 && inner >= Math.Max(310f, naturalWidth * 1.45f))
                    columns = 2;
                float cellWidth = columns > 1
                    ? Math.Min(naturalWidth, (inner - (columns - 1) * ColumnGap) / columns - 1f)
                    : Math.Min(naturalWidth, inner);
                float qualityScale = Math.Min(1f, cellWidth / naturalWidth);
                float qualityHeight = sample.com_Quality.height * qualityScale;
                float cellHeight = qualityHeight + TextGap + NameHeight + StatusHeight + 5f;

                var content = new GComponent { name = "GiftGuide.CharacterSections", touchable = false };
                float y = Padding;
                y = AddSection(content, usages, 0, "契约材料", name, description, sample,
                    available, columns, cellWidth, cellHeight, qualityScale, y);
                y = AddSection(content, usages, 1, "潜能材料", name, description, sample,
                    available, columns, cellWidth, cellHeight, qualityScale, y);
                y = AddSection(content, usages, 2, "偏好礼物", name, description, sample,
                    available, columns, cellWidth, cellHeight, qualityScale, y);
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
                layout = null;
                created = null;
                return true;
            } catch (Exception e) {
                _retry = DateTime.UtcNow.AddSeconds(5);
                SdkLog.Warn("GiftGuide", "角色材料列表暂不可用：" + e.Message);
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
            GTextField description, GButton preview, GTextField name,
            out float x, out float y, out float width, out float height) {
            x = y = width = height = 0f;
            if (pane.width < 300f || pane.height < 200f) return false;
            float rightSpace = layout.Right - layout.RightRailLeft;
            float leftSpace = layout.LeftRailRight - layout.Left;
            if (Math.Max(leftSpace, rightSpace) >= 140f) {
                bool right = rightSpace >= leftSpace;
                x = right ? layout.RightRailLeft : layout.Left;
                width = Math.Max(leftSpace, rightSpace);
                y = layout.Top;
                height = Math.Min(layout.Bottom - y, pane.height - y - 8f);
                // The viewport is bounded by the unchanged native background;
                // all sections may be taller, but only INSIDE this scroll pane.
                return height >= 95f && x >= 0f && x + width <= pane.width;
            }
            float textBottom = description.y + Math.Max(description.height,
                Math.Min(description.textHeight + 10f, description.height));
            y = Math.Max(preview.y + preview.height,
                Math.Max(name.y + name.height, textBottom)) + 8f;
            x = layout.Left;
            width = layout.Right - x;
            height = layout.FooterTop(y) - y - 6f;
            return width >= 150f && height >= 70f && x >= 0f && y >= 0f &&
                x + width <= pane.width && y + height <= pane.height;
        }

        private static float AddSection(GComponent content, IReadOnlyList<CharacterMaterialUsage> all,
            int group, string heading, GTextField nameStyle, GTextField descriptionStyle,
            UIButton_GoodsItem_Store sample, float available, int columns, float cellWidth,
            float cellHeight, float scale, float y) {
            var members = new List<CharacterMaterialUsage>();
            foreach (CharacterMaterialUsage item in all) {
                if ((group == 0 && item.ContractMaterial) ||
                    (group == 1 && item.PotentialMaterial) ||
                    (group == 2 && item.GiftPreferred)) members.Add(item);
            }
            if (members.Count == 0) return y;
            if (y > Padding) y += SectionGap;
            AddLabel(content, heading + "（" + members.Count + "）", nameStyle,
                18, Padding, y, available - 2f * Padding, CaptionHeight, bold: true);
            y += CaptionHeight + 3f;
            var list = new GList {
                name = "GiftGuide.StoreList." + group,
                layout = ListLayoutType.FlowHorizontal,
                columnGap = ColumnGap,
                lineGap = RowGap,
                columnCount = columns,
                autoResizeItem = false,
                touchable = false
            };
            int rows = (members.Count + columns - 1) / columns;
            float listHeight = rows * cellHeight + Math.Max(0, rows - 1) * RowGap;
            list.SetSize(available - 2f * Padding, listHeight);
            list.SetXY(Padding, y);
            content.AddChild(list);
            foreach (CharacterMaterialUsage usage in members) {
                GComponent card = BuildCard(usage, group, sample, nameStyle, descriptionStyle,
                    cellWidth, cellHeight, scale);
                list.AddChild(card);
            }
            list.EnsureBoundsCorrect();
            return y + listHeight;
        }

        private static GComponent BuildCard(CharacterMaterialUsage usage, int group,
            UIButton_GoodsItem_Store sample, GTextField nameStyle, GTextField descriptionStyle,
            float width, float height, float scale) {
            var card = new GComponent {
                name = "GiftGuide.Character." + usage.HeroId + "." + group,
                touchable = false
            };
            card.SetSize(width, height);
            // The native quality component is a child of this exact item, as
            // in StorePanel. Its size and both controller-driven frames remain
            // authored by the game's Common_External package.
            UICom_GoodsQuality_Store frame = UICom_GoodsQuality_Store.CreateInstance();
            if (frame == null) throw new InvalidOperationException("商店品质框缺失");
            frame.quality.selectedIndex = usage.ContractSigned
                ? (int)QualityType.Orange : (int)QualityType.Purple;
            if (frame.isEmpty != null) frame.isEmpty.selectedIndex = 0;
            frame.touchable = false;
            frame.SetScale(scale, scale);
            frame.SetXY((width - frame.width * scale) * .5f, 0f);
            card.AddChild(frame);

            // Portrait geometry is copied from the actual shelf template,
            // relative to its quality component rather than the popup pane.
            var portrait = new TextureLoader {
                name = "GiftGuide.Portrait", touchable = false,
                autoSize = false, fill = FillType.ScaleNoBorder
            };
            portrait.SetSize(sample.loader_Goods.width, sample.loader_Goods.height);
            portrait.SetXY(sample.loader_Goods.x - sample.com_Quality.x,
                sample.loader_Goods.y - sample.com_Quality.y);
            portrait.url = usage.Portrait;
            frame.AddChild(portrait);

            // All portrait and text objects belong to the quality component
            // itself (NOT to the popup or a neighboring GList child). The
            // GList card only allocates layout bounds for this component.
            string display = string.IsNullOrWhiteSpace(usage.Title) || usage.Title == usage.Name
                ? usage.Name : usage.Title + " · " + usage.Name;
            float sourceWidth = width / Math.Max(.1f, scale);
            float sourceX = (frame.width - sourceWidth) * .5f;
            float textTop = frame.height + TextGap / Math.Max(.1f, scale);
            int nameSize = Math.Max(15, (int)Math.Round(16f / Math.Max(.1f, scale)));
            int statusSize = Math.Max(13, (int)Math.Round(14f / Math.Max(.1f, scale)));
            var name = AddLabel(frame, display, nameStyle, nameSize,
                sourceX + 1f, textTop, sourceWidth - 2f,
                NameHeight / Math.Max(.1f, scale), bold: false);
            name.name = "GiftGuide.CharacterName";
            name.singleLine = true;
            string status = group == 0
                ? (usage.ContractSigned ? "已缔结契约" : usage.ContractMissing > 0
                    ? "缺少 " + usage.ContractMissing : "材料已齐")
                : group == 1
                    ? (usage.PotentialAwakened ? "已激发潜能" : usage.PotentialMissing > 0
                        ? "缺少 " + usage.PotentialMissing : "材料已齐")
                    : "偏好礼物";
            // The popup description uses the game's Chinese glyph font; the
            // store price numeral font is deliberately not reused here.
            AddLabel(frame, status, descriptionStyle, statusSize,
                sourceX + 1f, textTop + NameHeight / Math.Max(.1f, scale),
                sourceWidth - 2f, StatusHeight / Math.Max(.1f, scale), bold: false);
            return card;
        }

        private static void StartNativeNameScroll(GComponent content) {
            // Run the game's marquee only AFTER the text is on stage, so long
            // character titles are never permanently clipped at one line.
            for (int i = 0; i < content.numChildren; i++) {
                if (!(content.GetChildAt(i) is GList list)) continue;
                for (int j = 0; j < list.numChildren; j++) {
                    if (!(list.GetChildAt(j) is GComponent card)) continue;
                    for (int k = 0; k < card.numChildren; k++) {
                        if (!(card.GetChildAt(k) is UICom_GoodsQuality_Store frame)) continue;
                        for (int n = 0; n < frame.numChildren; n++) {
                            if (!(frame.GetChildAt(n) is GTextField field) ||
                                field.name != "GiftGuide.CharacterName") continue;
                            AutoSizeType previous = field.autoSize;
                            field.autoSize = AutoSizeType.Both;
                            field.TryScrollTextField(field.text);
                            field.autoSize = previous;
                        }
                    }
                }
            }
        }

        private static GTextField AddLabel(GComponent parent, string caption, GTextField native,
            int fontSize, float x, float y, float width, float height, bool bold) {
            var label = new GTextField {
                text = caption, autoSize = AutoSizeType.None,
                touchable = false, align = AlignType.Center,
                verticalAlign = VertAlignType.Middle
            };
            TextFormat format = label.textFormat;
            if (native != null && !native.isDisposed) format.CopyFrom(native.textFormat);
            format.size = fontSize;
            format.bold = bold;
            label.textFormat = format;
            label.stroke = native != null ? native.stroke : 0f;
            label.strokeColor = native != null ? native.strokeColor : Color.black;
            label.SetSize(Math.Max(1f, width), height);
            label.SetXY(x, y);
            parent.AddChild(label);
            return label;
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
