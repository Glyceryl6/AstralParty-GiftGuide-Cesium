using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Width-only, reversible popup extension. Do not resize its native GLabel
    /// (bottom): its internal loader_icon and _contentPane have FairyGUI size
    /// relations and would be translated by SetSize, even if other popup
    /// children stay at their original positions.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private readonly GComponent _pane;
        private readonly GLabel _nativeBackground;
        private readonly float _originalX, _originalY, _originalW, _originalH;
        private readonly List<GObject> _footers = new List<GObject>();
        private GGraph? _extension;
        private float _left;
        private float _right;
        private bool _disposed;

        private PropPopupLayout(GComponent pane, GLabel background) {
            _pane = pane;
            _nativeBackground = background;
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
            float newWidth = Math.Min(_originalW + 1080f,
                Math.Max(_originalW, _pane.width - 24f));
            if (newWidth <= _originalW + 16f)
                throw new InvalidOperationException("屏幕宽度不足以展开角色指南");
            _left = Math.Max(8f, Math.Min(_pane.width - newWidth - 8f,
                _originalX - (newWidth - _originalW) * .5f));
            _right = _left + newWidth;

            // A separate background lies behind the native window artwork.
            // This never triggers internal relations of bottom's icon, graph,
            // title or any purchase controls, and is fully removed on close.
            _extension = new GGraph {
                name = "GiftGuide.SideExtension", touchable = false
            };
            _extension.SetSize(newWidth, _originalH);
            _extension.DrawRect(newWidth, _originalH, 2,
                new Color(.52f, .62f, .75f, .95f),
                new Color(.075f, .10f, .15f, .92f));
            _extension.SetXY(_left, _originalY);
            int index = _pane.GetChildIndex(_nativeBackground);
            _pane.AddChildAt(_extension, Math.Max(0, index));
        }

        internal float Left => _left + 12f;
        internal float Right => _right - 12f;
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
            if (_extension != null && !_extension.isDisposed) {
                _extension.RemoveFromParent();
                _extension.Dispose();
            }
            _extension = null;
        }
    }
}
