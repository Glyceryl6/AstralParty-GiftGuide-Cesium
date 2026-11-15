using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using Tools;
using UI;
using UnityEngine;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// A native-quality frame and TextureLoader for each character, with
    /// readable labels positioned INSIDE one fixed-size card. Sections are
    /// laid out as real multi-column GLists in the original window's lower
    /// extension, never in a separately drawn left/right panel.
    /// </summary>
    internal sealed class PropCharacterAvatarGrid : IDisposable {
        private const float Padding = 9f;
        private const float CardWidth = 184f;
        private const float CardHeight = 194f;
        private const int ColumnGap = 9;
        private const int RowGap = 9;
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
            try {
                UIObjectFactory.SetPackageItemExtension(UISetting_Comp_Score_Desc.URL,
                    typeof(UISetting_Comp_Score_Desc));
                UIObjectFactory.SetPackageItemExtension(UICom_GoodsQuality_Store.URL,
                    typeof(UICom_GoodsQuality_Store));
                UIObjectFactory.SetPackageItemExtension(UICom_GoodsItemName.URL,
                    typeof(UICom_GoodsItemName));
                // Profiles are external textures. They MUST be loaded through
                // the game's TextureLoader, not a plain package GLoader.
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
                float inner = Math.Max(1f, available - 2f * Padding);
                float cellWidth = Math.Min(CardWidth, inner);
                float cellHeight = CardHeight;
                int columns = Math.Max(1, (int)Math.Floor((inner + ColumnGap) /
                    (cellWidth + ColumnGap)));
                var content = new GComponent {
                    name = "GiftGuide.CharacterSections", touchable = false
                };
                float y = Padding;
                y = AddSection(content, usages, 0, "契约", name, description,
                    inner, columns, cellWidth, cellHeight, y);
                y = AddSection(content, usages, 1, "潜能", name, description,
                    inner, columns, cellWidth, cellHeight, y);
                y = AddSection(content, usages, 2, "偏好礼物", name, description,
                    inner, columns, cellWidth, cellHeight, y);
                if (y <= Padding) { content.Dispose(); return false; }
                content.SetSize(available, y + Padding);
                created.AddChild(content);
                pane.AddChild(created);
                created.EnsureBoundsCorrect();
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
                if (created != null && !created.isDisposed) {
                    created.RemoveFromParent(); created.Dispose();
                }
                layout?.Dispose();
            }
        }

        private static float AddSection(GComponent content, IReadOnlyList<CharacterMaterialUsage> all,
            int group, string heading, GTextField nameStyle, GTextField descriptionStyle,
            float width, int columns,
            float cellWidth, float cellHeight, float y) {
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
            foreach (CharacterMaterialUsage usage in members)
                list.AddChild(BuildCharacterCard(usage, group, descriptionStyle, cellWidth));
            list.EnsureBoundsCorrect();
            return y + listHeight;
        }

        private static GComponent BuildCharacterCard(CharacterMaterialUsage usage,
            int group, GTextField descriptionStyle, float width) {
            var card = new GComponent {
                name = "GiftGuide.Character." + usage.HeroId + "." + group,
                touchable = false, opaque = false
            };
            card.SetSize(width, CardHeight);
            // Native frame contains both quality images. A texture loader
            // placed BETWEEN them preserves the visible authored border.
            UICom_GoodsQuality_Store quality = UICom_GoodsQuality_Store.CreateInstance();
            if (quality == null || quality.width < 2f || quality.height < 2f)
                throw new InvalidOperationException("Native quality frame not ready");
            if (quality.quality != null)
                quality.quality.selectedIndex = usage.ContractSigned
                    ? (int)QualityType.Orange : (int)QualityType.Purple;
            if (quality.isEmpty != null) quality.isEmpty.selectedIndex = 0;
            float frameSize = Math.Min(120f, width - 22f);
            float frameScale = Math.Min(frameSize / quality.width,
                frameSize / quality.height);
            var portrait = new TextureLoader {
                name = "GiftGuide.ProfilePhoto", touchable = false,
                fill = FillType.ScaleNoBorder
            };
            portrait.SetSize(Math.Max(1f, quality.width - 16f),
                Math.Max(1f, quality.height - 16f));
            portrait.SetXY(8f, 8f);
            quality.AddChildAt(portrait, Math.Min(1, quality.numChildren));
            portrait.url = usage.Portrait;
            quality.SetScale(frameScale, frameScale);
            quality.SetXY((width - quality.width * frameScale) * .5f, 1f);
            card.AddChild(quality);

            UICom_GoodsItemName nativeName = UICom_GoodsItemName.CreateInstance();
            if (nativeName == null || nativeName.txt_Name == null)
                throw new InvalidOperationException("Native item-name label not ready");
            nativeName.SetXY(4f, 122f);
            nativeName.SetSize(width - 8f, 47f);
            GTextField label = nativeName.txt_Name;
            label.SetXY(0f, 0f);
            label.SetSize(width - 8f, 47f);
            label.autoSize = AutoSizeType.None;
            label.singleLine = false;
            label.align = AlignType.Center;
            label.verticalAlign = VertAlignType.Middle;
            TextFormat nameFont = label.textFormat;
            nameFont.size = Math.Max(13, Math.Min(18, nameFont.size));
            label.textFormat = nameFont;
            label.text = string.IsNullOrWhiteSpace(usage.Title) || usage.Title == usage.Name
                ? usage.Name : usage.Title + " / " + usage.Name;
            nativeName.visible = true;
            card.AddChild(nativeName);

            string status = group == 0
                ? (usage.ContractSigned ? "" : usage.ContractMissing > 0
                    ? "\u7f3a\u5c11 " + usage.ContractMissing : "\u6750\u6599\u5df2\u9f50")
                : group == 1
                    ? (usage.PotentialAwakened ? "" : usage.PotentialMissing > 0
                        ? "\u7f3a\u5c11 " + usage.PotentialMissing : "\u6750\u6599\u5df2\u9f50")
                    : "\u504f\u597d\u793c\u7269";
            if (status.Length > 0) {
                var countLabel = new GRichTextField {
                    name = "GiftGuide.MaterialCount", touchable = false,
                    autoSize = AutoSizeType.None, singleLine = true,
                    align = AlignType.Center,
                    verticalAlign = VertAlignType.Middle
                };
                TextFormat priceFont = countLabel.textFormat;
                priceFont.CopyFrom(descriptionStyle.textFormat);
                priceFont.size = Math.Max(13, Math.Min(16, priceFont.size));
                countLabel.textFormat = priceFont;
                countLabel.SetXY(3f, 171f);
                countLabel.SetSize(width - 6f, 20f);
                countLabel.text = status;
                card.AddChild(countLabel);
            }
            return card;
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
