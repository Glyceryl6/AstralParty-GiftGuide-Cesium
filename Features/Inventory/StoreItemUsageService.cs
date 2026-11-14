using System;
using System.Collections.Generic;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>Shop item usage: use native portrait cells without altering the description.</summary>
    internal sealed class StoreItemUsageService : IDisposable {
        private readonly PropCharacterAvatarGrid _grid = new PropCharacterAvatarGrid();
        internal bool Enabled { get; set; }

        internal void Tick() {
            if (!Enabled || !UIManager.hasInstance) { Shutdown(); return; }
            PurchaseWindow window = UIManager.inst.purchase;
            if (!window.isShowing || !(window.contentPane is UIPurchaseWindow pane) ||
                pane.WinType.selectedIndex != 0) { Shutdown(); return; }
            int itemId = WindowItemSelectionReader.FromPurchase(window);
            if (itemId <= 0) { Shutdown(); return; }
            if (_grid.IsFor(pane, itemId)) return;
            _grid.Clear();
            if (!PropCharacterUsageReader.TryBuildForItem(itemId, out List<CharacterMaterialUsage> usages) ||
                usages.Count == 0) return;
            _grid.TryShow(pane, itemId, pane.txt_itemDes, pane.btn_itemLoader, pane.txt_itemName,
                pane.bottom, new GObject[] { pane.btn_addNum, pane.btn_delNum, pane.btn_Min,
                    pane.btn_Max, pane.txt_TotalPrice, pane.loader_Currency_Total,
                    pane.btn_DelLv, pane.btn_AddLv }, usages);
        }

        internal void Shutdown() => _grid.Clear();
        public void Dispose() => Shutdown();
    }
}
