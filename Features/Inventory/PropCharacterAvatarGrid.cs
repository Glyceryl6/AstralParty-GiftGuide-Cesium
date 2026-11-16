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
        // Keep authored shop-card size; scaling the button also shrinks the
        // portrait/name and creates large looking gaps around each cell.
        private const int ColumnGap = 5;
        private const int RowGap = 5;
        private const float HeadingHeight = 27f;
        private const float SectionGap = 10f;

        private GuideViewport? _viewport;
        private GComponent? _scrollBar;
        private GGraph? _scrollThumb;
        private ScrollPane? _scrollPane;
        private float _barLength;
        private float _thumbLength;
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
            IReadOnlyList<GObject> objects, bool afterFooter,
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
                UIObjectFactory.SetPackageItemExtension(UICom_Label.URL,
                    typeof(UICom_Label));
                UIObjectFactory.SetPackageItemExtension(UICom_ItemType.URL,
                    typeof(UICom_ItemType));
                UIObjectFactory.SetPackageItemExtension(UICom_Price.URL,
                    typeof(UICom_Price));
                UIObjectFactory.SetPackageItemExtension(UIButton_GoodsItem_Store.URL,
                    typeof(UIButton_GoodsItem_Store));

                stage = "扩展原版弹窗";
                layout = PropPopupLayout.Expand(pane, background, footer, objects,
                    description, preview, name, afterFooter);
                if (layout == null || layout.GuideWidth < 270f || layout.GuideHeight < 105f) {
                    _retry = DateTime.UtcNow.AddSeconds(5);
                    SdkLog.Warn("GiftGuide", "原版物品弹窗当前可用区域不足，角色列表稍后重试");
                    return false;
                }

                stage = "创建角色列表滚动区域";
                created = new GuideViewport {
                    name = "GiftGuide.ScrollViewport",
                    touchable = true,
                    opaque = true // captures wheel and drag even over blank space
                };
                // Leave a fixed gutter for a visible, usable scroll bar.
                created.SetSize(layout.GuideWidth - 19f, layout.GuideHeight);
                created.SetXY(layout.GuideLeft, layout.GuideTop);
                ScrollPane? scroll = created.scrollPane;
                if (scroll == null)
                    throw new InvalidOperationException("FairyGUI ScrollPane 创建失败");
                scroll.mouseWheelEnabled = true;
                scroll.touchEffect = true;
                scroll.bouncebackEffect = false;
                // Measure the original store component in the game's UI package.
                // Never SetScale the authored item: its text and portrait are
                // already laid out to fit these actual dimensions.
                UIButton_GoodsItem_Store sample = UIButton_GoodsItem_Store.CreateInstance();
                if (sample == null || sample.width <= 0f || sample.height <= 0f)
                    throw new InvalidOperationException("商店角色卡片尚未就绪");
                float cellWidth = sample.width;
                float cellHeight = sample.height;
                sample.Dispose();
                scroll.scrollStep = cellHeight + RowGap;

                float available = Math.Max(1f, Math.Min(created.width,
                    scroll.viewWidth > 0f ? scroll.viewWidth : created.width));
                float inner = Math.Max(1f, available - 2f * Padding);
                if (cellWidth > inner)
                    throw new InvalidOperationException("角色列表宽度不足以容纳原版商店卡片");
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
                stage = "添加角色列表到原版窗口";
                pane.AddChild(created);
                created.EnsureBoundsCorrect();
                scroll.UpdateScrollBarVisible();
                CreateScrollBar(pane, layout, scroll);
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
                if (_scrollPane != null) _scrollPane.onScroll.Remove(UpdateThumb);
                _scrollPane = null;
                if (_scrollBar != null && !_scrollBar.isDisposed) {
                    _scrollBar.RemoveFromParent();
                    _scrollBar.Dispose();
                }
                _scrollBar = null;
                _scrollThumb = null;
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
                align = AlignType.Left,
                autoResizeItem = false,
                touchable = false
            };
            int rows = (members.Count + columns - 1) / columns;
            float listHeight = rows * cellHeight + Math.Max(0, rows - 1) * RowGap;
            list.SetSize(width, listHeight);
            list.SetXY(Padding, y);
            content.AddChild(list);
            foreach (CharacterMaterialUsage usage in members)
                list.AddChild(BuildCharacterCard(usage, group, scrollLabels));
            list.EnsureBoundsCorrect();
            return y + listHeight;
        }

        private static GComponent BuildCharacterCard(CharacterMaterialUsage usage,
            int group, List<GTextField> scrollLabels) {
            // Use the complete native store product component. The original
            // hierarchy draws com_Quality and then loader_Goods in the correct
            // order; manually placing a TextureLoader *inside* the quality
            // frame put portraits behind opaque parts of that frame.
            UIButton_GoodsItem_Store native = UIButton_GoodsItem_Store.CreateInstance();
            if (native == null || native.width < 4f || native.height < 4f ||
                native.com_Quality == null || native.loader_Goods == null ||
                native.com_ItemName?.txt_Name == null ||
                native.com_Price?.txt_SalePrice == null)
                throw new InvalidOperationException("原版商店角色卡片组件尚未准备好");

            native.touchable = false;
            native.opaque = false;
            native.grayed = false;
            if (native.sellOut != null) native.sellOut.selectedIndex = 0;
            if (native.owned != null) native.owned.selectedIndex = 0;
            if (native.isEmpty != null) native.isEmpty.selectedIndex = 0;
            if (native.isRecharge != null) native.isRecharge.selectedIndex = 0;
            if (native.isrecharge != null) native.isrecharge.selectedIndex = 0;
            if (native.isVideo != null) native.isVideo.selectedIndex = 0;
            if (native.com_Quality.quality != null)
                native.com_Quality.quality.selectedIndex = usage.ContractSigned
                    ? (int)QualityType.Orange : (int)QualityType.Purple;
            if (native.com_Quality.isEmpty != null)
                native.com_Quality.isEmpty.selectedIndex = 0;
            native.loader_Goods.url = usage.Portrait ?? "";
            native.loader_Goods.visible = true;

            if (native.txt_Remaining != null) native.txt_Remaining.visible = false;
            if (native.txt_ItemNum != null) native.txt_ItemNum.visible = false;
            if (native.txt_soldout != null) native.txt_soldout.visible = false;
            if (native.txt_timeTip != null) native.txt_timeTip.visible = false;
            if (native.txt_FirstRecharge != null) native.txt_FirstRecharge.visible = false;
            if (native.txt_FirstRechargeNum != null) native.txt_FirstRechargeNum.visible = false;
            if (native.com_Label != null) native.com_Label.visible = false;
            if (native.com_ItemType != null) native.com_ItemType.visible = false;

            GTextField label = native.com_ItemName.txt_Name;
            label.text = string.IsNullOrWhiteSpace(usage.Title) || usage.Title == usage.Name
                ? usage.Name : usage.Title + " / " + usage.Name;
            label.visible = true;
            native.com_ItemName.visible = true;
            scrollLabels.Add(label);

            string status = group == 0
                ? (usage.ContractSigned ? "" : usage.ContractMissing > 0
                    ? "缺少 " + usage.ContractMissing : "材料已齐")
                : group == 1
                    ? (usage.PotentialAwakened ? "" : usage.PotentialMissing > 0
                        ? "缺少 " + usage.PotentialMissing : "材料已齐")
                    : "偏好礼物";
            native.com_Price.visible = status.Length > 0;
            native.com_Price.isDiscount.selectedIndex = 0;
            if (native.com_Price.group_Discount != null)
                native.com_Price.group_Discount.visible = false;
            if (native.com_Price.txt_OriginalPrice != null)
                native.com_Price.txt_OriginalPrice.visible = false;
            if (native.com_Price.txt_DiscountPercent != null)
                native.com_Price.txt_DiscountPercent.visible = false;
            native.com_Price.txt_SalePrice.text = status;

            // GList owns the native component directly. No wrapper, scaling
            // or re-centering: the same sizing/layout as a real StorePanel row.
            return native;
        }

        private static void CopyStyle(TextFormat target, GTextField? preferred,
            GTextField? fallback, int minSize, int maxSize) {
            GTextField? source = preferred != null && !preferred.isDisposed ? preferred :
                fallback != null && !fallback.isDisposed ? fallback : null;
            if (source != null) target.CopyFrom(source.textFormat);
            int size = target.size > 0 ? target.size : 15;
            target.size = Math.Max(minSize, Math.Min(maxSize, size));
        }

        // The game's UIConfig.verticalScrollBar is not always populated on
        // these screens. Draw a small persistent fallback scrollbar instead of
        // silently leaving a scrollable but unmarked viewport.
        private void CreateScrollBar(GComponent pane, PropPopupLayout layout, ScrollPane scroll) {
            _scrollPane = scroll;
            _barLength = layout.GuideHeight;
            var bar = new GComponent { name = "GiftGuide.VisibleScrollbar", touchable = true,
                opaque = true };
            bar.SetSize(14f, _barLength);
            bar.SetXY(layout.GuideLeft + layout.GuideWidth - 14f, layout.GuideTop);
            GGraph track = new GGraph { touchable = false };
            track.SetSize(14f, _barLength);
            track.DrawRect(14f, _barLength, 0, Color.clear,
                new Color(.13f, .13f, .16f, .55f));
            bar.AddChild(track);
            GGraph thumb = new GGraph { touchable = false };
            thumb.SetXY(2f, 0f);
            bar.AddChild(thumb);
            _scrollBar = bar;
            _scrollThumb = thumb;
            pane.AddChild(bar);
            bar.onTouchBegin.Add(ScrollBarPointer);
            bar.onTouchMove.Add(ScrollBarPointer);
            scroll.onScroll.Add(UpdateThumb);
            UpdateThumb();
        }

        private void ScrollBarPointer(EventContext context) {
            if (_scrollBar == null || _scrollPane == null) return;
            try {
                Vector2 local = _scrollBar.GlobalToLocal(Stage.inst.touchPosition);
                float travel = Math.Max(1f, _barLength - _thumbLength);
                float value = Mathf.Clamp01((local.y - _thumbLength * .5f) / travel);
                _scrollPane.SetPercY(value, false);
                UpdateThumb();
            } catch (Exception ex) {
                SdkLog.Warn("GiftGuide", "滚动条操作失败：" + ex.Message);
            }
        }

        private void UpdateThumb() {
            if (_scrollPane == null || _scrollBar == null || _scrollThumb == null ||
                _scrollThumb.isDisposed) return;
            float content = Math.Max(_scrollPane.contentHeight, _barLength);
            _thumbLength = Math.Max(23f, Math.Min(_barLength,
                _barLength * _scrollPane.viewHeight / Math.Max(1f, content)));
            float track = Math.Max(0f, _barLength - _thumbLength);
            _scrollThumb.SetSize(10f, _thumbLength);
            _scrollThumb.SetXY(2f, track * _scrollPane.percY);
            _scrollThumb.DrawRect(10f, _thumbLength, 1,
                new Color(.83f, .83f, .83f, 1f), new Color(.84f, .84f, .84f, .86f));
        }

        internal void Clear() {
            if (_scrollPane != null) _scrollPane.onScroll.Remove(UpdateThumb);
            _scrollPane = null;
            if (_scrollBar != null && !_scrollBar.isDisposed) {
                _scrollBar.RemoveFromParent();
                _scrollBar.Dispose();
            }
            _scrollBar = null;
            _scrollThumb = null;
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
