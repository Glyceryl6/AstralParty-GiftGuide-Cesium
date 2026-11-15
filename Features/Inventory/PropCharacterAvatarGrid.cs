#nullable enable
using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using FairyGUI.Utils;
using Tools;
using UI;
using UnityEngine;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// A scrollable lower extension of the original item window. Character
    /// portraits use the store quality frame and TextureLoader; names and
    /// material counts use the game's own store text components.
    /// </summary>
    internal sealed class PropCharacterAvatarGrid : IDisposable {
        private const float Padding = 9f;
        private const float CardWidth = 184f;
        private const float CardHeight = 194f;
        private const int ColumnGap = 9;
        private const int RowGap = 9;
        private const float HeadingHeight = 27f;
        private const float SectionGap = 10f;

        private GuideViewport? _viewport;
        private GComponent? _pane;
        private PropPopupLayout? _layout;
        private int _item;
        private DateTime _retry;

        private sealed class GuideViewport : GComponent {
            internal GuideViewport() {
                // SetupOverflow(Scroll) does NOT create a ScrollPane in the
                // game's FairyGUI implementation. Only SetupScroll does.
                // Pass a minimal native scroll config: vertical + auto bar,
                // no margins, no extra header/footer and four null URLs.
                // ReadS encodes null as 0xFFFE (big-endian UInt16).
                SetupScroll(new ByteBuffer(new byte[] {
                    1, 2, // ScrollType.Vertical, ScrollBarDisplayType.Auto
                    0, 0, 0, 0, // flags
                    0, // no scrollbar margins
                    0xff, 0xfe, 0xff, 0xfe,
                    0xff, 0xfe, 0xff, 0xfe
                }));
            }
        }

        internal bool Alive => _viewport != null && !_viewport.isDisposed && _viewport.parent != null;
        internal bool IsFor(GComponent pane, int item) => ReferenceEquals(_pane, pane) && _item == item && Alive;

        internal bool TryShow(GComponent pane, int itemId, GTextField? description, GButton? preview,
            GTextField? name, GLabel background, IReadOnlyList<GObject> footer,
            IReadOnlyList<CharacterMaterialUsage> usages) {
            if (pane == null || pane.isDisposed || background == null || background.isDisposed ||
                usages == null || usages.Count == 0 || DateTime.UtcNow < _retry ||
                !UiPackageWarmup.Ready()) return false;

            GuideViewport? created = null;
            PropPopupLayout? layout = null;
            string stage = "注册商店组件";
            try {
                UIObjectFactory.SetPackageItemExtension(UICom_GoodsQuality_Store.URL,
                    typeof(UICom_GoodsQuality_Store));
                UIObjectFactory.SetPackageItemExtension(UICom_GoodsItemName.URL,
                    typeof(UICom_GoodsItemName));

                stage = "扩展原版弹窗";
                layout = PropPopupLayout.Expand(pane, background, footer);
                if (layout == null || layout.GuideWidth < 130f || layout.GuideHeight < 95f)
                    return false;

                stage = "创建角色列表滚动区域";
                created = new GuideViewport {
                    name = "GiftGuide.ScrollViewport",
                    touchable = true,
                    opaque = false
                };
                created.SetSize(layout.GuideWidth, layout.GuideHeight);
                created.SetXY(layout.GuideLeft, layout.GuideTop);
                ScrollPane? scroll = created.scrollPane;
                if (scroll == null)
                    throw new InvalidOperationException("FairyGUI ScrollPane 创建失败");
                scroll.mouseWheelEnabled = true;
                scroll.touchEffect = true;
                scroll.bouncebackEffect = false;
                scroll.scrollStep = CardHeight + RowGap;

                float available = Math.Max(1f, Math.Min(layout.GuideWidth,
                    scroll.viewWidth > 0f ? scroll.viewWidth : layout.GuideWidth));
                float inner = Math.Max(1f, available - 2f * Padding);
                float cellWidth = Math.Min(CardWidth, inner);
                float cellHeight = CardHeight;
                int columns = Math.Max(1, (int)Math.Floor((inner + ColumnGap) /
                    (cellWidth + ColumnGap)));

                stage = "生成契约、潜能和偏好礼物分组";
                var content = new GComponent {
                    name = "GiftGuide.CharacterSections",
                    touchable = false,
                    opaque = false
                };
                var scrollLabels = new List<GTextField>();
                float y = Padding;
                y = AddSection(content, usages, 0, "契约", name, description,
                    inner, columns, cellWidth, cellHeight, y, scrollLabels);
                y = AddSection(content, usages, 1, "潜能", name, description,
                    inner, columns, cellWidth, cellHeight, y, scrollLabels);
                y = AddSection(content, usages, 2, "偏好礼物", name, description,
                    inner, columns, cellWidth, cellHeight, y, scrollLabels);
                if (y <= Padding) {
                    content.Dispose();
                    return false;
                }

                content.SetSize(available, y + Padding);
                created.AddChild(content);
                created.EnsureBoundsCorrect();
                scroll.UpdateScrollBarVisible();

                stage = "添加角色列表到原版窗口";
                pane.AddChild(created);
                foreach (GTextField label in scrollLabels) {
                    if (label == null || label.isDisposed || !label.onStage) continue;
                    AutoSizeType autoSize = label.autoSize;
                    try {
                        label.autoSize = AutoSizeType.Both;
                        label.TryScrollTextField(label.text);
                    } catch (Exception ex) {
                        SdkLog.Warn("GiftGuide", "角色名称滚动显示失败：" + ex.Message);
                    } finally {
                        if (!label.isDisposed) label.autoSize = autoSize;
                    }
                }
                _viewport = created;
                _pane = pane;
                _item = itemId;
                _layout = layout;
                created = null;
                layout = null;
                return true;
            } catch (Exception ex) {
                _retry = DateTime.UtcNow.AddSeconds(5);
                // Keep the stack trace and the failed stage. A message-only
                // warning hid the actual null component in previous builds.
                SdkLog.Warn("GiftGuide", "角色列表暂不可用（" + stage + "）：" + ex);
                return false;
            } finally {
                if (created != null && !created.isDisposed) {
                    created.RemoveFromParent();
                    created.Dispose();
                }
                layout?.Dispose();
            }
        }

        private static float AddSection(GComponent content, IReadOnlyList<CharacterMaterialUsage> all,
            int group, string heading, GTextField? nameStyle, GTextField? descriptionStyle,
            float width, int columns, float cellWidth, float cellHeight, float y,
            List<GTextField> scrollLabels) {
            var members = new List<CharacterMaterialUsage>();
            foreach (CharacterMaterialUsage? usage in all) {
                if (usage == null) continue;
                if ((group == 0 && usage.ContractMaterial) ||
                    (group == 1 && usage.PotentialMaterial) ||
                    (group == 2 && usage.GiftPreferred)) members.Add(usage);
            }
            if (members.Count == 0) return y;
            if (y > Padding) y += SectionGap;

            GTextField title = new GTextField {
                text = heading + "（" + members.Count + "）",
                touchable = false,
                autoSize = AutoSizeType.None,
                verticalAlign = VertAlignType.Middle,
                align = AlignType.Left
            };
            TextFormat headingFormat = title.textFormat;
            CopyStyle(headingFormat, nameStyle, descriptionStyle, 16, 21);
            headingFormat.bold = true;
            title.textFormat = headingFormat;
            title.SetXY(Padding, y);
            title.SetSize(width, HeadingHeight);
            content.AddChild(title);
            y += HeadingHeight + 3f;

            var list = new GList {
                name = "GiftGuide.NativeStoreItems." + group,
                layout = ListLayoutType.FlowHorizontal,
                columnGap = ColumnGap,
                lineGap = RowGap,
                columnCount = columns,
                autoResizeItem = false,
                touchable = false
            };
            int rows = (members.Count + columns - 1) / columns;
            float listHeight = rows * cellHeight + Math.Max(0, rows - 1) * RowGap;
            list.SetSize(width, listHeight);
            list.SetXY(Padding, y);
            content.AddChild(list);
            foreach (CharacterMaterialUsage usage in members)
                list.AddChild(BuildCharacterCard(usage, group, descriptionStyle, nameStyle,
                    cellWidth, cellHeight, scrollLabels));
            list.EnsureBoundsCorrect();
            return y + listHeight;
        }

        private static GComponent BuildCharacterCard(CharacterMaterialUsage usage,
            int group, GTextField? descriptionStyle, GTextField? nameStyle,
            float width, float height, List<GTextField> scrollLabels) {
            var card = new GComponent {
                name = "GiftGuide.Character." + usage.HeroId + "." + group,
                touchable = false,
                opaque = false
            };
            card.SetSize(width, height);

            UICom_GoodsQuality_Store quality = UICom_GoodsQuality_Store.CreateInstance();
            if (quality == null || quality.width < 2f || quality.height < 2f)
                throw new InvalidOperationException("原版商品品质框尚未就绪");
            if (quality.quality != null)
                quality.quality.selectedIndex = usage.ContractSigned
                    ? (int)QualityType.Orange : (int)QualityType.Purple;
            if (quality.isEmpty != null) quality.isEmpty.selectedIndex = 0;

            float frameSize = Math.Min(120f, width - 22f);
            float frameScale = Math.Min(frameSize / quality.width,
                frameSize / quality.height);
            var portrait = new TextureLoader {
                name = "GiftGuide.ProfilePhoto",
                touchable = false,
                fill = FillType.ScaleNoBorder
            };
            portrait.SetSize(Math.Max(1f, quality.width - 16f),
                Math.Max(1f, quality.height - 16f));
            portrait.SetXY(8f, 8f);
            quality.AddChildAt(portrait, Math.Min(1, quality.numChildren));
            if (!string.IsNullOrWhiteSpace(usage.Portrait)) portrait.url = usage.Portrait;
            quality.SetScale(frameScale, frameScale);
            quality.SetXY((width - quality.width * frameScale) * .5f, 1f);
            card.AddChild(quality);

            UICom_GoodsItemName nativeName = UICom_GoodsItemName.CreateInstance();
            if (nativeName == null || nativeName.txt_Name == null)
                throw new InvalidOperationException("原版商品名称组件尚未就绪");
            nativeName.SetXY(4f, 122f);
            nativeName.SetSize(width - 8f, 47f);
            GTextField label = nativeName.txt_Name;
            label.SetXY(0f, 0f);
            label.SetSize(width - 8f, 47f);
            label.autoSize = AutoSizeType.None;
            label.singleLine = true;
            label.align = AlignType.Center;
            label.verticalAlign = VertAlignType.Middle;
            TextFormat nameFormat = label.textFormat;
            CopyStyle(nameFormat, nameStyle, descriptionStyle, 13, 18);
            label.textFormat = nameFormat;
            label.text = string.IsNullOrWhiteSpace(usage.Title) || usage.Title == usage.Name
                ? usage.Name : usage.Title + " / " + usage.Name;
            // The native marquee requires an attached display object; defer
            // TryScrollTextField until the viewport has been added to the window.
            scrollLabels.Add(label);
            nativeName.visible = true;
            card.AddChild(nativeName);

            string status = group == 0
                ? (usage.ContractSigned ? "" : usage.ContractMissing > 0
                    ? "缺少 " + usage.ContractMissing : "材料已齐")
                : group == 1
                    ? (usage.PotentialAwakened ? "" : usage.PotentialMissing > 0
                        ? "缺少 " + usage.PotentialMissing : "材料已齐")
                    : "偏好礼物";
            if (status.Length > 0) {
                var countLabel = new GRichTextField {
                    name = "GiftGuide.MaterialCount",
                    touchable = false,
                    autoSize = AutoSizeType.None,
                    singleLine = true,
                    align = AlignType.Center,
                    verticalAlign = VertAlignType.Middle
                };
                TextFormat countFormat = countLabel.textFormat;
                CopyStyle(countFormat, descriptionStyle, nameStyle, 13, 16);
                countLabel.textFormat = countFormat;
                countLabel.SetXY(3f, 171f);
                countLabel.SetSize(width - 6f, 20f);
                countLabel.text = status;
                card.AddChild(countLabel);
            }
            return card;
        }

        private static void CopyStyle(TextFormat target, GTextField? preferred,
            GTextField? fallback, int minSize, int maxSize) {
            GTextField? source = preferred != null && !preferred.isDisposed ? preferred :
                fallback != null && !fallback.isDisposed ? fallback : null;
            if (source != null) target.CopyFrom(source.textFormat);
            int size = target.size > 0 ? target.size : 15;
            target.size = Math.Max(minSize, Math.Min(maxSize, size));
        }

        internal void Clear() {
            if (_viewport != null && !_viewport.isDisposed) {
                _viewport.RemoveFromParent();
                _viewport.Dispose();
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
