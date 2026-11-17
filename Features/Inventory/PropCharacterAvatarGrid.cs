#nullable enable
using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using FairyGUI.Utils;
using UnityEngine;
using Core;
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
        private const int ContractCountFontSize = 21;
        // v0.2.22's light, outlined label was readable; use a slightly
        // larger fixed face without inheriting the dark native slider fill.
        private const int FavorExpFontSize = 20;
        private const float HeadingHeight = 36f;
        private const float SectionGap = 12f;
        private const float ContractColumnGap = 2f;
        private const float ContractRowGap = 2f;
        // Exact pink used by SettingWindow for its active controls.
        private static readonly Color SettingPink = new Color(1f, .1686f, .5686f, 1f);
        // Do not instantiate and dispose an entire HeroPanel just to read a
        // font: the native panel contains external loaders/effect resources.
        // Likewise, don't replace extension creators already owned by the game.
        private static void EnsureNativeExtension(string url, Type type) {
            PackageItem? asset = UIPackage.GetItemByURL(url);
            if (asset != null && asset.extensionCreator == null)
                UIObjectFactory.SetPackageItemExtension(url, type);
        }

        // Capture the *small* native slider's title, not a temporary HeroPanel.
        // TextFormat.CopyFrom omits outline/shadow fields, so keep those too.
        private sealed class FavorTitleAppearance {
            internal readonly TextFormat Format = new TextFormat();
            internal readonly float Stroke;
            internal readonly Color StrokeColor;

            internal FavorTitleAppearance(GTextField source) {
                TextFormat original = source.textFormat;
                Format.CopyFrom(original);
                Format.outline = original.outline;
                Format.outlineColor = original.outlineColor;
                Format.shadowOffset = original.shadowOffset;
                Format.shadowColor = original.shadowColor;
                Format.gradientColor = original.gradientColor;
                Stroke = Math.Max(source.stroke, original.outline);
                StrokeColor = original.outline > 0f ? original.outlineColor : source.strokeColor;
            }

            internal void ApplyTo(GTextField field) {
                TextFormat target = field.textFormat;
                target.CopyFrom(Format);
                target.outline = Format.outline;
                target.outlineColor = Format.outlineColor;
                target.shadowOffset = Format.shadowOffset;
                target.shadowColor = Format.shadowColor;
                // The native progress title sometimes has a dark fill. Keep
                // its font metrics but restore the readable light v0.2.22 fill.
                target.size = FavorExpFontSize;
                target.color = new Color(1f, .975f, .99f, 1f);
                target.gradientColor = null;
                field.textFormat = target;
                ApplyOutline(field);
            }

            internal void ApplyOutline(GTextField field) {
                // Always use the same dark fine edge for experience and the
                // two types of material counts. Never touch the source title.
                field.stroke = 1.25f;
                field.strokeColor = new Color(.22f, .12f, .27f, 1f);
            }
        }

        private static FavorTitleAppearance? ReadNativeFavorTitle() {
            GObject? slider = null;
            try {
                // Force the base progress-bar class; we only need its authored
                // GTextField named 'title', not a live HeroPanel or game logic.
                slider = UIPackage.CreateObjectFromURL(UIHero_bar_FavorValue.URL,
                    typeof(GProgressBar));
                GTextField? title = (slider as GComponent)?.GetChild("title") as GTextField;
                return title == null ? null : new FavorTitleAppearance(title);
            } catch (Exception ex) {
                SdkLog.Warn("GiftGuide", "原版友情进度文字样式暂不可用：" + ex.Message);
                return null;
            } finally {
                slider?.Dispose();
            }
        }

        private GuideViewport? _viewport;
        private GComponent? _pane;
        private PropPopupLayout? _layout;
        private int _item;
        private DateTime _retry;
        private static bool _loggedDetailViewport;

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
                EnsureNativeExtension(UICom_GoodsQuality_Store.URL, typeof(UICom_GoodsQuality_Store));
                EnsureNativeExtension(UICom_GoodsItemName.URL, typeof(UICom_GoodsItemName));
                EnsureNativeExtension(UICom_Label.URL, typeof(UICom_Label));
                EnsureNativeExtension(UICom_ItemType.URL, typeof(UICom_ItemType));
                EnsureNativeExtension(UICom_Price.URL, typeof(UICom_Price));
                EnsureNativeExtension(UIButton_GoodsItem_Store.URL, typeof(UIButton_GoodsItem_Store));
                EnsureNativeExtension(UICom_Item.URL, typeof(UICom_Item));
                EnsureNativeExtension(UIHero_Com_BreakThrough.URL, typeof(UIHero_Com_BreakThrough));
                EnsureNativeExtension(UIHero_Com_TalentItem.URL, typeof(UIHero_Com_TalentItem));
                EnsureNativeExtension(UIHero_com_show.URL, typeof(UIHero_com_show));
                EnsureNativeExtension(UIHero_Com_HeroItem.URL, typeof(UIHero_Com_HeroItem));
                EnsureNativeExtension(UIHero_Button_Hero.URL, typeof(UIHero_Button_Hero));
                EnsureNativeExtension(UICom_SkinQuality.URL, typeof(UICom_SkinQuality));
                EnsureNativeExtension(UIButton_Collect.URL, typeof(UIButton_Collect));

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
                // Read authored dimensions from the package metadata instead
                // of creating a live hero card and immediately disposing it.
                // The native card's Dispose() stops its effect graph, and a
                // temporary full HeroPanel can also touch shared UI resources.
                PackageItem? heroAsset = UIPackage.GetItemByURL(UIHero_Button_Hero.URL);
                if (heroAsset != null && heroAsset.width > 0 && heroAsset.height > 0) {
                    useHeroCards = true;
                    cellWidth = heroAsset.width;
                    cellHeight = heroAsset.height;
                }
                if (!useHeroCards) {
                    PackageItem? shopAsset = UIPackage.GetItemByURL(UIButton_GoodsItem_Store.URL);
                    if (shopAsset == null || shopAsset.width <= 0 || shopAsset.height <= 0)
                        throw new InvalidOperationException("原版角色卡片和商店卡片资源均不可用");
                    cellWidth = shopAsset.width;
                    cellHeight = shopAsset.height;
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
                FavorTitleAppearance? favorTitle = ReadNativeFavorTitle();
                var sections = new GComponent {
                    name = "GiftGuide.CharacterSections",
                    touchable = false,
                    opaque = false
                };
                var scrollLabels = new List<GTextField>();
                float y = Padding;
                y = AddSection(sections, usages, 0, "契约", name, description,
                    inner, columns, cellWidth, cellHeight, nativeFont, useHeroCards,
                    favorTitle, y, scrollLabels);
                y = AddSection(sections, usages, 1, "潜能", name, description,
                    inner, columns, cellWidth, cellHeight, nativeFont, useHeroCards,
                    favorTitle, y, scrollLabels);
                y = AddSection(sections, usages, 2, "偏好礼物", name, description,
                    inner, columns, cellWidth, cellHeight, nativeFont, useHeroCards,
                    favorTitle, y, scrollLabels);
                if (y <= Padding) {
                    sections.Dispose();
                    return false;
                }

                sections.SetSize(available, y + Padding);
                created.AddChild(sections);
                created.EnsureBoundsCorrect();
                stage = "添加角色列表到原版窗口";
                pane.AddChild(created);
                // Apply the layout after parenting as well, so a native
                // container relation cannot silently restore the old Y.
                created.SetXY(layout.GuideLeft, layout.GuideTop, true);
                created.EnsureBoundsCorrect();
                if (!afterFooter && !_loggedDetailViewport) {
                    _loggedDetailViewport = true;
                    SdkLog.Warn("GiftGuide", "物品详情角色列表定位：Y=" +
                        created.y.ToString("F1") + "，高度=" +
                        created.height.ToString("F1") + "，弹窗顶部=" +
                        background.y.ToString("F1") + "。如仍有空白，请保留该条日志。");
                }
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
            bool useHeroCards, FavorTitleAppearance? favorTitle,
            float y, List<GTextField> scrollLabels) {
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
                    nativeFont, useHeroCards, favorTitle, scrollLabels));
            list.EnsureBoundsCorrect();
            return y + listHeight;
        }

        private static GComponent BuildCharacterCard(CharacterMaterialUsage usage,
            int group, float cellWidth, float cellHeight, string nativeFont,
            bool useHeroCards, FavorTitleAppearance? favorTitle,
            List<GTextField> scrollLabels) {
            return useHeroCards
                ? BuildHeroCharacterCard(usage, group, cellWidth, cellHeight, nativeFont, favorTitle)
                : BuildLegacyStoreCard(usage, group, cellWidth, cellHeight,
                    nativeFont, favorTitle, scrollLabels);
        }

        private static GComponent BuildHeroCharacterCard(CharacterMaterialUsage usage,
            int group, float cellWidth, float cellHeight, string nativeFont,
            FavorTitleAppearance? favorTitle) {
            UIHero_Button_Hero hero = UIHero_Button_Hero.CreateInstance();
            if (hero == null || hero.com_Loader?.loader_Character == null ||
                hero.com_show == null || hero.txt_chrname == null)
                throw new InvalidOperationException("HeroPanel 角色卡片结构与当前游戏版本不匹配");
            hero.name = "GiftGuide.Hero." + usage.HeroId;
            hero.HeroId = usage.HeroId;
            hero.touchable = false;
            hero.opaque = false;
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
            // Keep the actual Hero_Button_Hero.txt_chrname font and its
            // authored size. A full-panel style copy previously changed the
            // native name appearance and lacked several outline properties.
            GTextField caption = hero.txt_chrname;
            caption.text = usage.Name;
            caption.visible = true;
            caption.touchable = false;
            caption.tooltips = usage.Name;
            caption.singleLine = true;
            caption.autoSize = AutoSizeType.Shrink;
            caption.verticalAlign = VertAlignType.Middle;
            if (usage.ContractSigned) {
                // Contracted heroes use black text, even when the native name
                // template uses a multi-stop fill gradient.
                TextFormat nameFormat = caption.textFormat;
                nameFormat.color = Color.black;
                nameFormat.gradientColor = null;
                caption.textFormat = nameFormat;
            }
            float nameHeight = Math.Min(HeroNameHeight, Math.Max(22f, hero.height * .24f));
            // This is the full-card coordinate system that actually centered
            // the name in v0.2.19. The later pivot-ignoring SetXY moved it right.
            // Leave it slightly lower, close to the native hero-button layout.
            caption.SetSize(Math.Max(1f, hero.width - 8f), nameHeight);
            // The native authored pivot/relation offsets shift its visible glyphs
            // to the right. Move just this card's caption left and down.
            caption.SetXY(-9f, Math.Max(2f, hero.height - nameHeight - 1f));
            caption.align = AlignType.Center;
            // The original selector grays out an unavailable hero button.
            hero.grayed = !usage.Owned;

            hero.com_show.visible = false;
            hero.com_show.releationPowerUp.selectedIndex = 0;
            hero.com_show.Status.selectedIndex = 0;

            var cell = new GComponent { name = "GiftGuide.HeroCell", touchable = false,
                opaque = false, tooltips = usage.Name };
            cell.SetSize(cellWidth, cellHeight);
            cell.AddChild(hero);

            // Use the same native com_show levels as HeroPanel's two sorting
            // modes, not only the completion states. Unowned portraits stay
            // grayscale and never receive an experience overlay.
            if (group == 2 && usage.Owned) {
                hero.com_show.visible = true;
                hero.com_show.Status.selectedIndex = 1;
                hero.com_show.releationPowerUp.selectedIndex = usage.FavorDeepened ? 1 : 0;
                hero.com_show.LV_txt.text = usage.FavorLevel.ToString();
                if (!usage.FavorDeepened)
                    AddHeroStatus(hero, usage.FavorExp + "/" + usage.FavorNeedExp,
                        favorTitle);
            } else if (group == 1 && usage.Owned) {
                hero.com_show.visible = true;
                hero.com_show.Status.selectedIndex = usage.PotentialAwakened ? 3 : 2;
                hero.com_show.releationPowerUp.selectedIndex = 0;
                hero.com_show.LV_txt.text = usage.PotentialLevel.ToString();
            }
            if (group == 0) {
                if (usage.ContractSigned) AddGiftCompletionArt(hero, caption.y - 4f);
                else AddHeroMaterials(hero, usage.ContractRequirements, false, favorTitle);
            } else if (group == 1 && !usage.PotentialAwakened) {
                // Always show talent materials, even when all are available.
                AddHeroMaterials(hero, usage.PotentialRequirements, true, favorTitle);
            }
            return cell;
        }

        private static void AddHeroStatus(UIHero_Button_Hero hero, string status,
            FavorTitleAppearance? favorTitle) {
            UIHero_com_show anchor = hero.com_show;
            float width = Math.Max(1f, hero.width - 8f);
            var text = new GTextField {
                name = "GiftGuide.HeroStatus", text = status, touchable = false,
                autoSize = AutoSizeType.None, singleLine = true,
                verticalAlign = VertAlignType.Middle
            };
            if (favorTitle != null) favorTitle.ApplyTo(text);
            else {
                // Resource-version fallback; keep native font and a thin
                // readable outline rather than estimating a tiny 18px label.
                TextFormat format = text.textFormat;
                format.CopyFrom(hero.txt_chrname.textFormat);
                format.size = FavorExpFontSize;
                format.color = new Color(1f, .975f, .99f, 1f);
                format.gradientColor = null;
                text.textFormat = format;
                text.stroke = 1f;
                text.strokeColor = new Color(.22f, .12f, .27f, 1f);
            }
            text.SetPivot(0f, 0f, false);
            text.align = AlignType.Center;
            text.autoSize = AutoSizeType.None;
            float height = Math.Max(26f, anchor.height);
            text.SetSize(width, height);
            text.SetXY(4f, Math.Max(2f,
                Math.Min(hero.txt_chrname.y - height - 3f,
                    anchor.y + anchor.height + 2f)));
            hero.AddChild(text);
        }

        /// <summary>
        /// Show the original Hero_Com_Gift completion artwork: image_CN and
        /// image_EN are separate GImages, selected just as HeroPanel does.
        /// Create only the small gift component as an untyped template; never
        /// create/destroy a full HeroPanel or replace its extension mappings.
        /// </summary>
        private static void AddGiftCompletionArt(GComponent card, float bottom) {
            GObject? template = null;
            try {
                template = UIPackage.CreateObjectFromURL(UIHero_Com_Gift.URL,
                    typeof(GComponent));
                if (!(template is GComponent source) || source.numChildren <= 17 ||
                    !(source.GetChildAt(16) is GImage chinese) ||
                    !(source.GetChildAt(17) is GImage english))
                    throw new InvalidOperationException("原版礼物完成图标结构不匹配");
                source.RemoveChild(chinese, false);
                source.RemoveChild(english, false);
                float nativeW = Math.Max(1f, Math.Max(chinese.width, english.width));
                float nativeH = Math.Max(1f, Math.Max(chinese.height, english.height));
                var art = new GComponent {
                    name = "GiftGuide.ContractComplete", touchable = false, opaque = false
                };
                art.SetSize(nativeW, nativeH);
                chinese.SetXY((nativeW - chinese.width) * .5f,
                    (nativeH - chinese.height) * .5f);
                english.SetXY((nativeW - english.width) * .5f,
                    (nativeH - english.height) * .5f);
                chinese.visible = GameSettings.languageType == LanguageType.SimplifiedChinese;
                english.visible = !chinese.visible;
                art.AddChild(chinese);
                art.AddChild(english);
                float scale = Math.Min(1f, Math.Min(
                    (card.width - 18f) / nativeW,
                    Math.Max(20f, bottom - card.height * .24f) / nativeH));
                art.SetScale(scale, scale);
                art.SetXY((card.width - nativeW * scale) * .5f,
                    Math.Max(2f, bottom - nativeH * scale));
                card.AddChild(art);
            } catch (Exception ex) {
                SdkLog.Warn("GiftGuide", "原版契约完成图标暂不可用：" + ex.Message);
            } finally {
                template?.Dispose();
            }
        }

        private static void AddHeroMaterials(UIHero_Button_Hero hero,
            IReadOnlyList<ContractRequirement> requirements, bool potential,
            FavorTitleAppearance? favorTitle) {
            int count = requirements.Count;
            if (count == 0) return;

            // Pull each icon and its count inward together, keeping both rows
            // directly above the name (the original portrait stays untouched).
            int columns = count <= 3 ? count : count == 4 ? 2 : 3;
            int rows = (count + columns - 1) / columns;
            float width = Math.Max(1f, hero.width - 24f);
            float bottom = Math.Max(28f, hero.txt_chrname.y - 7f);
            float availableHeight = Math.Max(1f,
                bottom - Math.Max(4f, hero.height * .25f));
            float height = Math.Min(availableHeight,
                rows * 86f + (rows - 1) * ContractRowGap);
            var materials = new GList {
                name = potential ? "GiftGuide.list_PotentialMaterials" :
                    "GiftGuide.list_BreakThrough",
                touchable = false, opaque = false,
                layout = ListLayoutType.FlowHorizontal, columnCount = columns,
                columnGap = (int)ContractColumnGap, lineGap = (int)ContractRowGap,
                autoResizeItem = false
            };
            materials.SetSize(width, height);
            materials.SetXY((hero.width - width) * .5f, bottom - height);
            hero.AddChild(materials);

            float slotWidth = (width - (columns - 1) * ContractColumnGap) / columns;
            float slotHeight = (height - (rows - 1) * ContractRowGap) / rows;
            foreach (ContractRequirement need in requirements) {
                // Talent materials use the matching native HeroPanel template.
                GComponent item;
                GButton icon;
                GTextField number;
                if (potential) {
                    UIHero_Com_TalentItem talent = UIHero_Com_TalentItem.CreateInstance();
                    if (talent == null || talent.txt_Num == null || talent.com_Item == null)
                        throw new InvalidOperationException("原版潜能材料组件尚未准备好");
                    item = talent;
                    icon = talent.com_Item;
                    number = talent.txt_Num;
                } else {
                    UIHero_Com_BreakThrough contract = UIHero_Com_BreakThrough.CreateInstance();
                    if (contract == null || contract.txt_Num == null || contract.com_Item == null)
                        throw new InvalidOperationException("原版契约材料组件尚未准备好");
                    item = contract;
                    icon = contract.com_Item;
                    number = contract.txt_Num;
                }
                if (!(icon is UICom_Item nativeIcon))
                    throw new InvalidOperationException("原版材料图标结构不匹配");
                item.touchable = false;
                item.SetSize(slotWidth, slotHeight);
                nativeIcon.RefreshItem(need.ItemId, showNum: false);
                icon.touchable = false;
                number.SetVar("color", need.Owned >= need.Required ? "00FF00" : "FF0000")
                    .SetVar("curNum", need.Owned.ToString())
                    .SetVar("target", need.Required.ToString()).FlushVars();
                number.visible = true;
                number.touchable = false;
                number.singleLine = true;
                number.autoSize = AutoSizeType.None;
                number.verticalAlign = VertAlignType.Middle;
                TextFormat format = number.textFormat;
                format.size = ContractCountFontSize;
                format.align = AlignType.Center;
                number.textFormat = format;
                number.align = AlignType.Center;
                if (favorTitle != null) favorTitle.ApplyOutline(number);
                else {
                    number.stroke = 1f;
                    number.strokeColor = new Color(.22f, .12f, .27f, 1f);
                }
                float numberHeight = Math.Min(29f, Math.Max(24f, slotHeight * .34f));
                float scale = Math.Min(1.92f, Math.Min(
                    (slotWidth + 9f) / Math.Max(1f, icon.width),
                    (slotHeight - numberHeight * .12f + 3f) / Math.Max(1f, icon.height)));
                scale = Math.Max(.01f, scale);
                icon.SetScale(scale, scale);
                float iconHeight = icon.height * scale;
                float iconY = Math.Max(0f,
                    (slotHeight - iconHeight - numberHeight * .35f) * .5f - 3f);
                icon.SetXY((slotWidth - icon.width * scale) * .5f, iconY);
                number.SetSize(slotWidth, numberHeight);
                number.SetXY(0f, Math.Max(0f, Math.Min(slotHeight - numberHeight,
                    iconY + iconHeight - numberHeight * .72f - 2f)));
                materials.AddChild(item);
            }
            materials.EnsureBoundsCorrect();
        }

        private static GComponent BuildLegacyStoreCard(CharacterMaterialUsage usage,
            int group, float cellWidth, float cellHeight, string nativeFont,
            FavorTitleAppearance? favorTitle, List<GTextField> scrollLabels) {
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
            native.grayed = !usage.Owned;
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
            bool showIcon = usage.Owned && (group == 1 || group == 2);
            string status = group == 2 && usage.Owned
                ? usage.FavorExp + "/" + usage.FavorNeedExp : "";
            native.com_Price.visible = group == 2 && usage.Owned && !usage.FavorDeepened;
            native.com_Price.isDiscount.selectedIndex = 0;
            if (native.com_Price.group_Discount != null)
                native.com_Price.group_Discount.visible = false;
            if (native.com_Price.txt_OriginalPrice != null)
                native.com_Price.txt_OriginalPrice.visible = false;
            if (native.com_Price.txt_DiscountPercent != null)
                native.com_Price.txt_DiscountPercent.visible = false;
            price.text = status;
            if (group == 2 && favorTitle != null) {
                favorTitle.ApplyTo(price);
                price.autoSize = AutoSizeType.None;
                price.align = AlignType.Center;
            }

            if (showIcon) {
                UIHero_com_show marker = UIHero_com_show.CreateInstance();
                marker.touchable = false;
                marker.Status.selectedIndex = group == 2 ? 1 :
                    usage.PotentialAwakened ? 3 : 2;
                marker.releationPowerUp.selectedIndex = group == 2 && usage.FavorDeepened ? 1 : 0;
                marker.LV_txt.text = (group == 2 ? usage.FavorLevel : usage.PotentialLevel).ToString();
                GComponent target = native.com_Price;
                float scale = Math.Min(1f, Math.Min(
                    (target.width - 4f) / Math.Max(1f, marker.width),
                    (target.height - 2f) / Math.Max(1f, marker.height)));
                marker.SetScale(scale, scale);
                marker.SetXY(target.x + (target.width - marker.width * scale) * .5f,
                    group == 2 && !usage.FavorDeepened
                        ? Math.Max(2f, target.y - marker.height * scale - 2f)
                        : target.y + (target.height - marker.height * scale) * .5f);
                native.AddChild(marker);
            }

            if (group == 0) {
                if (usage.ContractSigned)
                    AddGiftCompletionArt(native, native.com_ItemName.y - 4f);
                else AddContractMaterials(native, usage.ContractRequirements,
                    cellWidth, cellHeight, favorTitle);
            } else if (group == 1 && !usage.PotentialAwakened)
                AddContractMaterials(native, usage.PotentialRequirements,
                    cellWidth, cellHeight, favorTitle);
            return native;
        }

        private static void AddContractMaterials(UIButton_GoodsItem_Store native,
            IReadOnlyList<ContractRequirement> requirements, float cellWidth, float cellHeight,
            FavorTitleAppearance? favorTitle) {
            int count = requirements.Count;
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
            foreach (ContractRequirement need in requirements) {
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
                if (favorTitle != null) favorTitle.ApplyOutline(item.txt_Num);
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
