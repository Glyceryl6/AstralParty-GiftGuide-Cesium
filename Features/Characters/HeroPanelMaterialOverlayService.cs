#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using AstralProbe.Features.Inventory;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using GameLogic;
using UI;

namespace AstralProbe.Features.Characters {
    /// <summary>
    /// Adds temporary, Mod-owned labels/material cells to HeroPanel's visible
    /// native hero cards. Never replaces itemRenderer, mutates com_show, or
    /// registers/replaces the game's UI component constructors.
    /// </summary>
    internal sealed class HeroPanelMaterialOverlayService : IDisposable {
        private sealed class OverlayState {
            internal readonly string Signature;
            internal readonly List<GObject> Children = new();
            internal OverlayState(string signature) { Signature = signature; }
        }

        private readonly Dictionary<UIHero_Button_Hero, OverlayState> _states = new();
        private readonly HashSet<UIHero_Button_Hero> _visible = new();
        private UIHeroPanel? _panel;
        private PropCharacterAvatarGrid.FavorTitleAppearance? _favorStyle;
        private DateTime _next;
        private DateTime _warnAt;
        private bool _enabled;

        internal bool Enabled {
            get => _enabled;
            set {
                if (_enabled == value) return;
                _enabled = value;
                if (!value) Clear();
            }
        }

        internal void Tick() {
            if (!Enabled || !UIManager.hasInstance || !GameLogicManager.hasInstance) {
                Clear();
                return;
            }
            if (DateTime.UtcNow < _next) return;
            _next = DateTime.UtcNow.AddMilliseconds(450);
            if (!(UIManager.inst.currentPanel is HeroPanel panel) ||
                panel.UIObject == null || panel.UIObject.isDisposed ||
                !panel.UIObject.onStage) {
                Clear();
                return;
            }
            try {
                UIHeroPanel ui = panel.UIObject;
                if (!ReferenceEquals(_panel, ui)) {
                    Clear();
                    _panel = ui;
                    _favorStyle = PropCharacterAvatarGrid.ReadNativeFavorTitle();
                }
                // HeroPanel's selector defines 0=default, 1=favor, 2=PVE.
                // In the default mode there must be NO Mod-added materials.
                int sort = ui.com_HeroSort.selectedIndex;
                if (ui.tab.selectedIndex != 0 || (sort != 1 && sort != 2)) {
                    RemoveAllOverlays();
                    return;
                }

                GList list = ui.com_HeroList.list_Hero;
                _visible.Clear();
                for (int i = 0, n = Math.Min(96, list.numChildren); i < n; i++) {
                    if (!(list.GetChildAt(i) is UIHero_Button_Hero card) ||
                        card.isDisposed || !card.onStage || card.HeroId <= 0) continue;
                    _visible.Add(card);
                    if (!CharacterMaterialUsageReader.TryReadHero(card.HeroId,
                            out CharacterMaterialUsage usage)) {
                        RemoveCard(card);
                        continue;
                    }
                    string signature = Signature(sort, usage);
                    if (_states.TryGetValue(card, out OverlayState? current) &&
                        current.Signature == signature && IsAlive(card, current)) continue;
                    RemoveCard(card);
                    var entry = new OverlayState(signature);
                    try {
                        if (usage.Owned) {
                            if (sort == 1) {
                                float reserveTop = 0f;
                                if (!usage.FavorDeepened) {
                                    GTextField exp = PropCharacterAvatarGrid.AddHeroStatus(card,
                                        usage.FavorExp + "/" + usage.FavorNeedExp, _favorStyle);
                                    entry.Children.Add(exp);
                                    if (!usage.ContractSigned && usage.ContractRequirements.Count > 0) {
                                        // The native status icon is differently positioned
                                        // across skins. Reserve room for materials first so
                                        // the extra EXP row cannot flatten their cells.
                                        float materialBottom = Math.Max(28f,
                                            card.txt_chrname.y - 18f);
                                        float materialRoom = Math.Min(80f,
                                            Math.Max(38f, card.height * .38f));
                                        float latestExpBottom = materialBottom - materialRoom - 4f;
                                        if (exp.y + exp.height > latestExpBottom)
                                            exp.SetXY(exp.x, Math.Max(2f,
                                                latestExpBottom - exp.height));
                                    }
                                    reserveTop = exp.y + exp.height + 4f;
                                }
                                if (!usage.ContractSigned) {
                                    GList? materials = PropCharacterAvatarGrid.AddHeroMaterials(
                                        card, usage.ContractRequirements, false, _favorStyle, reserveTop);
                                    if (materials != null) entry.Children.Add(materials);
                                }
                            } else if (!usage.PotentialAwakened) {
                                GList? materials = PropCharacterAvatarGrid.AddHeroMaterials(
                                    card, usage.PotentialRequirements, true, _favorStyle);
                                if (materials != null) entry.Children.Add(materials);
                            }
                        }
                        _states[card] = entry;
                    } catch {
                        DisposeChildren(entry);
                        throw;
                    }
                }
                var expired = new List<UIHero_Button_Hero>();
                foreach (UIHero_Button_Hero card in _states.Keys)
                    if (!_visible.Contains(card) || card.isDisposed) expired.Add(card);
                foreach (UIHero_Button_Hero card in expired) RemoveCard(card);
            } catch (Exception ex) {
                RemoveAllOverlays();
                if (DateTime.UtcNow >= _warnAt) {
                    _warnAt = DateTime.UtcNow.AddSeconds(30);
                    SdkLog.Warn("GiftGuide", "英雄卡片排序材料显示暂不可用：" + ex);
                }
            }
        }

