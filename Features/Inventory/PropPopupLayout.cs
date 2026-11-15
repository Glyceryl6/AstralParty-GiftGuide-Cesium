using System;
using System.Collections.Generic;
using FairyGUI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Keep the native popup's item labels, pictures and counters in their
    /// authored relative positions. Enlarge the frame sideways and BELOW the
    /// original content; never scale/re-space individual native components.
    /// Both the frame and every pane-level change are reversible.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private readonly GComponent _pane;
        private readonly GLabel _background;
        private readonly float _originalX, _originalY, _originalW, _originalH;
        private readonly List<OriginalLocation> _paneItems = new List<OriginalLocation>();
        private readonly List<GObject> _footers = new List<GObject>();
        private bool _disposed;

        private sealed class OriginalLocation {
            internal readonly GObject Object;
            internal readonly float X, Y, Width, Height;
            internal readonly bool IsBottomRow;
            internal OriginalLocation(GObject obj, bool isBottomRow) {
                Object = obj;
                X = obj.x; Y = obj.y;
                Width = obj.width; Height = obj.height;
                IsBottomRow = isBottomRow;
            }
            internal void Restore() {
                if (Object.isDisposed) return;
                Object.SetSize(Width, Height);
                Object.SetXY(X, Y);
            }
        }

        private PropPopupLayout(GComponent pane, GLabel background) {
            _pane = pane;
            _background = background;
            _originalX = background.x; _originalY = background.y;
            _originalW = background.width; _originalH = background.height;
        }

        internal static PropPopupLayout? Expand(GComponent pane, GLabel background,
            IReadOnlyList<GObject> footer) {
            if (pane == null || background == null || background.isDisposed ||
                background.width < 280f || background.height < 210f) return null;
            var layout = new PropPopupLayout(pane, background);
            try { layout.Apply(footer); return layout; }
            catch { layout.Dispose(); return null; }
        }

        private static float Clamp(float value, float low, float high) =>
            Math.Max(low, Math.Min(high, value));

        private void Apply(IReadOnlyList<GObject> footer) {
            float newW = Math.Min(_originalW + 300f, Math.Max(_originalW, _pane.width - 36f));
            float newH = Math.Min(_originalH + 240f, Math.Max(_originalH, _pane.height - 36f));
            float dw = Math.Max(0f, newW - _originalW);
            float dh = Math.Max(0f, newH - _originalH);
            float x = Clamp((_pane.width - newW) * .5f, 8f, Math.Max(8f, _pane.width - newW - 8f));
            float y = Clamp((_pane.height - newH) * .5f, 8f, Math.Max(8f, _pane.height - newH - 8f));

            // When the native frame recenters, all free-standing content moves
            // together as ONE block. Changing each element by a different ratio
            // caused labels, icons and their adjacent controls to separate.
            float moveX = x - _originalX + dw * .5f;
            float moveY = y - _originalY;
            for (int i = 0; i < footer.Count; i++) {
                if (footer[i] != null) _footers.Add(footer[i]);
            }
            // The last part of the original dialog is a *row*: move its text,
            // number labels, currency images and buttons together to the new
            // bottom, including fields not explicitly listed as footers.
            float footerBoundary = _originalY + _originalH - 118f;
            for (int i = 0; i < _pane.numChildren; i++) {
                GObject obj = _pane.GetChildAt(i);
                if (obj == _background || obj.isDisposed || obj.name == "mohu" ||
                    IsFullScreenBackdrop(obj)) continue;
                float cx = obj.x + obj.width * .5f;
                float cy = obj.y + obj.height * .5f;
                bool belongs = cx >= _originalX - 16f && cx <= _originalX + _originalW + 16f &&
                    cy >= _originalY - 16f && cy <= _originalY + _originalH + 16f;
                bool explicitFooter = _footers.Contains(obj);
                if (!belongs && !explicitFooter) continue;
                bool lower = explicitFooter || cy >= footerBoundary;
                if (!lower) {
                    // Text and icon widgets next to a footer button often
                    // have no explicit reference in the generated window.
                    // They still belong to the SAME row and must move with it.
                    foreach (GObject footerItem in _footers) {
                        if (footerItem == null || footerItem.isDisposed ||
                            footerItem.parent != _pane) continue;
                        float fy = footerItem.y + footerItem.height * .5f;
                        if (Math.Abs(cy - fy) <= 26f) { lower = true; break; }
                    }
                }
                _paneItems.Add(new OriginalLocation(obj, lower));
            }

            // Do not reposition or resize the GLabel's own children. Its skin,
            // background images and any internal relations must remain native.
            _background.SetSize(newW, newH);
            _background.SetXY(x, y);
            foreach (OriginalLocation item in _paneItems) {
                if (item.Object.isDisposed) continue;
                item.Object.SetXY(item.X + moveX,
                    item.Y + moveY + (item.IsBottomRow ? dh : 0f));
            }
        }

        private bool IsFullScreenBackdrop(GObject obj) =>
            obj.width >= _pane.width * .88f && obj.height >= _pane.height * .88f;

        internal float Left => _background.x + 18f;
        internal float Right => _background.x + _background.width - 18f;
        internal float Bottom => _background.y + _background.height - 24f;

        internal float FooterTop(float top) {
            float bottom = Bottom;
            foreach (OriginalLocation item in _paneItems) {
                if (!item.IsBottomRow || item.Object.isDisposed ||
                    !item.Object.visible || !item.Object.onStage) continue;
                if (item.Object.y > top + 30f && item.Object.y < bottom)
                    bottom = Math.Min(bottom, item.Object.y - 12f);
            }
            return bottom;
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            try {
                if (!_background.isDisposed && !_pane.isDisposed) {
                    _background.SetSize(_originalW, _originalH);
                    _background.SetXY(_originalX, _originalY);
                }
                foreach (OriginalLocation item in _paneItems) item.Restore();
            } catch { /* A scene may already have disposed the native dialog. */ }
        }
    }
}
