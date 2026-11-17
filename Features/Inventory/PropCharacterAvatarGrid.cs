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
        private const int ContractCountFontSize = 14;
        private const float HeadingHeight = 36f;
        private const float SectionGap = 12f;
        private const float ContractColumnGap = 2f;
        private const float ContractRowGap = 2f;
        // Exact pink used by SettingWindow for its active controls.
        private static readonly Color SettingPink = new Color(1f, .1686f, .5686f, 1f);
        private static NativeTextStyle? _heroPanelNameStyle;
        private static NativeTextStyle? _favorTitleStyle;
        private static bool _sampledHeroPanelStyles;

        // Use the game's complete text style (font, fill/gradient, outline,
        // shadow and original size) without subsequent color/stroke overrides.
        // V0.2.20's extra property setters changed the authored appearance.
        private sealed class NativeTextStyle {
            private readonly TextFormat _format = new TextFormat();

            internal NativeTextStyle(GTextField source) {
                _format.CopyFrom(source.textFormat);
            }

            internal void ApplyExact(GTextField target) {
                TextFormat format = target.textFormat;
                format.CopyFrom(_format);
                target.textFormat = format;
            }
        }

        private static void EnsureHeroPanelTextStyles() {
            if (_sampledHeroPanelStyles) return;
            _sampledHeroPanelStyles = true;
            UIHeroPanel? sample = null;
            try {
                // Create once and dispose immediately; retain only style values.
                sample = UIHeroPanel.CreateInstance();
                if (sample?.txt_Name != null)
                    _heroPanelNameStyle = new NativeTextStyle(sample.txt_Name);
                // FairyGUI progress bars use a child named "title" to render
                // their label (the `title` value itself is a string).
                if (sample?.slider_Favor?.GetChild("title") is GTextField title)
                    _favorTitleStyle = new NativeTextStyle(title);
            } catch (Exception ex) {
                SdkLog.Warn("GiftGuide", "读取原版英雄面板文字样式失败，保留现有样式：" + ex.Message);
            } finally {
                if (sample != null && !sample.isDisposed) {
                    try { sample.Dispose(); }
                    catch (Exception ex) {
                        SdkLog.Warn("GiftGuide", "释放英雄面板样本失败：" + ex.Message);
                    }
                }
            }
            if (_favorTitleStyle == null) {
                UIHero_bar_FavorValue? bar = null;
                try {
                    bar = UIHero_bar_FavorValue.CreateInstance();
                    if (bar?.GetChild("title") is GTextField title)
                        _favorTitleStyle = new NativeTextStyle(title);
                } catch (Exception ex) {
                    SdkLog.Warn("GiftGuide", "读取原版友情进度文字样式失败：" + ex.Message);
                } finally {
                    if (bar != null && !bar.isDisposed) bar.Dispose();
                }
            }
        }

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
                bool hasPreferredGift = false;
                foreach (CharacterMaterialUsage usage in usages) {
                    if (usage.GiftPreferred) { hasPreferredGift = true; break; }
                }
                layout = PropPopupLayout.Expand(pane, background, footer, objects,
                    description, preview, name, afterFooter, hasPreferredGift);
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
                        // The native name now stays INSIDE the hero button.
                        cellHeight = sampleHero.height;
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
                if (useHeroCards) EnsureHeroPanelTextStyles();
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
                ? BuildHeroCharacterCard(usage, group, cellWidth, cellHeight, nativeFont)
                : BuildLegacyStoreCard(usage, group, cellWidth, cellHeight, nativeFont, scrollLabels);
        }

        private static GComponent BuildHeroCharacterCard(CharacterMaterialUsage usage,
            int group, float cellWidth, float cellHeight, string nativeFont) {
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
            // The authored name field belongs to the actual hero button.
            // Reuse it instead of creating a caption beyond the card's edge.
            GTextField caption = hero.txt_chrname;
            caption.text = usage.Name;
            caption.visible = true;
            caption.touchable = false;
            caption.tooltips = usage.Name;
            caption.singleLine = true;
            caption.autoSize = AutoSizeType.Shrink;
            caption.verticalAlign = VertAlignType.Middle;
            // HeroPanel's txt_Name is the style authority. Do not manually
            // recolor its fill, replace the outline, or force an ad-hoc size.
            if (_heroPanelNameStyle != null)
                _heroPanelNameStyle.ApplyExact(caption);
            // V0.2.19 centered across the full hero button correctly. Keep
            // that horizontal frame; only raise the label inside the card.
            float nameHeight = Math.Min(HeroNameHeight, Math.Max(22f, hero.height * .24f));
            caption.SetSize(Math.Max(1f, hero.width - 8f), nameHeight);
            caption.SetXY(4f, Math.Max(2f, hero.height - nameHeight - 12f));
            caption.align = AlignType.Center;

            hero.com_show.visible = false;
            hero.com_show.releationPowerUp.selectedIndex = 0;
            hero.com_show.Status.selectedIndex = 0;

            var cell = new GComponent { name = "GiftGuide.HeroCell", touchable = false,
                opaque = false, tooltips = usage.Name };
            cell.SetSize(cellWidth, cellHeight);
            cell.AddChild(hero);

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
                AddHeroStatus(hero, status, nativeFont, group == 2);
            }
            if (group == 0)
                AddHeroContractMaterials(hero, usage);
            return cell;
        }

        private static void AddHeroStatus(UIHero_Button_Hero hero, string status,
            string nativeFont, bool isFavorExperience) {
            UIHero_com_show anchor = hero.com_show;
            float width = Math.Max(1f, hero.width - 6f);
            var text = new GTextField {
                name = "GiftGuide.HeroStatus", text = status, touchable = false,
                autoSize = AutoSizeType.None, singleLine = true,
                verticalAlign = VertAlignType.Middle
            };
            if (isFavorExperience && _favorTitleStyle != null) {
                // Keep the complete native FavorValue/title style at its
                // original size, rather than replacing its fill or outline.
                _favorTitleStyle.ApplyExact(text);
            } else if (_heroPanelNameStyle != null) {
                _heroPanelNameStyle.ApplyExact(text);
            } else {
                // The native hero label is a safe fallback when the sample
                // UI package is still loading; never invent a black outline.
                TextFormat format = text.textFormat;
                format.CopyFrom(hero.txt_chrname.textFormat);
                text.textFormat = format;
            }
            // None avoids the native Shrink behavior: 1/10 and 1234/5678
            // now use exactly the same font size. Keep the whole card width.
            text.autoSize = AutoSizeType.None;
            text.SetPivot(0f, 0f, false);
            text.align = AlignType.Center;
            float height = Math.Max(24f, anchor.height);
            text.SetSize(width, height);
            text.SetXY((hero.width - width) * .5f, Math.Max(2f,
                Math.Min(hero.height - height - 2f, anchor.y + (anchor.height - height) * .5f)), true);
            hero.AddChild(text);
        }

        private static void AddHeroContractMaterials(UIHero_Button_Hero hero,
            CharacterMaterialUsage usage) {
            int count = usage.ContractRequirements.Count;
            if (count == 0) return;

            // Place materials in the LOWER part of the native hero button,
            // directly above its name. Both stay inside the card bounds.
            int columns = count <= 3 ? count : count == 4 ? 2 : 3;
            int rows = (count + columns - 1) / columns;
            float width = Math.Max(1f, hero.width - 8f);
            float bottom = Math.Max(28f, hero.txt_chrname.y - 2f);
            float availableHeight = Math.Max(1f,
                bottom - Math.Max(4f, hero.height * .30f));
            float height = Math.Min(availableHeight,
                rows * 79f + (rows - 1) * ContractRowGap);
            var materials = new GList {
                name = "GiftGuide.list_BreakThrough", touchable = false, opaque = false,
                layout = ListLayoutType.FlowHorizontal, columnCount = columns,
                columnGap = (int)ContractColumnGap, lineGap = (int)ContractRowGap,
                autoResizeItem = false
            };
            materials.SetSize(width, height);
            materials.SetXY(4f, bottom - height);
            hero.AddChild(materials);

            float slotWidth = (width - (columns - 1) * ContractColumnGap) / columns;
            float slotHeight = (height - (rows - 1) * ContractRowGap) / rows;
            foreach (ContractRequirement need in usage.ContractRequirements) {
                UIHero_Com_BreakThrough item = UIHero_Com_BreakThrough.CreateInstance();
                if (item == null || !(item.com_Item is UICom_Item icon) || item.txt_Num == null)
                    throw new InvalidOperationException("原版契约材料组件尚未准备好");
                item.touchable = false;
                item.SetSize(slotWidth, slotHeight);
                icon.RefreshItem(need.ItemId, showNum: false);
                icon.touchable = false;
                // Match HeroPanel's txt_Num template. Only the *owned* count
                // is red/green; the required count keeps its native color.
                GTextField number = item.txt_Num;
                number.SetVar("color", need.Owned >= need.Required ? "00FF00" : "FF0000")
                    .SetVar("curNum", need.Owned.ToString())
                    .SetVar("target", need.Required.ToString()).FlushVars();
                number.visible = true;
                number.touchable = false;
                number.singleLine = true;
                number.autoSize = AutoSizeType.None;
                number.verticalAlign = VertAlignType.Middle;
                TextFormat numberFormat = number.textFormat;
                numberFormat.size = ContractCountFontSize;
                numberFormat.align = AlignType.Center;
                number.textFormat = numberFormat;
                number.align = AlignType.Center;
                // One fixed size for all owned/required counts. Slightly more
                // of the available space goes to the icon than in v0.2.19;
                // the count sits closer to, and partly over, its bottom edge.
                float numberHeight = Math.Min(19f, Math.Max(16f, slotHeight * .28f));
                float scale = Math.Min(1.72f, Math.Min(
                    (slotWidth + 4f) / Math.Max(1f, icon.width),
                    (slotHeight - numberHeight * .22f + 2f) / Math.Max(1f, icon.height)));
                scale = Math.Max(.01f, scale);
                icon.SetScale(scale, scale);
                float iconHeight = icon.height * scale;
                float iconY = Math.Max(0f, (slotHeight - iconHeight - numberHeight * .35f) * .5f);
                icon.SetXY((slotWidth - icon.width * scale) * .5f, iconY);
                number.SetSize(slotWidth, numberHeight);
                number.SetXY(0f, Math.Max(0f, Math.Min(slotHeight - numberHeight,
                    iconY + iconHeight - numberHeight * .58f)), true);
                materials.AddChild(item);
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
                // Preserve the original template: owned / required, with
                // only the owned count red/green (same as HeroPanel).
                item.txt_Num.SetVar("color", need.Owned >= need.Required ? "00FF00" : "FF0000")
                    .SetVar("curNum", need.Owned.ToString())
                    .SetVar("target", need.Required.ToString()).FlushVars();
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
