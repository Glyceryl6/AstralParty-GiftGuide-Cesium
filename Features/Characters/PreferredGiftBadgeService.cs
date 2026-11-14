using System;
using System.Collections.Generic;
using System.Reflection;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using GameLogic;
using UI;

namespace AstralProbe.Features.Characters {
    /// <summary>使用原版偏好礼物图案（非整个礼物按钮），尺寸和位置沿用旧版。</summary>
    internal sealed class PreferredGiftBadgeService : IDisposable {
        private readonly Dictionary<UIHero_Button_Hero, GLoader> _badges = new();
        private readonly HashSet<UIHero_Button_Hero> _visible = new();
        private UIHeroPanel? _panel;
        private NTexture? _texture;
        private DateTime _next;
        private DateTime _warnAt;
        private static readonly PropertyInfo? InternalVisible = typeof(GObject).GetProperty("internalVisible", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly PropertyInfo? InternalVisible2 = typeof(GObject).GetProperty("internalVisible2", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        internal bool Enabled { get; set; }

        internal void Tick() {
            if (!Enabled || !UIManager.hasInstance || !GameLogicManager.hasInstance) { Clear(); return; }
            if (DateTime.UtcNow < _next) return;
            _next = DateTime.UtcNow.AddMilliseconds(450);
            if (!(UIManager.inst.currentPanel is HeroPanel panel) || panel.UIObject == null ||
                panel.UIObject.isDisposed || !panel.UIObject.onStage) { Clear(); return; }
            try {
                UIHeroPanel ui = panel.UIObject;
                if (!ReferenceEquals(_panel, ui)) { Clear(); _panel = ui; }
                if (_texture == null && !ResolveIcon()) return;
                GList list = ui.com_HeroList.list_Hero;
                BagLogic bag = GameLogicManager.inst.bag;
                HeroCardLogic cards = GameLogicManager.inst.heroCard;
                _visible.Clear();
                for (int i = 0, count = Math.Min(96, list.numChildren); i < count; i++) {
                    if (!(list.GetChildAt(i) is UIHero_Button_Hero card) || card.isDisposed) continue;
                    HeroCardData data = cards.GetCardData(card.HeroId);
                    if (data == null || !data.IsHas || !HasPreferredGift(bag, card.HeroId)) continue;
                    _visible.Add(card);
                    if (!_badges.TryGetValue(card, out GLoader badge) || badge.isDisposed) {
                        badge = new GLoader {
                            name = "GiftGuide.PreferredBadge",
                            touchable = false,
                            autoSize = false,
                            fill = FillType.Scale
                        };
                        badge.SetSize(46f, 46f);
                        badge.texture = _texture;
                        card.AddChild(badge);
                        _badges[card] = badge;
                    }
                    // Old native placement: the icon is independent of the card's own scale.
                    badge.SetXY(18f, Math.Max(18f, card.height - 64f));
                }
                var expired = new List<UIHero_Button_Hero>();
                foreach (var pair in _badges) {
                    if (_visible.Contains(pair.Key) && !pair.Value.isDisposed) continue;
                    DisposeBadge(pair.Value);
                    expired.Add(pair.Key);
                }
                foreach (UIHero_Button_Hero card in expired) _badges.Remove(card);
            } catch (Exception ex) {
                Clear();
                Warn("偏好礼物角标暂不可用：" + ex.Message);
            }
        }

        private static bool HasPreferredGift(BagLogic bag, int heroId) {
            CharacterHeroFavorGiftConfigure config = CharacterHandle.GetCharacterHeroFavorGiftConfigure(heroId);
            if (config == null) return false;
            var items = config.CharacterHeroFavorGiftConfigureItems;
            for (int i = 0, count = Math.Min(128, items.Count); i < count; i++)
                if (bag.GetItemCount(items[i].ItemID) > 0) return true;
            return false;
        }

        private bool ResolveIcon() {
            UIHero_Button_GiftItem? template = null;
            try {
                template = UIHero_Button_GiftItem.CreateInstance();
                if (template == null) return false;
                // Old algorithm: identify the one image added by the game's "liked" state.
                template.like.selectedIndex = 0;
                var before = new HashSet<GImage>();
                CollectVisibleImages(template, before, 0);
                template.like.selectedIndex = 1;
                var after = new HashSet<GImage>();
                CollectVisibleImages(template, after, 0);
                after.ExceptWith(before);
                if (after.Count != 1) {
                    Warn("原版礼物偏好图案无法唯一定位，稍后重试");
                    return false;
                }
                foreach (GImage image in after) _texture = image.texture;
                return _texture != null;
            } finally {
                if (template != null && !template.isDisposed) template.Dispose();
            }
        }

        private static bool IsDisplayed(GObject obj) {
            // Internal controller visibility is separate from GObject.visible.
            try {
                if (InternalVisible != null && InternalVisible.GetValue(obj) is bool a && !a) return false;
                if (InternalVisible2 != null && InternalVisible2.GetValue(obj) is bool b && !b) return false;
            } catch { return obj.visible; }
            return obj.visible;
        }

        private static void CollectVisibleImages(GObject obj, HashSet<GImage> images, int depth) {
            if (obj == null || depth > 6 || !IsDisplayed(obj)) return;
            if (obj is GImage image) { images.Add(image); return; }
            if (!(obj is GComponent group)) return;
            for (int i = 0, count = Math.Min(48, group.numChildren); i < count; i++)
                CollectVisibleImages(group.GetChildAt(i), images, depth + 1);
        }

        private void Warn(string message) {
            if (DateTime.UtcNow < _warnAt) return;
            _warnAt = DateTime.UtcNow.AddSeconds(30);
            SdkLog.Warn("GiftGuide", message);
        }

        private static void DisposeBadge(GLoader badge) {
            if (badge.isDisposed) return;
            badge.RemoveFromParent();
            badge.Dispose();
        }

        internal void Clear() {
            foreach (GLoader badge in _badges.Values) DisposeBadge(badge);
            _badges.Clear();
            _visible.Clear();
            _panel = null;
            // Preserve the package-owned NTexture while Hero package remains loaded.
            // On the next Hero panel the original state can be queried again.
            _texture = null;
        }
        public void Dispose() => Clear();
    }
}
