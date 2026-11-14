using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// The native Setting scroll viewport contains THREE separate groups, using
    /// the authored Hero portrait and quality frame. The original description
    /// and the purchase buttons stay in place.
    /// </summary>
    internal sealed class PropCharacterAvatarGrid : IDisposable {
        private const int Columns = 5;
        private const float HorizontalPadding = 10f;
        private const float ColumnGap = 6f;
        private const float RowGap = 12f;
        private const float CaptionHeight = 25f;
        private const float SectionGap = 12f;
        private const float AvatarLimit = 108f;
        private const float LabelHeight = 20f;
        private const float StatusHeight = 19f;
        private UISetting_Comp_Score_Desc? _viewport;
        private GComponent? _pane;
        private int _item;
        private DateTime _retry;
        private static bool _typesRegistered;

        internal bool Alive => _viewport != null && !_viewport.isDisposed && _viewport.parent != null;
        internal bool IsFor(GComponent pane, int item) => ReferenceEquals(_pane, pane) && _item == item && Alive;

        internal bool TryShow(GComponent pane, int itemId, GTextField description, GButton preview,
            GTextField name, GLabel background, IReadOnlyList<GObject> footer,
            IReadOnlyList<CharacterMaterialUsage> usages) {
            if (usages.Count == 0 || DateTime.UtcNow < _retry || !UiPackageWarmup.Ready()) return false;
            UISetting_Comp_Score_Desc? created = null;
            try {
                if (!TryGetViewport(pane, description, preview, name, background, footer,
                        out float left, out float top, out float width, out float height)) return false;

                RegisterNativeTypes();
                created = UISetting_Comp_Score_Desc.CreateInstance();
                if (created == null) return false;
                // Preserve the game's authored scroll pane and scrollbar.
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
                float cellWidth = (available - HorizontalPadding * 2f - ColumnGap * 4f) / Columns;
                if (cellWidth < 42f) throw new InvalidOperationException("角色头像可用宽度不足");
                float avatarSize = Math.Min(AvatarLimit, cellWidth - 8f);
                float cardHeight = avatarSize + LabelHeight + StatusHeight + 10f;

                var content = new GComponent();
                float y = 0f;
                y = AddSection(content, usages, 0, "契约材料", description, name,
                    available, cellWidth, avatarSize, cardHeight, y);
                y = AddSection(content, usages, 1, "潜能材料", description, name,
                    available, cellWidth, avatarSize, cardHeight, y);
                y = AddSection(content, usages, 2, "偏好礼物", description, name,
                    available, cellWidth, avatarSize, cardHeight, y);
                if (y <= 0f) { created.Dispose(); return false; }
                content.SetSize(available, y + 6f);
                created.AddChild(content);
                pane.AddChild(created);
                created.EnsureBoundsCorrect();
                _viewport = created;
                _pane = pane;
                _item = itemId;
                return true;
            } catch (Exception e) {
                _retry = DateTime.UtcNow.AddSeconds(5);
                if (created != null && !created.isDisposed) {
                    created.RemoveFromParent();
                    created.Dispose();
                }
                SdkLog.Warn("GiftGuide", "角色材料滚动区域暂不可用：" + e.Message);
                return false;
            }
        }

        private static bool TryGetViewport(GComponent pane, GTextField description, GButton preview,
            GTextField name, GLabel background, IReadOnlyList<GObject> footer,
            out float x, out float y, out float width, out float height) {
            x = y = width = height = 0f;
            float paneW = pane.width, paneH = pane.height;
            if (paneW < 280f || paneH < 180f) return false;
            float left = 12f, right = paneW - 12f, bottom = paneH - 12f;
            // The native 'bottom' GLabel is a background, not a footer.
            if (background != null && background.width >= paneW * .55f &&
                background.height >= paneH * .5f && background.x >= 0f && background.y >= 0f &&
                background.x + background.width <= paneW + 2f) {
                left = Math.Max(left, background.x + 16f);
                right = Math.Min(right, background.x + background.width - 16f);
                bottom = Math.Min(bottom, background.y + background.height - 15f);
            }
            float textBottom = description.y +
                Math.Min(description.height, Math.Max(12f, description.textHeight + 5f));
            float top = Math.Max(preview.y + preview.height,
                Math.Max(textBottom, name.y + name.height)) + 8f;
            foreach (GObject control in footer) {
                if (control == null || !control.visible) continue;
                if (control.y > top + 50f && control.y < bottom) bottom = control.y - 7f;
            }
            x = left;
            y = top;
            width = right - left;
            height = bottom - top - 6f;
            return width >= 290f && height >= 58f && x >= 0f &&
                x + width <= paneW + 2f && y >= 0f && y + height <= paneH;
        }

        private static void RegisterNativeTypes() {
            if (_typesRegistered) return;
            UIObjectFactory.SetPackageItemExtension(UISetting_Comp_Score_Desc.URL, typeof(UISetting_Comp_Score_Desc));
            UIObjectFactory.SetPackageItemExtension(UIHero_Com_HeroItem.URL, typeof(UIHero_Com_HeroItem));
            UIObjectFactory.SetPackageItemExtension(UIHero_com_show.URL, typeof(UIHero_com_show));
            _typesRegistered = true;
        }

        private static float AddSection(GComponent content, IReadOnlyList<CharacterMaterialUsage> all,
            int group, string title, GTextField style, GTextField nameStyle,
            float available, float cellWidth, float avatarSize, float cardHeight, float y) {
            var members = new List<CharacterMaterialUsage>();
            for (int i = 0; i < all.Count; i++) {
                CharacterMaterialUsage item = all[i];
                if ((group == 0 && item.ContractMaterial) ||
                    (group == 1 && item.PotentialMaterial) ||
                    (group == 2 && item.GiftPreferred)) members.Add(item);
            }
            if (members.Count == 0) return y;
            if (y > 0f) y += SectionGap;
            AddText(content, title + "  ·  " + members.Count + " 位角色", nameStyle,
                17, HorizontalPadding, y, available - HorizontalPadding * 2f, CaptionHeight, false);
            y += CaptionHeight + 6f;
            for (int i = 0; i < members.Count; i++) {
                float x = HorizontalPadding + (i % Columns) * (cellWidth + ColumnGap);
                float cy = y + (i / Columns) * (cardHeight + RowGap);
                AddCharacter(content, members[i], group, style, x, cy, cellWidth, avatarSize);
            }
            int rows = (members.Count + Columns - 1) / Columns;
            return y + rows * cardHeight + Math.Max(0, rows - 1) * RowGap;
        }

        private static void AddCharacter(GComponent parent, CharacterMaterialUsage usage,
            int group, GTextField style, float x, float y, float cellWidth, float avatarSize) {
            // Native portrait prefab: its own Controller selects the correct
            // quality-colored surround (same artwork as Hero/shop/inventory UI).
            UIHero_Com_HeroItem hero = UIHero_Com_HeroItem.CreateInstance();
            float naturalW = hero.width, naturalH = hero.height;
            if (naturalW <= 0f || naturalH <= 0f)
                throw new InvalidOperationException("原版角色头像尺寸不可用");
            float scale = Math.Min(avatarSize / naturalW, avatarSize / naturalH);
            float displayW = naturalW * scale;
            hero.SetScale(scale, scale);
            hero.SetXY(x + (cellWidth - displayW) * .5f, y);
            hero.touchable = false;
            hero.grayed = !usage.Owned;
            if (!string.IsNullOrEmpty(usage.Portrait)) {
                hero.loader_Character.autoSize = false;
                hero.loader_Character.url = usage.Portrait;
            }
            Controller quality = hero.com_Qulity.GetControllerAt(0);
            if (quality != null) quality.selectedIndex = Math.Max(0, usage.Quality);
            hero.Ultimate.selectedIndex = usage.Quality == 3 ? 1 : 0;
            hero.graph_Effect.visible = false;
            parent.AddChild(hero);

            // Keep the native awakened badge inside the portrait without
            // replacing the original quality background.
            if (group == 1 && usage.PotentialAwakened) {
                UIHero_com_show badge = UIHero_com_show.CreateInstance();
                badge.Status.selectedIndex = 3;
                badge.releationPowerUp.selectedIndex = 1;
                badge.LV_txt.visible = false;
                if (badge.width > 0f && badge.height > 0f) {
                    float s = Math.Min(1f, Math.Min(25f / badge.width, 25f / badge.height));
                    badge.SetScale(s, s);
                    badge.SetXY(x + cellWidth - badge.width * s, y);
                    badge.touchable = false;
                    parent.AddChild(badge);
                } else badge.Dispose();
            }
            float textY = y + avatarSize + 3f;
            AddText(parent, usage.Name, style, 14, x, textY, cellWidth, LabelHeight, true);
            string status = group == 0 ? "契约缺少 " + usage.ContractMissing :
                group == 1 ? (usage.PotentialAwakened ? "已觉醒" : "潜能缺少 " + usage.PotentialMissing) : "偏好礼物";
            AddText(parent, status, style, 12, x, textY + LabelHeight,
                cellWidth, StatusHeight, true);
        }

        private static void AddText(GComponent parent, string value, GTextField style,
            int size, float x, float y, float width, float height, bool center) {
            var label = new GTextField {
                text = value,
                autoSize = AutoSizeType.None,
                singleLine = true,
                align = center ? AlignType.Center : AlignType.Left,
                verticalAlign = VertAlignType.Middle,
                touchable = false
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
        }

        internal void Clear() {
            if (_viewport != null && !_viewport.isDisposed) {
                _viewport.RemoveFromParent();
                _viewport.Dispose();
            }
            _viewport = null;
            _pane = null;
            _item = 0;
        }
        public void Dispose() => Clear();
    }
}
