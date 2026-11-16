#nullable enable
using System;
using System.Collections.Generic;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>Shows the selected optional reward below the chest's native buttons.</summary>
    internal sealed class BoxPropUsageService : IDisposable {
        private readonly PropCharacterAvatarGrid _grid = new PropCharacterAvatarGrid();
        internal bool Enabled { get; set; }

        internal void Tick() {
            if (!Enabled || !UIManager.hasInstance) { Shutdown(); return; }
            BoxPropWindow window = UIManager.inst.boxProp;
            if (!window.isShowing || !(window.contentPane is UIBoxPropWindow pane) ||
                pane.type.selectedIndex != 1 || pane.com_Chest == null) {
                Shutdown();
                return;
            }
            int itemId = WindowItemSelectionReader.FromBoxProp(window);
            if (itemId <= 0) { Shutdown(); return; }
            if (_grid.IsFor(pane, itemId)) return;
            _grid.Clear();
            if (!PropCharacterUsageReader.TryBuildForItem(itemId,
                out List<CharacterMaterialUsage> usages) || usages.Count == 0) return;
            // Buttons and the reward selection list belong to com_Chest rather
            // than the outer window. Treat the entire component as native
            // content, leaving all of it above the new scroll viewport.
            _grid.TryShow(pane, itemId, pane.com_Chest.txt_ItemDesc, null,
                pane.com_Chest.txt_title, pane.bottom,
                new GObject[] { pane.com_Chest }, new GObject[] { pane.com_Chest },
                true, usages);
        }

        internal void Shutdown() => _grid.Clear();
        public void Dispose() => Shutdown();
    }
}
