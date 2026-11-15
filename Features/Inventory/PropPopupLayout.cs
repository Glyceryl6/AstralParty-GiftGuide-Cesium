using System;
using System.Collections.Generic;
using FairyGUI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Expand the popup's REAL bottom GLabel horizontally. Unlike a separately
    /// drawn side panel, its native border, hit region and children now have
    /// the enlarged width. Restore every changed native property on closing.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private readonly GComponent _pane;
        private readonly GLabel _background;
        private readonly Window? _window;
        private readonly float _originalPaneWidth, _originalPaneHeight;
        private readonly float _originalWindowX, _originalWindowY;
        private readonly float _originalWindowWidth, _originalWindowHeight;
        private readonly float _originalX, _originalY, _originalWidth, _originalHeight;
        private readonly List<OriginalObject> _paneChildren = new List<OriginalObject>();
        private readonly List<OriginalObject> _backgroundChildren = new List<OriginalObject>();
        private bool _resizedPane;
        private bool _disposed;
        private float _guideLeft, _guideWidth;

        private sealed class OriginalObject {
            internal readonly GObject Object;
            internal readonly float X, Y, Width, Height;
            internal OriginalObject(GObject obj) {
                Object = obj; X = obj.x; Y = obj.y;
                Width = obj.width; Height = obj.height;
            }
            internal void Restore() {
                if (Object == null || Object.isDisposed) return;
                Object.SetSize(Width, Height);
                Object.SetXY(X, Y);
            }
        }

        private PropPopupLayout(GComponent pane, GLabel background) {
            _pane = pane;
            _background = background;
            _window = pane.parent as Window;
            _originalPaneWidth = pane.width;
            _originalPaneHeight = pane.height;
            _originalWindowX = _window?.x ?? 0f;
            _originalWindowY = _window?.y ?? 0f;
            _originalWindowWidth = _window?.width ?? 0f;
            _originalWindowHeight = _window?.height ?? 0f;
            _originalX = background.x;
            _originalY = background.y;
            _originalWidth = background.width;
            _originalHeight = background.height;
            foreach (GObject child in pane.GetChildren())
                _paneChildren.Add(new OriginalObject(child));
            foreach (GObject child in background.GetChildren())
                _backgroundChildren.Add(new OriginalObject(child));
        }

        internal static PropPopupLayout? Expand(GComponent pane, GLabel background,
            IReadOnlyList<GObject> footer) {
            if (pane == null || pane.isDisposed || background == null ||
                background.isDisposed || background.width < 250f || background.height < 200f)
                return null;
            var layout = new PropPopupLayout(pane, background);
            try { layout.Apply(); return layout; }
            catch { layout.Dispose(); return null; }
        }

        private void Apply() {
            GRoot root = GRoot.inst;
            if (root == null || root.width < 650f) throw new InvalidOperationException("界面宽度不足");
            // Keep the original top/bottom; use only the horizontal free space.
            bool fillScreen = _originalPaneWidth >= root.width - 80f;
            float nativeWidth = fillScreen ? _originalWidth :
                Math.Max(_originalPaneWidth, _originalWindowWidth);
            float extra = (float)Math.Floor(Math.Min(1160f,
                Math.Max(0f, root.width - nativeWidth - 38f)) / 2f) * 2f;
            if (extra < 340f) throw new InvalidOperationException("窗口宽度不足以放置原版商店卡片");
            float half = extra * .5f;
            // Some popup content panes already cover the full screen, whereas
            // others are window-sized. Only the latter require root resizing.
            _resizedPane = !fillScreen;
            if (_resizedPane) {
                if (_window != null) {
                    _window.SetSize(_originalWindowWidth + extra, _originalWindowHeight);
                    _window.CenterOn(root, false);
                } else _pane.SetSize(_originalPaneWidth + extra, _originalPaneHeight);
                foreach (OriginalObject child in _paneChildren) {
                    if (ReferenceEquals(child.Object, _background) ||
                        child.Object.isDisposed || child.Object.name == "mohu") continue;
                    // Full-screen blur covers are not part of the central card.
                    if (child.Width > _originalPaneWidth * .9f &&
                        child.Height > _originalPaneHeight * .85f) continue;
                    child.Object.SetXY(child.X + half, child.Y);
                }
                _background.SetXY(_originalX, _originalY);
                _guideLeft = _originalX + 14f;
            } else {
                _background.SetXY(_originalX - half, _originalY);
                _guideLeft = _originalX - half + 14f;
            }
            // The native background itself now owns the expanded area.
            _background.SetSize(_originalWidth + extra, _originalHeight);
            // Restore the central glyphs/icons from the original GLabel. Its
            // full-size background surface may stretch; smaller contents must
            // retain their original onscreen coordinates after centering.
            foreach (OriginalObject child in _backgroundChildren) {
                if (child.Object.isDisposed) continue;
                bool surface = child.Width >= _originalWidth * .82f &&
                    child.Height >= _originalHeight * .65f &&
                    (child.Object is GGraph || child.Object is GImage);
                if (surface) {
                    child.Object.SetSize(child.Width + extra, child.Height);
                    child.Object.SetXY(child.X, child.Y);
                } else child.Object.SetXY(child.X + half, child.Y);
            }
            // All three guide sections share ONE scroll area in the left
            // half of the newly extended *native window* (not a new panel).
            _guideWidth = half - 28f;
        }

        internal float GuideLeft => _guideLeft;
        internal float GuideTop => _originalY + 18f;
        internal float GuideWidth => _guideWidth;
        internal float GuideHeight => Math.Max(0f, _originalHeight - 36f);

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            if (_background == null || _background.isDisposed) return;
            // Undo in reverse order to avoid keeping a transient relation state
            // in a pooled native Purchase/PropDetail window.
            _background.SetSize(_originalWidth, _originalHeight);
            _background.SetXY(_originalX, _originalY);
            foreach (OriginalObject child in _backgroundChildren) child.Restore();
            if (_resizedPane) {
                if (_window != null && !_window.isDisposed) {
                    _window.SetSize(_originalWindowWidth, _originalWindowHeight);
                    _window.SetXY(_originalWindowX, _originalWindowY);
                } else if (!_pane.isDisposed) _pane.SetSize(_originalPaneWidth, _originalPaneHeight);
                foreach (OriginalObject child in _paneChildren) child.Restore();
            }
        }
    }
}
