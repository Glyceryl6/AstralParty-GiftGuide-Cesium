using System;
using System.Collections.Generic;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>Bag item usage: native five-column portraits, original description unchanged.</summary>
    internal sealed class PropUsageDescriptionService : IDisposable {
        private readonly PropCharacterAvatarGrid _grid = new PropCharacterAvatarGrid();
        internal bool Enabled { get; set; }

        internal void Tick() {
            if (!Enabled || !UIManager.hasInstance) { Shutdown(); return; }
            PropDetailWindow window = UIManager.inst.propDetail;
            if (!window.isShowing || !(window.contentPane is UIPropDetailWindow pane)) {
                Shutdown();
                return;
            }
            int itemId = WindowItemSelectionReader.FromDetail(window);
            if (itemId <= 0) { Shutdown(); return; }
            if (_grid.IsFor(pane, itemId)) return;
            _grid.Clear();
            if (!PropCharacterUsageReader.TryBuildForItem(itemId, out List<CharacterMaterialUsage> usages) ||
                usages.Count == 0) return;
            _grid.TryShow(pane, itemId, pane.txt_itemDes, pane.btn_item, pane.txt_itemName,
                pane.bottom, new GObject[] { pane.btn_addNum, pane.btn_delNum, pane.btn_Min,
                    pane.btn_Max, pane.txt_Recycle },
                new GObject[] { pane.com_Explain, pane.list_ChestItems, pane.list_PropWay, pane.list_Way },
                false, usages);
        }

        internal void Shutdown() => _grid.Clear();
        public void Dispose() => Shutdown();
    }
}
