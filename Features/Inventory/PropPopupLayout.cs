using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Extend the original window and its existing GLabel both horizontally
    /// and DOWNWARDS. Preserve the screen positions of authored controls, so
    /// resizing relations do not pull the title, close button or item loaders
    /// to one side. The guide viewport occupies the new lower strip.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private readonly GComponent _pane;
        private readonly GLabel _background;
        private readonly Window? _window;
        private readonly float _originalPaneX, _originalPaneY;
        private readonly float _originalPaneWidth, _originalPaneHeight;
        private readonly float _originalWindowX, _originalWindowY;
        private readonly float _originalWindowWidth, _originalWindowHeight;
        private readonly float _originalX, _originalY, _originalWidth, _originalHeight;
        private readonly Vector2 _backgroundGlobal;
        private readonly List<Snapshot> _paneChildren = new List<Snapshot>();
        private readonly List<Snapshot> _backgroundChildren = new List<Snapshot>();
        private readonly bool _fullScreenPane;
        private bool _disposed;
        private float _guideLeft, _guideTop, _guideWidth, _guideHeight;

        private sealed class Snapshot {
            internal readonly GObject Object;
            internal readonly float X, Y, Width, Height;
            internal readonly Vector2 Global;
            internal Snapshot(GObject obj) {
                Object = obj;
                X = obj.x; Y = obj.y;
                Width = obj.width; Height = obj.height;
                Global = obj.LocalToGlobal(Vector2.zero);
            }
            internal void Restore() {
                if (Object == null || Object.isDisposed) return;
                Object.SetSize(Width, Height);
                Object.SetXY(X, Y);
            }
            internal void KeepScreenPosition(float dy, float dx = 0f) {
                if (Object == null || Object.isDisposed || Object.parent == null) return;
                Vector2 local = Object.parent.GlobalToLocal(Global + new Vector2(dx, dy));
                Object.SetXY(local.x, local.y);
            }
        }

        private PropPopupLayout(GComponent pane, GLabel background) {
            _pane = pane;
            _background = background;
            _window = pane.parent as Window;
            _originalPaneX = pane.x; _originalPaneY = pane.y;
            _originalPaneWidth = pane.width; _originalPaneHeight = pane.height;
            _originalWindowX = _window?.x ?? 0f;
            _originalWindowY = _window?.y ?? 0f;
            _originalWindowWidth = _window?.width ?? 0f;
            _originalWindowHeight = _window?.height ?? 0f;
            _originalX = background.x; _originalY = background.y;
            _originalWidth = background.width; _originalHeight = background.height;
            _backgroundGlobal = background.LocalToGlobal(Vector2.zero);
            _fullScreenPane = GRoot.inst != null &&
                pane.width >= GRoot.inst.width - 80f;
            foreach (GObject child in pane.GetChildren())
                _paneChildren.Add(new Snapshot(child));
            foreach (GObject child in background.GetChildren())
                _backgroundChildren.Add(new Snapshot(child));
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
            if (root == null || root.width < 650f)
                throw new InvalidOperationException("Insufficient UI width");
            float extraWidth = (float)Math.Floor(Math.Min(820f,
                Math.Max(0f, root.width - _originalWidth - 38f)) / 2f) * 2f;
            // The new region belongs to the existing window BELOW its authored
            // content, not a sidecar at the same vertical coordinates.
            float desiredHeight = Math.Min(310f, root.height * .34f);
            float availableBelow = root.height - (_backgroundGlobal.y + _originalHeight) - 12f;
            float moveUp = Math.Min(Math.Max(0f, desiredHeight - availableBelow),
                Math.Max(0f, _backgroundGlobal.y - 12f));
            float extraHeight = Math.Min(desiredHeight, Math.Max(0f, availableBelow + moveUp));
            if (extraHeight < 135f || _originalWidth + extraWidth < 440f)
                throw new InvalidOperationException("Insufficient space for guide");
            float half = extraWidth * .5f;
            // Resize the real native content, and not just a drawn GGraph.
            // Full-screen content panes cannot grow wider than the screen.
            if (!_fullScreenPane) {
                if (_window != null) {
                    _window.SetSize(_originalWindowWidth + extraWidth,
                        _originalWindowHeight + extraHeight);
                    _window.SetXY(_originalWindowX - half, _originalWindowY - moveUp);
                } else {
                    _pane.SetSize(_originalPaneWidth + extraWidth,
                        _originalPaneHeight + extraHeight);
                    _pane.SetXY(_originalPaneX - half, _originalPaneY - moveUp);
                }
            }
            _background.SetSize(_originalWidth + extraWidth, _originalHeight + extraHeight);
            // Preserve the old title/image/button locations, despite changes
            // to the original FairyGUI relations during SetSize.
            foreach (Snapshot snapshot in _paneChildren) {
                if (ReferenceEquals(snapshot.Object, _background) ||
                    snapshot.Object.isDisposed || snapshot.Object.name == "mohu") continue;
                if (snapshot.Width > _originalPaneWidth * .9f &&
                    snapshot.Height > _originalPaneHeight * .85f) continue;
                snapshot.KeepScreenPosition(-moveUp);
            }
            Vector2 origin = _pane.GlobalToLocal(_backgroundGlobal +
                new Vector2(-half, -moveUp));
            _background.SetXY(origin.x, origin.y);
            foreach (Snapshot snapshot in _backgroundChildren) {
                if (snapshot.Object.isDisposed) continue;
                bool surface = snapshot.Width >= _originalWidth * .80f &&
                    snapshot.Height >= _originalHeight * .64f &&
                    (snapshot.Object is GGraph || snapshot.Object is GImage);
                if (surface) {
                    snapshot.Object.SetSize(snapshot.Width + extraWidth,
                        snapshot.Height + extraHeight);
                    snapshot.KeepScreenPosition(-moveUp, -half);
                } else {
                    // The native title remains visually centered. The close
                    // button follows the new right edge of the same window.
                    string name = snapshot.Object.name ?? "";
                    float closeOffset = name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0
                        ? half : 0f;
                    snapshot.KeepScreenPosition(-moveUp, closeOffset);
                }
            }
            Vector2 guide = _pane.GlobalToLocal(_backgroundGlobal +
                new Vector2(-half + 16f, _originalHeight + 8f - moveUp));
            _guideLeft = guide.x;
            _guideTop = guide.y;
            _guideWidth = _originalWidth + extraWidth - 32f;
            _guideHeight = extraHeight - 22f;
        }

        internal float GuideLeft => _guideLeft;
        internal float GuideTop => _guideTop;
        internal float GuideWidth => _guideWidth;
        internal float GuideHeight => _guideHeight;

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            if (_background.isDisposed) return;
            _background.SetSize(_originalWidth, _originalHeight);
            _background.SetXY(_originalX, _originalY);
            foreach (Snapshot child in _backgroundChildren) child.Restore();
            if (_window != null && !_window.isDisposed) {
                _window.SetSize(_originalWindowWidth, _originalWindowHeight);
                _window.SetXY(_originalWindowX, _originalWindowY);
            } else if (!_fullScreenPane && !_pane.isDisposed) {
                _pane.SetSize(_originalPaneWidth, _originalPaneHeight);
                _pane.SetXY(_originalPaneX, _originalPaneY);
            }
            foreach (Snapshot child in _paneChildren) child.Restore();
        }
    }
}
