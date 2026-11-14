using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Three groups in ONE native scroll viewport. Popup bounds come from the
    /// real 'bottom' frame, not the often full-screen contentPane. Portraits
    /// use the original quality skin and square-cropped head photos.
    /// </summary>
    internal sealed class PropCharacterAvatarGrid : IDisposable {
        private const int Columns = 5;
        private const float Padding = 12f;
        private const float ColumnGap = 4f;
        private const float RowGap = 5f;
        private const float CaptionHeight = 24f;
        private const float SectionGap = 8f;
        private const float AvatarLimit = 98f;
        private const float LabelHeight = 19f;
        private const float StatusHeight = 18f;
        private UISetting_Comp_Score_Desc? _viewport;
        private GComponent? _pane;
        private PropPopupLayout? _layout;
        private int _item;
        private DateTime _retry;
        private static bool _typesRegistered;

        internal bool Alive => _viewport is { isDisposed: false, parent: not null };
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

                RegisterNativeTypes();
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
                // The scroll bar consumes part of the viewport; do not use
                // contentPane.width for the five-column calculation.
                float available = Math.Min(width, scroll.viewWidth > 0f ? scroll.viewWidth : width);
                float cellWidth = (available - Padding * 2f - ColumnGap * (Columns - 1)) / Columns;
                if (cellWidth < 44f) throw new InvalidOperationException("弹窗内角色头像可用宽度不足");
                float avatarSize = Math.Min(AvatarLimit, cellWidth - 4f);
                float cardHeight = avatarSize + LabelHeight + StatusHeight + 5f;

                var content = new GComponent();
                float y = 0f;
                y = AddSection(content, usages, 0, "契约材料", description, name,
                    available, cellWidth, avatarSize, cardHeight, y);
                y = AddSection(content, usages, 1, "潜能材料", description, name,
                    available, cellWidth, avatarSize, cardHeight, y);
                y = AddSection(content, usages, 2, "偏好礼物", description, name,
                    available, cellWidth, avatarSize, cardHeight, y);
                if (y <= 0f) return false;
                content.SetSize(available, y + 8f);
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
            if (pane.width < 280f || pane.height < 180f) return false;
            float top = Math.Max(preview.y + preview.height,
                Math.Max(name.y + name.height,
                    description.y + Math.Min(description.height, Math.Max(12f, description.textHeight + 5f)))) + 10f;
            float left = Math.Max(8f, layout.Left);
            float right = Math.Min(pane.width - 8f, layout.Right);
            float bottom = Math.Min(pane.height - 8f, layout.FooterTop(top));
            x = left;
            y = top;
            width = right - left;
            height = bottom - top - 4f;
            return width >= 270f && height >= 85f && x >= 0f &&
                x + width <= pane.width && y >= 0f && y + height <= pane.height;
        }

        private static void RegisterNativeTypes() {
            if (_typesRegistered) return;
            UIObjectFactory.SetPackageItemExtension(UISetting_Comp_Score_Desc.URL, typeof(UISetting_Comp_Score_Desc));
            // Register the complete original Hero inventory portrait and its
            // Common_External quality controller before creating native cells.
            UIObjectFactory.SetPackageItemExtension(UICom_SkinQuality.URL, typeof(UICom_SkinQuality));
            UIObjectFactory.SetPackageItemExtension(UIHero_Com_HeroItem.URL, typeof(UIHero_Com_HeroItem));
            UIObjectFactory.SetPackageItemExtension(UIHero_com_show.URL, typeof(UIHero_com_show));
            _typesRegistered = true;
        }

        private static float AddSection(GComponent content, IReadOnlyList<CharacterMaterialUsage> all,
            int group, string title, GTextField style, GTextField nameStyle,
            float available, float cellWidth, float avatarSize, float cardHeight, float y) {
            var members = new List<CharacterMaterialUsage>();
            foreach (CharacterMaterialUsage item in all) {
                if ((group == 0 && item.ContractMaterial) ||
                    (group == 1 && item.PotentialMaterial) ||
                    (group == 2 && item.GiftPreferred)) members.Add(item);
            }
            if (members.Count == 0) return y;
            if (y > 0f) y += SectionGap;
            AddText(content, title + " · " + members.Count + " 位角色", nameStyle,
                17, Padding, y, available - Padding * 2f, CaptionHeight, false);
            y += CaptionHeight + 3f;
            for (int i = 0; i < members.Count; i++) {
                float x = Padding + (i % Columns) * (cellWidth + ColumnGap);
                float cy = y + i / Columns * (cardHeight + RowGap);
                AddCharacter(content, members[i], group, style, x, cy, cellWidth, avatarSize);
            }
            int rows = (members.Count + Columns - 1) / Columns;
            return y + rows * cardHeight + Math.Max(0, rows - 1) * RowGap;
        }

        private static void AddCharacter(GComponent parent, CharacterMaterialUsage usage,
            int group, GTextField style, float x, float y, float cellWidth, float avatarSize) {
            float photoX = x + (cellWidth - avatarSize) * .5f;
            // Reuse the COMPLETE original Hero_Com_HeroItem, not a plain image
            // with a frame copied on top. The prefab owns the game's quality
            // background, ultimate state and portrait clipping hierarchy.
            var square = new SquarePortraitClip();
            square.SetSize(avatarSize, avatarSize);
            square.SetXY(photoX, y);
            square.touchable = false;
            UIHero_Com_HeroItem hero = UIHero_Com_HeroItem.CreateInstance();
            if (hero?.com_Qulity == null || hero.loader_Character == null ||
                hero.width <= 0f || hero.height <= 0f)
                throw new InvalidOperationException("原版角色头像与品质背景不可用");
            Controller quality = hero.com_Qulity.GetControllerAt(0);
            if (quality is not { pageCount: > 0 })
                throw new InvalidOperationException("原版角色品质控制器不可用");
            quality.selectedIndex = Math.Max(0, Math.Min(quality.pageCount - 1, usage.Quality));
            hero.Ultimate.selectedIndex = usage.Quality == 3 ? 1 : 0;
            hero.graph_Effect.visible = false;
            hero.grayed = false; // Never wash out the native rarity color.
            hero.touchable = false;
            hero.loader_Character.autoSize = false;
            hero.loader_Character.fill = FillType.ScaleNoBorder;
            hero.loader_Character.url = usage.Portrait;
            // Uniform native-prefab scaling preserves the quality surround.
            // A square parent clips any portrait content beyond the cell.
            float scale1 = Math.Min(avatarSize / hero.width, avatarSize / hero.height);
            hero.SetScale(scale1, scale1);
            hero.SetXY((avatarSize - hero.width * scale1) * .5f,
                (avatarSize - hero.height * scale1) * .5f);
            square.AddChild(hero);
            parent.AddChild(square);

            if (group == 1 && usage.PotentialAwakened) {
                UIHero_com_show badge = UIHero_com_show.CreateInstance();
                badge.Status.selectedIndex = 3;
                badge.releationPowerUp.selectedIndex = 1;
                badge.LV_txt.visible = false;
                if (badge.width > 0f && badge.height > 0f) {
                    float scale2 = Math.Min(1f, Math.Min(23f / badge.width, 23f / badge.height));
                    badge.SetScale(scale2, scale2);
                    badge.SetXY(photoX + avatarSize - badge.width * scale2, y);
                    badge.touchable = false;
                    parent.AddChild(badge);
                } else badge.Dispose();
            }
            float textY = y + avatarSize + 2f;
            AddText(parent, usage.Name, style, 13, x, textY, cellWidth, LabelHeight, true);
            string status = group switch {
                0 => "契约缺少 " + usage.ContractMissing,
                1 => usage.PotentialAwakened ? "已觉醒" : "潜能缺少 " + usage.PotentialMissing,
                _ => "偏好礼物"
            };
            AddText(parent, status, style, 12, x, textY + LabelHeight,
                cellWidth, StatusHeight, true);
        }

        private sealed class SquarePortraitClip : GComponent {
            internal SquarePortraitClip() { SetupOverflow(OverflowType.Hidden); }
        }

        private static void AddText(GComponent parent, string value, GTextField? style,
            int size, float x, float y, float width, float height, bool center) {
            var label = new GTextField {
                text = value, autoSize = AutoSizeType.None, singleLine = true,
                align = center ? AlignType.Center : AlignType.Left,
                verticalAlign = VertAlignType.Middle, touchable = false
            };
            TextFormat format = label.textFormat;
            if (style is { isDisposed: false }) format.CopyFrom(style.textFormat);
            format.size = size;
            label.textFormat = format;
            if (style is { isDisposed: false }) {
                label.stroke = style.stroke;
                label.strokeColor = style.strokeColor;
            }
            label.SetSize(width, height);
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
