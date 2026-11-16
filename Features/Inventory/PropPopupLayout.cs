#nullable enable
using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Resize the original Com_PopUpWindow_Bottom (not the full-screen contentPane).
    /// All authored controls keep their relative positions; the detail window
    /// gets its list before the moved footer, while the purchase list is below
    /// the original purchase buttons. Every changed object is restored on close.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private const float Edge = 12f;
        private const float Inner = 15f;
        private const float Gap = 10f;
        private const float DesiredExtraHeight = 310f;
        private readonly GComponent _pane;
        private readonly GLabel _background;
        private readonly Snapshot _originalBackground;
        private readonly List<Snapshot> _paneChildren = new List<Snapshot>();
        private readonly List<Snapshot> _backgroundChildren = new List<Snapshot>();
        private bool _disposed;
        private float _left, _top, _width, _height;

        private sealed class Snapshot {
            internal readonly GObject Item;
            internal readonly float X, Y, Width, Height;
            internal Snapshot(GObject item) {
                Item = item;
                X = item.x; Y = item.y; Width = item.width; Height = item.height;
            }
            internal void Restore() {
                if (Item == null || Item.isDisposed) return;
                if (Item.width != Width || Item.height != Height)
                    Item.SetSize(Width, Height, true);
                Item.SetXY(X, Y);
            }
        }

        private PropPopupLayout(GComponent pane, GLabel background) {
            _pane = pane;
            _background = background;
            _originalBackground = new Snapshot(background);
            foreach (GObject child in pane.GetChildren())
                if (child != null && !child.isDisposed && !ReferenceEquals(child, background))
                    _paneChildren.Add(new Snapshot(child));
            foreach (GObject child in background.GetChildren())
                if (child != null && !child.isDisposed)
                    _backgroundChildren.Add(new Snapshot(child));
        }

        internal static PropPopupLayout? Expand(GComponent pane, GLabel background,
            IReadOnlyList<GObject> footer, IReadOnlyList<GObject> content,
            GTextField? description, GButton? preview, GTextField? name, bool afterFooter) {
            // A generated UI window exposes 'bottom' as GLabel. Do not require
            // that a particular UIObjectFactory extension has already bound it.
            if (pane == null || pane.isDisposed || background == null || background.isDisposed ||
                background.parent != pane || background.width < 260f || background.height < 170f)
                return null;
            PropPopupLayout result = new PropPopupLayout(pane, background);
            try {
                if (result.Apply(footer, content, description, preview, name, afterFooter))
                    return result;
            } catch (Exception ex) {
                SdkLog.Warn("GiftGuide", "扩展原版物品窗口失败：" + ex);
            }
            result.Dispose();
            return null;
        }

        private static float ParentOffset(GObject obj, bool horizontal) {
            float offset = 0f;
            for (GObject? parent = obj.parent; parent != null && !(parent is GRoot); parent = parent.parent)
                offset += horizontal ? parent.x : parent.y;
            return offset;
        }

        private static float Bottom(GObject? item, float current, GComponent pane) =>
            item == null || item.isDisposed || !item.visible || item.parent != pane ||
            (item is GList list && list.numItems == 0)
                ? current : Math.Max(current, item.y + item.height);

        private bool Apply(IReadOnlyList<GObject> footer, IReadOnlyList<GObject> content,
            GTextField? description, GButton? preview, GTextField? name, bool afterFooter) {
            GRoot root = GRoot.inst;
            if (root == null || root.width < 640f || root.height < 440f) return false;
            float x = _originalBackground.X;
            float y = _originalBackground.Y;
            float oldWidth = _originalBackground.Width;
            float oldHeight = _originalBackground.Height;
            float left = ParentOffset(_background, true) + x;
            float top = ParentOffset(_background, false) + y;
            float extraWidth = Math.Max(0f, Math.Min(560f,
                root.width - 2f * Edge - oldWidth));
            // Even when the authored frame is aligned to one edge, use the
            // available space on the other side instead of refusing to expand.
            float newLeft = Math.Max(Edge, Math.Min(left - extraWidth * .5f,
                root.width - Edge - oldWidth - extraWidth));
            float horizontalShift = left - newLeft;
            // Reserve space above and below the popup: never exceed the stage.
            // When little space remains below, move the ENTIRE authored popup
            // (not individual item images/text) upwards by the same amount.
            float extraHeight = Math.Max(0f, Math.Min(DesiredExtraHeight,
                root.height - 2f * Edge - oldHeight));
            float availableBelow = Math.Max(0f, root.height - Edge - (top + oldHeight));
            float lift = Math.Min(Math.Max(0f, top - Edge),
                Math.Max(0f, extraHeight - availableBelow));
            if (extraWidth < 50f && extraHeight < 115f) return false;

            float contentBottom = y + 36f;
            contentBottom = Bottom(description, contentBottom, _pane);
            contentBottom = Bottom(preview, contentBottom, _pane);
            contentBottom = Bottom(name, contentBottom, _pane);
            if (content != null)
                foreach (GObject item in content)
                    contentBottom = Bottom(item, contentBottom, _pane);

            float footerBottom = contentBottom;
            float detailFooterTop = float.MaxValue;
            if (footer != null)
                foreach (GObject item in footer) {
                    footerBottom = Bottom(item, footerBottom, _pane);
                    if (item != null && !item.isDisposed && item.visible && item.parent == _pane)
                        detailFooterTop = Math.Min(detailFooterTop, item.y);
                }
            UICom_PopUpWindow_Bottom? native = _background as UICom_PopUpWindow_Bottom;
            GButton?[] buttons = native == null ? Array.Empty<GButton?>() : new GButton?[] {
                native.btn_Sure, native.btn_Sure_Only, native.btn_Cancel
            };
            float nativeFooterTop = float.MaxValue;
            foreach (GButton? button in buttons) {
                if (button == null || button.isDisposed || !button.visible) continue;
                float buttonY = y + button.y;
                nativeFooterTop = Math.Min(nativeFooterTop, buttonY);
                footerBottom = Math.Max(footerBottom, buttonY + button.height);
            }
            if (nativeFooterTop < detailFooterTop) detailFooterTop = nativeFooterTop;

            // Extend the existing native window frame. Repaint only its large
            // background layers; labels/buttons retain their native sizes.
            _background.SetSize(oldWidth + extraWidth, oldHeight + extraHeight, true);
            _background.SetXY(x - horizontalShift, y - lift);
            foreach (Snapshot snap in _paneChildren) {
                GObject obj = snap.Item;
                if (obj == null || obj.isDisposed) continue;
                // Do not displace the full-screen dimming veil.
                bool overlay = snap.Width >= _pane.width * .85f &&
                    snap.Height >= _pane.height * .85f;
                if (!overlay) obj.SetXY(snap.X,
                    snap.Y - lift + (!afterFooter && IsFooter(obj, footer) ? extraHeight : 0f));
            }
            foreach (Snapshot snap in _backgroundChildren) {
                GObject obj = snap.Item;
                if (obj == null || obj.isDisposed) continue;
                bool backdrop = (obj is GImage || obj is GGraph || obj is GLoader) &&
                    snap.Width >= oldWidth * .65f && snap.Height >= oldHeight * .55f;
                if (backdrop) {
                    obj.SetXY(snap.X, snap.Y);
                    obj.SetSize(snap.Width + extraWidth, snap.Height + extraHeight, true);
                } else if (native != null && ReferenceEquals(obj, native.closeButton)) {
                    obj.SetXY(snap.X + extraWidth, snap.Y);
                } else {
                    bool button = obj is GButton && IsNativeButton(obj, buttons);
                    obj.SetXY(snap.X + horizontalShift,
                        snap.Y + (!afterFooter && button ? extraHeight : 0f));
                }
            }

            // Both lists occupy the REAL extended frame rather than a detached
            // side panel. The purchase list starts after the purchase controls.
            _left = _background.x + Inner;
            _width = _background.width - 2f * Inner;
            _top = (afterFooter ? Math.Max(contentBottom, footerBottom) : contentBottom) + Gap - lift;
            float bottomLimit = y - lift + oldHeight + extraHeight - Inner;
            if (!afterFooter && detailFooterTop < float.MaxValue)
                bottomLimit = Math.Min(bottomLimit, detailFooterTop + extraHeight - lift - Gap);
            _height = Math.Max(0f, bottomLimit - _top);
            return _width >= 270f && _height >= 105f;
        }

        private static bool IsFooter(GObject obj, IReadOnlyList<GObject> footer) {
            if (footer == null) return false;
            foreach (GObject candidate in footer)
                if (ReferenceEquals(obj, candidate)) return true;
            return false;
        }

        private static bool IsNativeButton(GObject obj, GButton?[] buttons) {
            foreach (GButton? candidate in buttons)
                if (ReferenceEquals(obj, candidate)) return true;
            return false;
        }

        internal float GuideLeft => _left;
        internal float GuideTop => _top;
        internal float GuideWidth => _width;
        internal float GuideHeight => _height;

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            if (_pane.isDisposed || _background.isDisposed) return;
            _originalBackground.Restore();
            foreach (Snapshot item in _paneChildren) item.Restore();
            foreach (Snapshot item in _backgroundChildren) item.Restore();
        }
    }
}
