#nullable enable
using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using FairyGUI.Utils;
using UnityEngine;
using Tools;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// A scrollable lower extension of the original item window. Character
    /// portraits prefer HeroPanel's native hero buttons. The previous shop
    /// cards remain available as a fallback for UI package mismatches.
    /// </summary>
    internal sealed class PropCharacterAvatarGrid : IDisposable {
        private const float Padding = 9f;
        // Keep authored shop-card size; scaling the button also shrinks the
        // portrait/name and creates large looking gaps around each cell.
        private const int ColumnGap = 5;
        private const int RowGap = 5;
        private const float HeroNameHeight = 29f;
        private const float HeadingHeight = 36f;
        private const float SectionGap = 12f;
        private const float ContractColumnGap = 2f;
        private const float ContractRowGap = 2f;
        // Exact pink used by SettingWindow for its active controls.
        private static readonly Color SettingPink = new Color(1f, .1686f, .5686f, 1f);

        private GuideViewport? _viewport;
        private GComponent? _pane;
        private PropPopupLayout? _layout;
        private int _item;
        private DateTime _retry;

        private sealed class GuideViewport : GComponent {
            internal GuideViewport(string verticalBarUrl) {
                // The supplied URL points to an existing FairyGUI GScrollBar.
                // ScrollPane owns it, including thumb dragging and visibility.
                var scrollOptions = new ByteBuffer(new byte[] {
                    1, 1, // ScrollType.Vertical, ScrollBarDisplayType.Visible
                    0, 0, 0, 0, // flags
                    0, // no scroll-bar margins
                    0, 0, // string-table index 0: native vertical scroll bar
                    0xff, 0xfe, 0xff, 0xfe, 0xff, 0xfe // no horizontal bar/header/footer
                }) { stringTable = new[] { verticalBarUrl } };
                SetupScroll(scrollOptions);
            }
        }

        private static string? FindVerticalScrollBar() {
            // Respect the game's configured scrollbar first.
            string configured = UIConfig.verticalScrollBar;
            if (!string.IsNullOrWhiteSpace(configured)) {
                PackageItem? item = UIPackage.GetItemByURL(configured);
                if (item != null && item.objectType == ObjectType.ScrollBar)
                    return configured;
            }
            // Some game windows leave UIConfig.verticalScrollBar unset.
            // Search the already-loaded native packages, never draw a fake bar.
            string? fallback = null;
            foreach (UIPackage package in UIPackage.GetPackages()) {
                foreach (PackageItem item in package.GetItems()) {
                    if (item.objectType != ObjectType.ScrollBar ||
                        item.width > item.height || item.width <= 0) continue;
                    string url = "ui://" + package.id + item.id;
                    if (package.name == "Common_External" || package.name == "Common")
                        return url;
                    fallback ??= url;
                }
            }
            return fallback;
        }

        private static void StyleNativeScrollbar(GScrollBar? bar) {
            if (bar == null || bar.isDisposed) return;
            // Resize the native draggable control itself (not a decorative overlay).
            // Do this before sizing the viewport so ScrollPane reserves the new gutter.
            float oldWidth = Math.Max(1f, bar.width);
            float width = Math.Max(24f, Math.Min(34f, oldWidth * 1.55f));
            if (width > oldWidth + .5f) {
                GObject? track = bar.GetChild("bar");
                GObject? grip = bar.GetChild("grip");
                float trackWidth = track?.width ?? 0f;
                float gripWidth = grip?.width ?? 0f;
                bar.SetSize(width, bar.height);
                // Relations may already resize the children when the scrollbar
                // grows. Set explicit target widths from the ORIGINAL values.
                if (track != null) {
                    track.SetSize(Math.Max(10f, trackWidth + width - oldWidth), track.height);
                    track.x = Math.Max(0f, (width - track.width) * .5f);
                }
                if (grip != null) {
                    grip.SetSize(Math.Max(10f, gripWidth + width - oldWidth), grip.height);
                    grip.x = Math.Max(0f, (width - grip.width) * .5f);
                }
            }
            // A FairyGUI scrollbar's grip is frequently a button containing
            // multiple images for normal/hover/pressed states. Tint every skin.
            TintScrollbarSkin(bar.GetChild("grip"), SettingPink);
        }

        private static void TintScrollbarSkin(GObject? obj, Color tint) {
            if (obj == null) return;
            if (obj is GImage image) image.color = tint;
            else if (obj is GLoader loader) loader.color = tint;
            else if (obj is GGraph graph) graph.color = tint;
            if (obj is GComponent component)
                foreach (GObject child in component.GetChildren())
                    TintScrollbarSkin(child, tint);
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
                UIObjectFactory.SetPackageItemExtension(UICom_Item.URL,
                    typeof(UICom_Item));
                UIObjectFactory.SetPackageItemExtension(UIHero_Com_BreakThrough.URL,
                    typeof(UIHero_Com_BreakThrough));
                UIObjectFactory.SetPackageItemExtension(UIHero_com_show.URL,
                    typeof(UIHero_com_show));
                UIObjectFactory.SetPackageItemExtension(UIHero_Com_HeroItem.URL,
                    typeof(UIHero_Com_HeroItem));
                UIObjectFactory.SetPackageItemExtension(UIHero_Button_Hero.URL,
                    typeof(UIHero_Button_Hero));
                UIObjectFactory.SetPackageItemExtension(UICom_SkinQuality.URL,
                    typeof(UICom_SkinQuality));
                UIObjectFactory.SetPackageItemExtension(UIButton_Collect.URL,
                    typeof(UIButton_Collect));

                stage = "扩展原版弹窗";
                layout = PropPopupLayout.Expand(pane, background, footer, objects,
                    description, preview, name, afterFooter);
                if (layout == null || layout.GuideWidth < 270f || layout.GuideHeight < 105f) {
                    _retry = DateTime.UtcNow.AddSeconds(5);
                    SdkLog.Warn("GiftGuide", "原版物品弹窗当前可用区域不足，角色列表稍后重试");
                    return false;
                }

                stage = "查找原版滚动条";
                string? verticalBarUrl = FindVerticalScrollBar();
                if (string.IsNullOrEmpty(verticalBarUrl)) {
                    UiPackageWarmup.RequestNativeScrollBar();
                    _retry = DateTime.UtcNow.AddSeconds(5);
                    SdkLog.Warn("GiftGuide", "尚未找到原版 GScrollBar，角色列表等待 UI 包就绪");
                    return false;
                }
                stage = "创建角色列表滚动区域";
                created = new GuideViewport(verticalBarUrl) {
                    name = "GiftGuide.ScrollViewport",
                    touchable = true,
                    opaque = true // captures wheel and drag even over blank space
                };
                // Use the native interactive scrollbar, with SettingWindow's
                // contrasting pink grip and a larger mouse-drag target.
                StyleNativeScrollbar(created.scrollPane?.vtScrollBar);
                // ScrollPane reserves its own gutter for the resized GScrollBar.
                created.SetSize(layout.GuideWidth, layout.GuideHeight);
                created.SetXY(layout.GuideLeft, layout.GuideTop);
                ScrollPane? scroll = created.scrollPane;
                if (scroll == null)
                    throw new InvalidOperationException("FairyGUI ScrollPane 创建失败");
                scroll.mouseWheelEnabled = true;
                scroll.touchEffect = true;
                scroll.bouncebackEffect = false;
                // Prefer the exact hero card from HeroPanel, including its own
                // status icon and breakthrough quality frame. Keep the original
                // shop-card renderer below as a safe resource-version fallback.
                bool useHeroCards = false;
                float cellWidth = 0f, cellHeight = 0f;
                string nativeFont = "";
                UIHero_Button_Hero? sampleHero = null;
                try {
                    sampleHero = UIHero_Button_Hero.CreateInstance();
                    if (sampleHero != null && sampleHero.width > 0f && sampleHero.height > 0f &&
                        sampleHero.com_Loader?.loader_Character != null &&
                        sampleHero.com_show != null && sampleHero.txt_chrname != null) {
                        useHeroCards = true;
                        cellWidth = sampleHero.width;
                        cellHeight = sampleHero.height + HeroNameHeight;
                        nativeFont = sampleHero.txt_chrname.textFormat.font ?? "";
                    }
                } catch (Exception ex) {
                    SdkLog.Warn("GiftGuide", "原版角色卡片尚未就绪，使用商店卡片：" + ex.Message);
                } finally {
                    try {
                        if (sampleHero != null && !sampleHero.isDisposed) sampleHero.Dispose();
                    } catch (Exception ex) {
                        SdkLog.Warn("GiftGuide", "释放角色卡片样本失败：" + ex.Message);
                    }
                }
                if (!useHeroCards) {
                    UIButton_GoodsItem_Store sampleShop = UIButton_GoodsItem_Store.CreateInstance();
                    if (sampleShop == null || sampleShop.width <= 0f || sampleShop.height <= 0f)
                        throw new InvalidOperationException("原版角色卡片和商店卡片均不可用");
                    cellWidth = sampleShop.width;
                    cellHeight = sampleShop.height;
                    nativeFont = sampleShop.com_ItemName?.txt_Name?.textFormat.font ?? "";
                    sampleShop.Dispose();
                }
                scroll.scrollStep = Math.Max(28f, cellHeight * .38f);

                float available = Math.Max(1f, Math.Min(created.width,
                    scroll.viewWidth > 0f ? scroll.viewWidth : created.width));
                float inner = Math.Max(1f, available - 2f * Padding);
                if (cellWidth > inner)
                    throw new InvalidOperationException("角色列表宽度不足以容纳原版商店卡片");
                int columns = Math.Max(1, (int)Math.Floor((inner + ColumnGap) /
                    (cellWidth + ColumnGap)));

                stage = "生成契约、潜能和偏好礼物分组";
                var sections = new GComponent {
                    name = "GiftGuide.CharacterSections",
                    touchable = false,
                    opaque = false
                };
                var scrollLabels = new List<GTextField>();
                float y = Padding;
                y = AddSection(sections, usages, 0, "契约", name, description,
                    inner, columns, cellWidth, cellHeight, nativeFont, useHeroCards,
                    y, scrollLabels);
                y = AddSection(sections, usages, 1, "潜能", name, description,
                    inner, columns, cellWidth, cellHeight, nativeFont, useHeroCards,
                    y, scrollLabels);
                y = AddSection(sections, usages, 2, "偏好礼物", name, description,
                    inner, columns, cellWidth, cellHeight, nativeFont, useHeroCards,
                    y, scrollLabels);
                if (y <= Padding) {
                    sections.Dispose();
                    return false;
                }

                sections.SetSize(available, y + Padding);
                created.AddChild(sections);
                created.EnsureBoundsCorrect();
                stage = "添加角色列表到原版窗口";
                pane.AddChild(created);
                created.EnsureBoundsCorrect();
                scroll.UpdateScrollBarVisible();
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
            float width, int columns, float cellWidth, float cellHeight, string nativeFont,
            bool useHeroCards, float y, List<GTextField> scrollLabels) {
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
            CopyStyle(headingFormat, nameStyle, descriptionStyle, 22, 26);
            if (!string.IsNullOrEmpty(nativeFont)) headingFormat.font = nativeFont;
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
                list.AddChild(BuildCharacterCard(usage, group, cellWidth, cellHeight,
                    nativeFont, useHeroCards, scrollLabels));
            list.EnsureBoundsCorrect();
            return y + listHeight;
        }

        private static GComponent BuildCharacterCard(CharacterMaterialUsage usage,
            int group, float cellWidth, float cellHeight, string nativeFont,
            bool useHeroCards, List<GTextField> scrollLabels) {
            return useHeroCards
                ? BuildHeroCharacterCard(usage, group, cellWidth, cellHeight, nativeFont, scrollLabels)
                : BuildLegacyStoreCard(usage, group, cellWidth, cellHeight, nativeFont, scrollLabels);
        }

        private static GComponent BuildHeroCharacterCard(CharacterMaterialUsage usage,
            int group, float cellWidth, float cellHeight, string nativeFont,
            List<GTextField> scrollLabels) {
            UIHero_Button_Hero hero = UIHero_Button_Hero.CreateInstance();
            if (hero == null || hero.com_Loader?.loader_Character == null ||
                hero.com_show == null || hero.txt_chrname == null)
                throw new InvalidOperationException("HeroPanel 角色卡片结构与当前游戏版本不匹配");
            hero.name = "GiftGuide.Hero." + usage.HeroId;
            hero.HeroId = usage.HeroId;
            hero.touchable = false;
            hero.opaque = false;
            hero.grayed = false;
            hero.breakthrough.selectedIndex = usage.ContractSigned ? 1 : 0;
            hero.Collaborate.selectedIndex = usage.IsLinkage ? 1 : 0;
            hero.com_Loader.loader_Character.url = !string.IsNullOrEmpty(usage.HeroPortrait)
                ? usage.HeroPortrait : usage.Portrait;
            hero.com_Loader.graph_Effect.visible = false;
            if (hero.com_Loader.com_Qulity is UICom_SkinQuality quality)
                quality.appearanceType.selectedIndex = usage.Quality;
            hero.com_Loader.Ultimate.selectedIndex = usage.IsUltimate ? 1 : 0;
            hero.txt_Trial.visible = false;
            hero.btn_Collect.visible = false;
            // Put the name beneath the hero button rather than at the old
            // store-card title position. The game-authored portrait is unscaled.
            hero.txt_chrname.text = usage.Name;
            hero.txt_chrname.visible = false;
            hero.com_show.visible = false;
            hero.com_show.releationPowerUp.selectedIndex = 0;
            hero.com_show.Status.selectedIndex = 0;

            var cell = new GComponent { name = "GiftGuide.HeroCell", touchable = false,
                opaque = false, tooltips = usage.Name };
            cell.SetSize(cellWidth, cellHeight);
            cell.AddChild(hero);

            var caption = new GTextField {
                name = "GiftGuide.HeroName", text = usage.Name,
                touchable = false, autoSize = AutoSizeType.None,
                align = AlignType.Center, verticalAlign = VertAlignType.Middle,
                tooltips = usage.Name
            };
            TextFormat nameFormat = caption.textFormat;
            CopyStyle(nameFormat, hero.txt_chrname, null, 16, 20);
            if (!string.IsNullOrEmpty(nativeFont)) nameFormat.font = nativeFont;
            caption.textFormat = nameFormat;
            caption.SetSize(cellWidth, HeroNameHeight - 2f);
            caption.SetXY(0f, hero.height + 1f);
            cell.AddChild(caption);
            scrollLabels.Add(caption);

            bool showCompleted = group == 1 && usage.PotentialAwakened ||
                group == 2 && usage.FavorDeepened;
            if (showCompleted) {
                // This is the exact HeroPanel status object at its authored
                // coordinates, not a new icon floating over the card.
                hero.com_show.visible = true;
                hero.com_show.Status.selectedIndex = group == 2 ? 1 : 3;
                hero.com_show.releationPowerUp.selectedIndex = group == 2 ? 1 : 0;
                hero.com_show.LV_txt.text = (group == 2
                    ? usage.FavorLevel : usage.PotentialLevel).ToString();
            } else if (group == 1 || group == 2) {
                string status = group == 1
                    ? (usage.PotentialMissing > 0 ? "缺少 " + usage.PotentialMissing : "材料已齐")
                    : usage.FavorExp + "/" + usage.FavorNeedExp;
                AddHeroStatus(hero, status, nativeFont);
            }
            if (group == 0)
                AddHeroContractMaterials(hero, usage, nativeFont);
            return cell;
        }

        private static void AddHeroStatus(UIHero_Button_Hero hero, string status,
            string nativeFont) {
            UIHero_com_show anchor = hero.com_show;
            float width = Math.Min(hero.width - 6f, Math.Max(84f, anchor.width + 30f));
            var text = new GTextField {
                name = "GiftGuide.HeroStatus", text = status, touchable = false,
                autoSize = AutoSizeType.None, align = AlignType.Center,
                verticalAlign = VertAlignType.Middle
            };
            TextFormat format = text.textFormat;
            CopyStyle(format, hero.txt_chrname, anchor.LV_txt, 17, 20);
            if (!string.IsNullOrEmpty(nativeFont)) format.font = nativeFont;
            text.textFormat = format;
            text.SetSize(width, Math.Max(24f, anchor.height));
            text.SetXY(Math.Max(3f, Math.Min(hero.width - width - 3f,
                anchor.x + (anchor.width - width) * .5f)), anchor.y);
            hero.AddChild(text);
        }

        private static void AddHeroContractMaterials(UIHero_Button_Hero hero,
            CharacterMaterialUsage usage, string nativeFont) {
            int count = usage.ContractRequirements.Count;
            if (count == 0) return;

            // Contract materials share the original hero status position, but
            // each item has its own unscaled 16+ px count instead of shrinking
            // the native count together with the item art.
            GComponent anchor = hero.com_show;
            int columns = count <= 3 ? count : count == 4 ? 2 : 3;
            int rows = (count + columns - 1) / columns;
            float width = Math.Max(1f, hero.width - 8f);
            // Keep single-row gifts large, but leave the portrait visible.
            float desiredHeight = rows * 78f + (rows - 1) * 3f;
            float height = Math.Min(Math.Max(1f, hero.height - 8f), desiredHeight);
            float top = Math.Max(4f, Math.Min(hero.height - height - 4f,
                anchor.y + anchor.height * .5f - height * .78f));
            var materials = new GList {
                name = "GiftGuide.list_BreakThrough", touchable = false, opaque = false,
                layout = ListLayoutType.FlowHorizontal, columnCount = columns,
                columnGap = 3, lineGap = 3, autoResizeItem = false
            };
            materials.SetSize(width, height);
            materials.SetXY(4f, top);
            hero.AddChild(materials);

            float slotWidth = (width - (columns - 1) * 3f) / columns;
            float slotHeight = (height - (rows - 1) * 3f) / rows;
            foreach (ContractRequirement need in usage.ContractRequirements) {
                UIHero_Com_BreakThrough item = UIHero_Com_BreakThrough.CreateInstance();
                if (item == null || !(item.com_Item is UICom_Item icon) || item.txt_Num == null)
                    throw new InvalidOperationException("原版契约材料组件尚未准备好");
                icon.RefreshItem(need.ItemId, showNum: false);
                icon.touchable = false;
                item.touchable = false;
                item.txt_Num.SetVar("color", need.Owned >= need.Required ? "00FF00" : "FF0000")
                    .SetVar("curNum", need.Required.ToString())
                    .SetVar("target", need.Owned.ToString()).FlushVars();
                // Keep the native renderer and icon frame; replace only its
                // downsized number field with legible game-font text.
                item.txt_Num.visible = false;
                var slot = new GComponent { touchable = false, opaque = false };
                slot.SetSize(slotWidth, slotHeight);
                float labelHeight = Math.Min(25f, Math.Max(18f, slotHeight * .30f));
                float iconWidth = Math.Max(1f, icon.x + icon.width);
                float iconHeight = Math.Max(1f, icon.y + icon.height);
                float scale = Math.Min(1.2f, Math.Min(
                    (slotWidth - 3f) / iconWidth,
                    (slotHeight - labelHeight - 3f) / iconHeight));
                scale = Math.Max(.01f, scale);
                item.SetScale(scale, scale);
                item.SetXY((slotWidth - iconWidth * scale) * .5f, 0f);
                slot.AddChild(item);
                var number = new GTextField {
                    name = "GiftGuide.MaterialCount", touchable = false,
                    autoSize = AutoSizeType.None,
                    align = AlignType.Center, verticalAlign = VertAlignType.Middle,
                    text = need.Required + "/" + need.Owned
                };
                TextFormat countFormat = number.textFormat;
                CopyStyle(countFormat, item.txt_Num, hero.txt_chrname, 16, 19);
                countFormat.size = Math.Max(18, countFormat.size);
                countFormat.bold = true;
                if (!string.IsNullOrEmpty(nativeFont)) countFormat.font = nativeFont;
                number.textFormat = countFormat;
                number.color = need.Owned >= need.Required
                    ? new Color(.10f, .78f, .24f, 1f)
                    : new Color(.98f, .23f, .29f, 1f);
                number.SetSize(slotWidth, labelHeight);
                number.SetXY(0f, slotHeight - labelHeight);
                slot.AddChild(number);
                materials.AddChild(slot);
            }
            materials.EnsureBoundsCorrect();
        }

        private static GComponent BuildLegacyStoreCard(CharacterMaterialUsage usage,
            int group, float cellWidth, float cellHeight, string nativeFont,
            List<GTextField> scrollLabels) {
            // Fallback retains the previous UICom_GoodsQuality_Store renderer.
            // Retain the game's entire original shop card, including portrait
            // frame, size, title and price area.
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

            GTextField price = native.com_Price.txt_SalePrice;
            TextFormat priceFormat = price.textFormat;
            if (!string.IsNullOrEmpty(nativeFont)) priceFormat.font = nativeFont;
            price.textFormat = priceFormat;
            bool showIcon = (group == 1 && usage.PotentialAwakened) ||
                (group == 2 && usage.FavorDeepened);
            string status = group == 1
                ? (usage.PotentialMissing > 0 ? "缺少 " + usage.PotentialMissing : "材料已齐")
                : group == 2 ? usage.FavorExp + "/" + usage.FavorNeedExp : "";
            native.com_Price.visible = group != 0 && !showIcon;
            native.com_Price.isDiscount.selectedIndex = 0;
            if (native.com_Price.group_Discount != null)
                native.com_Price.group_Discount.visible = false;
            if (native.com_Price.txt_OriginalPrice != null)
                native.com_Price.txt_OriginalPrice.visible = false;
            if (native.com_Price.txt_DiscountPercent != null)
                native.com_Price.txt_DiscountPercent.visible = false;
            price.text = status;

            if (showIcon) {
                UIHero_com_show marker = UIHero_com_show.CreateInstance();
                marker.touchable = false;
                marker.Status.selectedIndex = group == 2 ? 1 : 3;
                marker.releationPowerUp.selectedIndex = group == 2 ? 1 : 0;
                marker.LV_txt.text = (group == 2 ? usage.FavorLevel : usage.PotentialLevel).ToString();
                GComponent target = native.com_Price;
                float scale = Math.Min(1f, Math.Min(
                    (target.width - 4f) / Math.Max(1f, marker.width),
                    (target.height - 2f) / Math.Max(1f, marker.height)));
                marker.SetScale(scale, scale);
                marker.SetXY(target.x + (target.width - marker.width * scale) * .5f,
                    target.y + (target.height - marker.height * scale) * .5f);
                native.AddChild(marker);
            }

            if (group == 0) AddContractMaterials(native, usage, cellWidth, cellHeight);
            return native;
        }

        private static void AddContractMaterials(UIButton_GoodsItem_Store native,
            CharacterMaterialUsage usage, float cellWidth, float cellHeight) {
            int count = usage.ContractRequirements.Count;
            if (count == 0) return;

            // Reuse the HeroPanel's native BreakThrough cells, but put the
            // miniature list in the shop card's original price/status area.
            // This must not add a second row below the card.
            GComponent anchor = native.com_Price;
            int columns = count <= 3 ? count : Math.Min(3, (count + 1) / 2);
            int rows = (count + columns - 1) / columns;
            float width = Math.Max(1f, Math.Min(cellWidth - 6f,
                Math.Max(anchor.width, cellWidth * .78f)));
            float desiredHeight = rows * 23f + (rows - 1) * ContractRowGap;
            float height = Math.Max(1f, Math.Min(cellHeight - 4f,
                Math.Min(Math.Max(anchor.height, desiredHeight),
                    Math.Max(30f, cellHeight * .30f))));
            float x = Math.Max(3f, Math.Min(cellWidth - width - 3f,
                anchor.x + (anchor.width - width) * .5f));
            float y = Math.Max(2f, Math.Min(cellHeight - height - 2f,
                anchor.y + (anchor.height - height) * .5f));

            var materials = new GList {
                name = "GiftGuide.list_BreakThrough",
                layout = ListLayoutType.FlowHorizontal,
                columnGap = (int)ContractColumnGap,
                lineGap = (int)ContractRowGap,
                columnCount = columns,
                autoResizeItem = false,
                touchable = false,
                opaque = false
            };
            materials.SetSize(width, height);
            materials.SetXY(x, y);
            native.AddChild(materials);

            float slotWidth = (width - (columns - 1) * ContractColumnGap) / columns;
            float slotHeight = (height - (rows - 1) * ContractRowGap) / rows;
            foreach (ContractRequirement need in usage.ContractRequirements) {
                UIHero_Com_BreakThrough item = UIHero_Com_BreakThrough.CreateInstance();
                if (item == null || !(item.com_Item is UICom_Item material))
                    throw new InvalidOperationException("原版契约材料组件尚未准备好");
                material.RefreshItem(need.ItemId, showNum: false);
                material.touchable = false;
                item.touchable = false;
                // The native text template is {curNum}/{target}. Its original
                // red/green formatting is retained, with required/owned order.
                item.txt_Num.SetVar("color", need.Owned >= need.Required ? "00FF00" : "FF0000")
                    .SetVar("curNum", need.Required.ToString())
                    .SetVar("target", need.Owned.ToString()).FlushVars();
                var slot = new GComponent { touchable = false, opaque = false };
                slot.SetSize(slotWidth, slotHeight);
                float scale = Math.Min(1f, Math.Min(
                    (slotWidth - 2f) / Math.Max(1f, item.width),
                    (slotHeight - 2f) / Math.Max(1f, item.height)));
                item.SetScale(scale, scale);
                item.SetXY((slotWidth - item.width * scale) * .5f,
                    (slotHeight - item.height * scale) * .5f);
                slot.AddChild(item);
                materials.AddChild(slot);
            }
            materials.EnsureBoundsCorrect();
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
