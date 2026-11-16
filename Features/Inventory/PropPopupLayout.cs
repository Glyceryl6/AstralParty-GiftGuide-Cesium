using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Expand only the authored Com_PopUpWindow_Bottom frame. Both item windows
    /// have a full-screen content pane; resizing that pane (or its Window)
    /// moves the original item art/description through FairyGUI relations.
    /// The old upper edge and every native pane child remain in place.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private const float EdgeMargin = 12f;
        private const float InsideMargin = 14f;
        private const float FooterReserve = 65f;
        private readonly GComponent _pane;
        private readonly UICom_PopUpWindow_Bottom _background;
        private readonly Snapshot _backgroundSnapshot;
        private readonly List<Snapshot> _paneChildren = new List<Snapshot>();
        private readonly List<Snapshot> _backgroundChildren = new List<Snapshot>();
        private bool _disposed;
        private float _guideLeft, _guideTop, _guideWidth, _guideHeight;

        private sealed class Snapshot {
            internal readonly GObject Object;
            internal readonly float X, Y, Width, Height;
            internal Snapshot(GObject obj) {
                Object = obj;
                X = obj.x; Y = obj.y; Width = obj.width; Height = obj.height;
            }
            internal void Restore() {
                if (Object == null || Object.isDisposed) return;
                Object.SetSize(Width, Height, true);
                Object.SetXY(X, Y);
            }
        }

        private PropPopupLayout(GComponent pane, UICom_PopUpWindow_Bottom background) {
            _pane = pane;
            _background = background;
            _backgroundSnapshot = new Snapshot(background);
            foreach (GObject child in pane.GetChildren())
                if (child != null && !child.isDisposed && !ReferenceEquals(child, background))
                    _paneChildren.Add(new Snapshot(child));
            foreach (GObject child in background.GetChildren())
                if (child != null && !child.isDisposed) _backgroundChildren.Add(new Snapshot(child));
        }

        internal static PropPopupLayout? Expand(GComponent pane, GLabel background,
            IReadOnlyList<GObject> footer, GTextField? description, GButton? preview, GTextField? name) {
            if (pane == null || pane.isDisposed ||
                !(background is UICom_PopUpWindow_Bottom bottom) || bottom.isDisposed ||
                bottom.parent != pane || bottom.width < 260f || bottom.height < 170f) return null;
            var result = new PropPopupLayout(pane, bottom);
            try {
                if (result.Apply(footer, description, preview, name)) return result;
            } catch (Exception ex) {
                SdkLog.Warn("GiftGuide", "扩展物品窗口失败：" + ex);
            }
            result.Dispose();
            return null;
        }

        // These popups generally use an unscaled, full-screen content pane.
        // Adding parent positions is safe even while displayObject is null;
        // LocalToGlobal is not safe until FairyGUI has built the display tree.
        private static float ParentOffset(GObject obj, bool horizontal) {
            float sum = 0f;
            for (GObject? parent = obj.parent; parent != null && !(parent is GRoot); parent = parent.parent)
                sum += horizontal ? parent.x : parent.y;
            return sum;
        }

        private bool Apply(IReadOnlyList<GObject> footer, GTextField? description,
            GButton? preview, GTextField? name) {
            GRoot root = GRoot.inst;
            if (root == null || root.width < 640f || root.height < 440f) return false;
            float oldWidth = _backgroundSnapshot.Width;
            float oldHeight = _backgroundSnapshot.Height;
            float x = _backgroundSnapshot.X;
            float y = _backgroundSnapshot.Y;
            float absoluteLeft = ParentOffset(_background, true) + x;
            float absoluteTop = ParentOffset(_background, false) + y;
            float halfExtra = Math.Max(0f, Math.Min(285f,
                Math.Min(absoluteLeft - EdgeMargin,
                    root.width - absoluteLeft - oldWidth - EdgeMargin)));
            float extraWidth = halfExtra * 2f;
            float extraHeight = Math.Max(0f, Math.Min(315f,
                root.height - EdgeMargin - absoluteTop - oldHeight));
            if (extraWidth < 70f && extraHeight < 45f) return false;

            // The guide starts immediately below the actual original content,
            // not at the end of the (often mostly empty) authored background.
            float contentBottom = y + 32f;
            if (description != null && !description.isDisposed)
                contentBottom = Math.Max(contentBottom, description.y + description.height);
            if (preview != null && !preview.isDisposed)
                contentBottom = Math.Max(contentBottom, preview.y + preview.height);
            if (name != null && !name.isDisposed)
                contentBottom = Math.Max(contentBottom, name.y + name.height);
            if (footer != null) {
                foreach (GObject item in footer) {
                    if (item != null && !item.isDisposed && item.visible && item.parent == _pane)
                        contentBottom = Math.Max(contentBottom, item.y + item.height);
                }
            }

            // Do not alter the full-screen pane or window. Shift the expanded
            // frame left by half the width increase so its center stays fixed;
            // its top stays fixed, so no new blank strip appears above the art.
            _background.SetSize(oldWidth + extraWidth, oldHeight + extraHeight, true);
            _background.SetXY(x - halfExtra, y);
            foreach (Snapshot child in _paneChildren) child.Restore();
            foreach (Snapshot child in _backgroundChildren) {
                child.Restore();
                GObject obj = child.Object;
                if (obj == null || obj.isDisposed) continue;
                bool backdrop = (obj is GGraph || obj is GImage) &&
                    child.Width >= oldWidth * .65f && child.Height >= oldHeight * .55f;
                if (backdrop) {
                    obj.SetSize(child.Width + extraWidth, child.Height + extraHeight, true);
                    continue;
                }
                if (ReferenceEquals(obj, _background.closeButton)) {
                    obj.SetXY(child.X + extraWidth, child.Y);
                    continue;
                }
                bool action = ReferenceEquals(obj, _background.btn_Sure) ||
                    ReferenceEquals(obj, _background.btn_Sure_Only) ||
                    ReferenceEquals(obj, _background.btn_Cancel);
                obj.SetXY(child.X + halfExtra, child.Y + (action ? extraHeight : 0f));
            }

            _guideLeft = _background.x + InsideMargin;
            _guideWidth = _background.width - InsideMargin * 2f;
            _guideTop = Math.Max(y + 32f, contentBottom + 8f);
            float bottom = y + oldHeight + extraHeight - FooterReserve;
            _guideHeight = Math.Max(0f, bottom - _guideTop);
            return _guideWidth >= 260f && _guideHeight >= 95f;
        }

        internal float GuideLeft => _guideLeft;
        internal float GuideTop => _guideTop;
        internal float GuideWidth => _guideWidth;
        internal float GuideHeight => _guideHeight;

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            if (!_pane.isDisposed && !_background.isDisposed) {
                _backgroundSnapshot.Restore();
                foreach (Snapshot child in _paneChildren) child.Restore();
                foreach (Snapshot child in _backgroundChildren) child.Restore();
            }
        }
    }
}
