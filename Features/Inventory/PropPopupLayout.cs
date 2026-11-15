using System;
using System.Collections.Generic;
using FairyGUI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// A width-only extension. The original popup title, loader, buttons and
    /// all native pane children retain their authored coordinates and sizes.
    /// The added side rails belong to the background, not the native controls.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private readonly GComponent _pane;
        private readonly GLabel _background;
        private readonly float _originalX, _originalY, _originalW, _originalH;
        private readonly List<GObject> _footers = new List<GObject>();
        private bool _disposed;

        private PropPopupLayout(GComponent pane, GLabel background) {
            _pane = pane;
            _background = background;
            _originalX = background.x;
            _originalY = background.y;
            _originalW = background.width;
            _originalH = background.height;
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
            for (int i = 0; i < footer.Count; i++)
                if (footer[i] != null) _footers.Add(footer[i]);

            // Increase ONLY width, symmetrically around the native frame.
            // Center the enlarged frame inside its parent if needed, without
            // translating the game's icon loaders, labels or footer buttons.
            float newW = Math.Min(_originalW + 660f,
                Math.Max(_originalW, _pane.width - 24f));
            float x = newW <= _originalW + 1f ? _originalX :
                Math.Max(8f, Math.Min(_pane.width - newW - 8f,
                    _originalX - (newW - _originalW) * .5f));
            _background.SetSize(newW, _originalH, true);
            _background.SetXY(x, _originalY);
        }

        internal float Left => _background.x + 12f;
        internal float Right => _background.x + _background.width - 12f;
        internal float Top => _originalY + 26f;
        internal float Bottom => _originalY + _originalH - 16f;
        internal float RightRailLeft => _originalX + _originalW + 10f;
        internal float LeftRailRight => _originalX - 10f;

        internal float FooterTop(float top) {
            float bottom = Bottom;
            foreach (GObject item in _footers) {
                if (item == null || item.isDisposed || !item.visible || !item.onStage ||
                    item.parent != _pane) continue;
                if (item.y > top + 10f && item.y < bottom)
                    bottom = Math.Min(bottom, item.y - 8f);
            }
            return bottom;
        }

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            try {
                if (!_background.isDisposed && !_pane.isDisposed) {
                    _background.SetSize(_originalW, _originalH, true);
                    _background.SetXY(_originalX, _originalY);
                }
            } catch { /* A scene may already have disposed the native dialog. */ }
        }
    }
}
