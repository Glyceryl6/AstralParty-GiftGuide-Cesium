using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using UI;
using UnityEngine;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// The three material groups share one native scroll pane. A character
    /// tile follows the store shelf, not the Hero card: store quality artwork
    /// (two original GImages), a TextureLoader portrait, a goods-name component
    /// and a rich-text price/status line. No game-owned widget is modified.
    /// </summary>
    internal sealed class PropCharacterAvatarGrid : IDisposable {
        private const float Padding = 16f;
        private const float ColumnGap = 10f;
        private const float RowGap = 15f;
        private const float CaptionHeight = 32f;
        private const float SectionGap = 13f;
        private const float AvatarLimit = 148f;
        private const float NameHeight = 43f;
        private const float StatusHeight = 28f;
        private const float CardPadding = 5f;

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
            try {
                layout = PropPopupLayout.Expand(pane, background, footer);
                if (layout == null || !TryGetViewport(pane, layout, description, preview, name,
                        out float left, out float top, out float width, out float height)) return false;

                // These are precisely the classes used by the current game's
                // Common_External shelf, and do not replace a built-in widget.
                UIObjectFactory.SetPackageItemExtension(UISetting_Comp_Score_Desc.URL,
                    typeof(UISetting_Comp_Score_Desc));
                UIObjectFactory.SetPackageItemExtension(UICom_GoodsQuality_Store.URL,
                    typeof(UICom_GoodsQuality_Store));
                UIObjectFactory.SetPackageItemExtension(UICom_GoodsItemName.URL,
                    typeof(UICom_GoodsItemName));
                created = UISetting_Comp_Score_Desc.CreateInstance();
                if (created == null) return false;
                // Only remove the example text. The ScrollPane itself belongs
                // to the component and is kept, including its native scrollbar.
                created.RemoveChildren(0, -1, true);
                created.SetSize(width, height);
                created.SetXY(left, top);
                ScrollPane scroll = created.scrollPane;
                if (scroll == null) throw new InvalidOperationException("原版滚动区域未提供滚动条");
                scroll.mouseWheelEnabled = true;
                scroll.touchEffect = true;
                scroll.bouncebackEffect = false;
                created.opaque = true;
                float available = Math.Min(width, scroll.viewWidth > 0f ? scroll.viewWidth : width);
                int columns = available >= 690f ? 4 : available >= 465f ? 3 : 2;
                float cellWidth = (available - Padding * 2f - ColumnGap * (columns - 1)) / columns;
                if (cellWidth < 95f) throw new InvalidOperationException("角色列表宽度不足");
                float avatarSize = Math.Min(AvatarLimit, cellWidth - CardPadding * 2f);
                float cardHeight = avatarSize + NameHeight + StatusHeight + 9f;

                var content = new GComponent { name = "GiftGuide.CharacterSections", touchable = false };
                float y = 8f;
                y = AddSection(content, usages, 0, "契约材料", description, name,
                    available, columns, cellWidth, avatarSize, cardHeight, y);
                y = AddSection(content, usages, 1, "潜能材料", description, name,
                    available, columns, cellWidth, avatarSize, cardHeight, y);
                y = AddSection(content, usages, 2, "偏好礼物", description, name,
                    available, columns, cellWidth, avatarSize, cardHeight, y);
                if (y <= 8f) { content.Dispose(); return false; }
                content.SetSize(available, y + 12f);
                created.AddChild(content);
                pane.AddChild(created);
                created.EnsureBoundsCorrect();
                _viewport = created;
                _pane = pane;
                _item = itemId;
                _layout = layout;
                layout = null;
                created = null;
                return true;
            } catch (Exception e) {
                _retry = DateTime.UtcNow.AddSeconds(5);
                SdkLog.Warn("GiftGuide", "角色材料滚动区域暂不可用：" + e.Message);
                return false;
            } finally {
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
            // The native description field may be an oversized empty box. The
            // rendered glyph height (with a safety margin) determines where
            // the list can start without covering the description's text.
            float rendered = description.textHeight;
            float usedHeight = rendered > 0f
                ? Math.Max(24f, Math.Min(description.height, rendered + 14f))
                : description.height;
            float descriptionBottom = description.y + usedHeight;
            float top = Math.Max(preview.y + preview.height,
                Math.Max(name.y + name.height, descriptionBottom)) + 17f;
            float left = Math.Max(8f, layout.Left);
            float right = Math.Min(pane.width - 8f, layout.Right);
            float bottom = Math.Min(pane.height - 8f, layout.FooterTop(top));
            x = left;
            y = top;
            width = right - left;
            height = bottom - top - 6f;
            return width >= 310f && height >= 135f && x >= 0f && y >= 0f &&
                x + width <= pane.width && y + height <= pane.height;
        }

        private static float AddSection(GComponent content, IReadOnlyList<CharacterMaterialUsage> all,
            int group, string heading, GTextField style, GTextField nameStyle,
            float available, int columns, float cellWidth, float avatarSize,
            float cardHeight, float y) {
            var members = new List<CharacterMaterialUsage>();
            foreach (CharacterMaterialUsage item in all) {
                if (group == 0 && item.ContractMaterial ||
                    group == 1 && item.PotentialMaterial ||
                    group == 2 && item.GiftPreferred) members.Add(item);
            }
            if (members.Count == 0) return y;
            if (y > 8f) y += SectionGap;
            AddText(content, heading + " · " + members.Count + " 位角色", nameStyle,
                21, Padding, y, available - Padding * 2f, CaptionHeight, false);
            y += CaptionHeight + 7f;
            for (int i = 0; i < members.Count; i++) {
                float x = Padding + (i % columns) * (cellWidth + ColumnGap);
                float cy = y + i / columns * (cardHeight + RowGap);
                AddCharacter(content, members[i], group, style,
                    x, cy, cellWidth, avatarSize);
            }
            int rows = (members.Count + columns - 1) / columns;
            return y + rows * cardHeight + Math.Max(0, rows - 1) * RowGap;
        }

        private static void AddCharacter(GComponent parent, CharacterMaterialUsage usage,
            int group, GTextField style, float x, float y, float cellWidth, float avatarSize) {
            float photoX = x + (cellWidth - avatarSize) * .5f;
            var portrait = new SquarePortraitClip {
                name = "GiftGuide.Portrait." + usage.HeroId, touchable = false
            };
            portrait.SetSize(avatarSize, avatarSize);
            portrait.SetXY(photoX, y);

            // The quality controller drives the same two GImages as in a
            // Button_GoodsItem_Store: orange/gold when contracted, purple else.
            UICom_GoodsQuality_Store frame = UICom_GoodsQuality_Store.CreateInstance();
            if (frame == null || frame.width <= 0f || frame.height <= 0f ||
                frame.quality == null || frame.quality.pageCount <= (int)QualityType.Orange)
                throw new InvalidOperationException("商店品质外框未就绪");
            frame.quality.selectedIndex = usage.ContractSigned
                ? (int)QualityType.Orange : (int)QualityType.Purple;
            if (frame.isEmpty != null) frame.isEmpty.selectedIndex = 0;
            float scale = Math.Min(avatarSize / frame.width, avatarSize / frame.height);
            frame.SetScale(scale, scale);
            frame.SetXY((avatarSize - frame.width * scale) * .5f,
                (avatarSize - frame.height * scale) * .5f);
            frame.touchable = false;
            portrait.AddChild(frame);

            // TextureLoader is the game's external portrait asset loader; a
            // plain GLoader can show an empty or fallback picture for this URL.
            var photo = new TextureLoader {
                name = "GiftGuide.CharacterTexture", touchable = false,
                autoSize = false, fill = FillType.ScaleNoBorder
            };
            float inset = Math.Max(6f, avatarSize * .075f);
            photo.SetXY(inset, inset);
            photo.SetSize(avatarSize - inset * 2f, avatarSize - inset * 2f);
            photo.url = usage.Portrait;
            portrait.AddChild(photo);
            parent.AddChild(portrait);

            UICom_GoodsItemName name = UICom_GoodsItemName.CreateInstance();
            if (name == null || name.txt_Name == null)
                throw new InvalidOperationException("商店商品名称组件未就绪");
            name.touchable = false;
            name.SetSize(cellWidth, NameHeight);
            name.SetXY(x, y + avatarSize + 5f);
            string title = string.IsNullOrWhiteSpace(usage.Title) || usage.Title == usage.Name
                ? usage.Name : usage.Title + "\n" + usage.Name;
            name.txt_Name.text = title;
            name.txt_Name.SetXY(0f, 0f);
            name.txt_Name.SetSize(cellWidth, NameHeight);
            name.txt_Name.autoSize = AutoSizeType.None;
            name.txt_Name.singleLine = false;
            name.txt_Name.align = AlignType.Center;
            name.txt_Name.verticalAlign = VertAlignType.Middle;
            TextFormat format = name.txt_Name.textFormat;
            format.size = 17;
            name.txt_Name.textFormat = format;
            parent.AddChild(name);

            string status = group == 0
                ? (usage.ContractSigned ? "已缔结契约" : usage.ContractMissing > 0
                    ? "缺少 " + usage.ContractMissing : "材料已齐")
                : group == 1
                    ? (usage.PotentialAwakened ? "已激发潜能" : usage.PotentialMissing > 0
                        ? "缺少 " + usage.PotentialMissing : "材料已齐")
                    : "偏好礼物";
            var price = new GRichTextField {
                name = "GiftGuide.MaterialCount", text = status,
                touchable = false, autoSize = AutoSizeType.None,
                singleLine = true, color = new Color(1f, .89f, .61f, 1f),
                align = AlignType.Center, verticalAlign = VertAlignType.Middle
            };
            TextFormat priceFormat = price.textFormat;
            priceFormat.size = 17;
            priceFormat.bold = true;
            price.textFormat = priceFormat;
            price.SetSize(cellWidth, StatusHeight);
            price.SetXY(x, y + avatarSize + NameHeight + 6f);
            parent.AddChild(price);
        }

        private sealed class SquarePortraitClip : GComponent {
            internal SquarePortraitClip() { SetupOverflow(OverflowType.Hidden); }
        }

        private static GTextField AddText(GComponent parent, string text, GTextField style,
            int size, float x, float y, float width, float height, bool center) {
            var label = new GTextField {
                text = text, autoSize = AutoSizeType.None, singleLine = true,
                align = center ? AlignType.Center : AlignType.Left,
                verticalAlign = VertAlignType.Middle, touchable = false
            };
            TextFormat format = label.textFormat;
            if (style != null && !style.isDisposed) format.CopyFrom(style.textFormat);
            format.size = size;
            label.textFormat = format;
            if (style != null && !style.isDisposed) {
                label.stroke = style.stroke;
                label.strokeColor = style.strokeColor;
            }
            label.SetSize(width, height);
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