        private static string Signature(int sort, CharacterMaterialUsage usage) {
            var text = new StringBuilder(80);
            text.Append(usage.HeroId).Append(':').Append(sort).Append(':')
                .Append(usage.Owned).Append(':');
            if (sort == 1) {
                text.Append(usage.ContractSigned).Append(':')
                    .Append(usage.FavorDeepened).Append(':')
                    .Append(usage.FavorLevel).Append(':')
                    .Append(usage.FavorExp).Append('/').Append(usage.FavorNeedExp);
                if (!usage.ContractSigned) AppendRequirements(text, usage.ContractRequirements);
            } else {
                text.Append(usage.PotentialAwakened).Append(':').Append(usage.PotentialLevel);
                if (!usage.PotentialAwakened) AppendRequirements(text, usage.PotentialRequirements);
            }
            return text.ToString();
        }

        private static void AppendRequirements(StringBuilder text,
            IReadOnlyList<ContractRequirement> requirements) {
            foreach (ContractRequirement need in requirements)
                text.Append('|').Append(need.ItemId).Append(':')
                    .Append(need.Owned).Append('/').Append(need.Required);
        }

        private static bool IsAlive(UIHero_Button_Hero card, OverlayState state) {
            foreach (GObject overlay in state.Children)
                if (overlay.isDisposed || !ReferenceEquals(overlay.parent, card)) return false;
            return true;
        }

        private static void DisposeChildren(OverlayState state) {
            foreach (GObject overlay in state.Children) {
                if (overlay.isDisposed) continue;
                overlay.RemoveFromParent();
                overlay.Dispose();
            }
            state.Children.Clear();
        }

        private void RemoveCard(UIHero_Button_Hero card) {
            if (!_states.TryGetValue(card, out OverlayState? state)) return;
            DisposeChildren(state);
            _states.Remove(card);
        }

        private void RemoveAllOverlays() {
            foreach (OverlayState state in _states.Values) DisposeChildren(state);
            _states.Clear();
            _visible.Clear();
        }

        internal void Clear() {
            RemoveAllOverlays();
            _panel = null;
            _favorStyle = null;
        }

        public void Dispose() => Clear();
    }
}
