using System;
using System.Collections.Generic;
using FairyGUI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// The item-window content pane usually occupies the FULL screen; 'bottom'
    /// is the actual native dialog. Enlarge that native frame conservatively
    /// and restore all coordinates when the item changes or the window closes.
    /// Never resize the full-screen blur/interaction layer.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private readonly GComponent _pane;
        private readonly GLabel _background;
        private readonly float _originalX, _originalY, _originalW, _originalH;
        private readonly List<OriginalLocation> _children = new List<OriginalLocation>();
        private readonly List<OriginalLocation> _footers = new List<OriginalLocation>();
        private bool _disposed;

        private sealed class OriginalLocation {
            internal readonly GObject Object;
            internal readonly float X, Y;
            internal OriginalLocation(GObject obj) { Object = obj; X = obj.x; Y = obj.y; }
        }

        private PropPopupLayout(GComponent pane, GLabel background) {
            _pane = pane;
            _background = background;
            _originalX = background.x;
            _originalY = background.y;
            _originalW = background.width;
            _originalH = background.height;
            for (int i = 0; i < background.numChildren; i++)
                _children.Add(new OriginalLocation(background.GetChildAt(i)));
        }

        internal static PropPopupLayout? Expand(GComponent pane, GLabel background,
            IReadOnlyList<GObject> footer) {
            if (pane == null || background == null || background.isDisposed ||
                background.width < 280f || background.height < 210f) return null;
            var layout = new PropPopupLayout(pane, background);
            try { layout.Apply(footer); return layout; }
            catch { layout.Dispose(); return null; }
        }

        private void Apply(IReadOnlyList<GObject> footer) {
            // Bound both dimensions to the real screen. Widening a list is
            // never allowed to make it wider than the dialog behind it.
            float newW = Math.Min(_originalW + 220f, Math.Max(_originalW, _pane.width - 28f));
            float newH = Math.Min(_originalH + 200f, Math.Max(_originalH, _pane.height - 28f));
            float dw = Math.Max(0f, newW - _originalW);
            float dh = Math.Max(0f, newH - _originalH);
            float x = Math.Max(8f, Math.Min(_pane.width - newW - 8f, _originalX - dw * .5f));
            float y = Math.Max(8f, Math.Min(_pane.height - newH - 8f, _originalY - dh * .5f));
            _background.SetSize(newW, newH);
            _background.SetXY(x, y);

            // FairyGUI relations may already follow a resized frame. Only
            // move children not moved by those relations; preserve close and
            // purchase buttons, including their hit areas.
            foreach (OriginalLocation child in _children) {
                GObject obj = child.Object;
                if (obj.isDisposed || Math.Abs(obj.x - child.X) > .5f ||
                    Math.Abs(obj.y - child.Y) > .5f) continue;
                if (child.Y > _originalH * .69f) {
                    obj.SetXY(child.X + dw * .5f, child.Y + dh);
                } else if (child.X > _originalW * .72f && child.Y < _originalH * .32f) {
                    obj.SetXY(child.X + dw, child.Y);
                }
            }

            foreach (GObject obj in footer) {
                if (obj == null || obj.isDisposed || obj.parent != _pane) continue;
                _footers.Add(new OriginalLocation(obj));
                if (obj.y >= _originalY + _originalH * .62f) obj.SetXY(obj.x, obj.y + dh * .8f);
            }
        }

        internal float Left => _background.x + 16f;
        internal float Right => _background.x + _background.width - 16f;
        internal float Bottom => _background.y + _background.height - 48f;

        internal float FooterTop(float top) {
            float bottom = Bottom;
            foreach (OriginalLocation item in _footers) {
                GObject obj = item.Object;
                if (obj.isDisposed || !obj.visible) continue;
                if (obj.y > top + 36f && obj.y < bottom) bottom = obj.y - 6f;
            }
            // Modal action buttons are children of the authored 'bottom'.
            foreach (OriginalLocation item in _children) {
                GObject obj = item.Object;
                if (!(obj is GButton) || obj.isDisposed || !obj.visible) continue;
                float y = _background.y + obj.y;
                if (y > top + 42f && y < bottom) bottom = y - 6f;
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
                foreach (OriginalLocation child in _children)
                    if (!child.Object.isDisposed) child.Object.SetXY(child.X, child.Y);
                foreach (OriginalLocation footer in _footers)
                    if (!footer.Object.isDisposed) footer.Object.SetXY(footer.X, footer.Y);
            } catch { /* A scene transition may have destroyed the native UI. */ }
        }
    }
}
