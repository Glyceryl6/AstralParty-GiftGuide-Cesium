using System;
using System.Collections.Generic;
using FairyGUI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// The content pane is usually a full-screen hit area; only its 'bottom'
    /// label is the visible dialog. Widen its left/right edges, grow its bottom
    /// edge and THEN recenter the finished dialog. Move its content with the
    /// frame, without ever resizing the screen-sized blur or dimming layer.
    /// Everything touched here is restored when the item/window changes.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private readonly GComponent _pane;
        private readonly GLabel _background;
        private readonly float _originalX, _originalY, _originalW, _originalH;
        private readonly List<OriginalLocation> _children = new List<OriginalLocation>();
        private readonly List<OriginalLocation> _paneItems = new List<OriginalLocation>();
        private readonly List<GObject> _footers = new List<GObject>();
        private bool _disposed;

        private sealed class OriginalLocation {
            internal readonly GObject Object;
            internal readonly float X, Y, Width, Height;
            internal OriginalLocation(GObject obj) {
                Object = obj; X = obj.x; Y = obj.y;
                Width = obj.width; Height = obj.height;
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

        private static float Clamp(float value, float low, float high) =>
            Math.Max(low, Math.Min(high, value));

        private void Apply(IReadOnlyList<GObject> footer) {
            float newW = Math.Min(_originalW + 300f, Math.Max(_originalW, _pane.width - 36f));
            float newH = Math.Min(_originalH + 240f, Math.Max(_originalH, _pane.height - 36f));
            float dw = Math.Max(0f, newW - _originalW);
            float dh = Math.Max(0f, newH - _originalH);

            // Expand horizontally around the old center; add height below the
            // old bottom, then recenter the resulting larger frame on screen.
            float x = Clamp((_pane.width - newW) * .5f, 8f, Math.Max(8f, _pane.width - newW - 8f));
            float y = Clamp((_pane.height - newH) * .5f, 8f, Math.Max(8f, _pane.height - newH - 8f));
            float globalDx = x - _originalX;
            float globalDy = y - _originalY;

            foreach (GObject item in footer) if (item != null) _footers.Add(item);
            // Snapshot all native content which intersects the old dialog,
            // except the full-screen backdrop and the frame itself. Some
            // buttons are children of the pane rather than of 'bottom'.
            for (int i = 0; i < _pane.numChildren; i++) {
                GObject obj = _pane.GetChildAt(i);
                if (obj == _background || obj.isDisposed ||
                    obj.name == "mohu" || IsFullScreenBackdrop(obj)) continue;
                float cx = obj.x + obj.width * .5f;
                float cy = obj.y + obj.height * .5f;
                bool belongs = cx >= _originalX - 14f &&
                    cx <= _originalX + _originalW + 14f &&
                    cy >= _originalY - 14f && cy <= _originalY + _originalH + 14f;
                if (!belongs) {
                    for (int j = 0; j < footer.Count; j++)
                        if (ReferenceEquals(obj, footer[j])) { belongs = true; break; }
                }
                if (belongs) _paneItems.Add(new OriginalLocation(obj));
            }

            _background.SetSize(newW, newH);
            _background.SetXY(x, y);
            foreach (OriginalLocation item in _children) {
                GObject obj = item.Object;
                if (obj.isDisposed) continue;
                float relativeX = Clamp((item.X + item.Width * .5f) / _originalW, 0f, 1f);
                float shiftX = dw * (relativeX - .5f);
                float shiftY = item.Y > _originalH * .70f ? dh : 0f;
                obj.SetXY(item.X + shiftX, item.Y + shiftY);
            }
            foreach (OriginalLocation item in _paneItems) {
                GObject obj = item.Object;
                if (obj.isDisposed) continue;
                float relativeX = Clamp((item.X + item.Width * .5f - _originalX) /
                    _originalW, 0f, 1f);
                float shiftX = dw * (relativeX - .5f);
                float shiftY = item.Y > _originalY + _originalH * .70f ? dh : 0f;
                obj.SetXY(item.X + globalDx + shiftX, item.Y + globalDy + shiftY);
            }
        }

        private bool IsFullScreenBackdrop(GObject obj) =>
            obj.width >= _pane.width * .88f && obj.height >= _pane.height * .88f;

        internal float Left => _background.x + 22f;
        internal float Right => _background.x + _background.width - 22f;
        internal float Bottom => _background.y + _background.height - 28f;

        internal float FooterTop(float top) {
            float bottom = Bottom;
            foreach (GObject obj in _footers) {
                if (obj.isDisposed || !obj.visible || !obj.onStage ||
                    obj.parent != _pane) continue;
                if (obj.y > top + 40f && obj.y < bottom)
                    bottom = Math.Min(bottom, obj.y - 10f);
            }
            // Close / action buttons may be children of the frame. Use their
            // actual pane-space Y, never their frame-local Y.
            foreach (OriginalLocation item in _children) {
                GObject obj = item.Object;
                if (!(obj is GButton) || obj.isDisposed || !obj.visible || !obj.onStage) continue;
                float y = _background.y + obj.y;
                if (y > top + 55f && y < bottom &&
                    item.Y >= _originalH * .58f) bottom = Math.Min(bottom, y - 10f);
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
                foreach (OriginalLocation item in _children) item.Restore();
                foreach (OriginalLocation item in _paneItems) item.Restore();
            } catch { /* The scene may already have destroyed the native dialog. */ }
        }
    }
}
