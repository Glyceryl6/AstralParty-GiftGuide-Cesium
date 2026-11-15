using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Expands the native item window in its own parent coordinate system.
    /// Do not use LocalToGlobal here: some template children are not attached
    /// to a FairyGUI DisplayObject until after the window's first layout pass.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private readonly GComponent _pane;
        private readonly GLabel _background;
        private readonly Window? _window;
        private readonly float _paneX, _paneY, _paneWidth, _paneHeight;
        private readonly float _windowX, _windowY, _windowWidth, _windowHeight;
        private readonly float _backgroundX, _backgroundY, _backgroundWidth, _backgroundHeight;
        private readonly List<Snapshot> _paneChildren = new List<Snapshot>();
        private readonly List<Snapshot> _backgroundChildren = new List<Snapshot>();
        private float _guideLeft, _guideTop, _guideWidth, _guideHeight;
        private bool _disposed;

        private sealed class Snapshot {
            internal readonly GObject Object;
            internal readonly float X, Y, Width, Height;

            internal Snapshot(GObject obj) {
                Object = obj;
                X = obj.x;
                Y = obj.y;
                Width = obj.width;
                Height = obj.height;
            }

            internal void Restore() {
                if (Object == null || Object.isDisposed) return;
                Object.SetSize(Width, Height);
                Object.SetXY(X, Y);
            }

            internal void Shift(float dx, float dy = 0f) {
                if (Object == null || Object.isDisposed) return;
                // x/y are local to the *same* parent before and after resize.
                // This remains safe even when the child has no displayObject.
                Object.SetXY(X + dx, Y + dy);
            }
        }

        private PropPopupLayout(GComponent pane, GLabel background) {
            _pane = pane;
            _background = background;
            _window = pane.parent as Window;
            _paneX = pane.x;
            _paneY = pane.y;
            _paneWidth = pane.width;
            _paneHeight = pane.height;
            _windowX = _window?.x ?? 0f;
            _windowY = _window?.y ?? 0f;
            _windowWidth = _window?.width ?? 0f;
            _windowHeight = _window?.height ?? 0f;
            _backgroundX = background.x;
            _backgroundY = background.y;
            _backgroundWidth = background.width;
            _backgroundHeight = background.height;
            foreach (GObject child in pane.GetChildren())
                if (child != null && !child.isDisposed) _paneChildren.Add(new Snapshot(child));
            foreach (GObject child in background.GetChildren())
                if (child != null && !child.isDisposed) _backgroundChildren.Add(new Snapshot(child));
        }

        internal static PropPopupLayout? Expand(GComponent pane, GLabel background,
            IReadOnlyList<GObject> footer) {
            if (pane == null || pane.isDisposed || background == null ||
                background.isDisposed || background.parent != pane ||
                background.width < 250f || background.height < 200f) return null;
            var layout = new PropPopupLayout(pane, background);
            try {
                if (!layout.Apply()) {
                    layout.Dispose();
                    return null;
                }
                return layout;
            } catch (Exception ex) {
                layout.Dispose();
                SdkLog.Warn("GiftGuide", "原版弹窗扩展失败：" + ex);
                return null;
            }
        }

        private bool Apply() {
            GRoot root = GRoot.inst;
            if (root == null || root.width < 650f || root.height < 420f) return false;

            // These game windows often have a full-screen Window/contentPane
            // with a smaller centered GLabel named bottom. Resizing the entire
            // screen-sized Window produces no usable space and moves controls.
            bool fullScreenFrame = _paneWidth >= root.width * .85f ||
                (_window != null && _windowWidth >= root.width * .85f);
            float extraWidth = (float)Math.Floor(Math.Min(740f,
                Math.Max(0f, root.width - _backgroundWidth - 32f)) * .5f) * 2f;
            // Only add height below the original content. The original title,
            // icon and purchase controls stay in the original vertical band.
            float top = (_window != null ? _windowY + _paneY : _paneY) + _backgroundY;
            float wanted = Math.Min(335f, root.height * .36f);
            float roomBelow = Math.Max(0f, root.height - top - _backgroundHeight - 12f);
            float moveUp = Math.Min(Math.Max(0f, wanted - roomBelow), Math.Max(0f, top - 12f));
            float extraHeight = Math.Min(wanted, roomBelow + moveUp);
            if (extraHeight < 115f || _backgroundWidth + extraWidth < 440f) return false;

            float half = extraWidth * .5f;
            if (!fullScreenFrame) {
                if (_window != null && !_window.isDisposed) {
                    _window.SetSize(_windowWidth + extraWidth, _windowHeight + extraHeight);
                    _window.SetXY(_windowX - half, _windowY - moveUp);
                    _pane.SetSize(_paneWidth + extraWidth, _paneHeight + extraHeight);
                    _pane.SetXY(_paneX, _paneY);
                } else {
                    _pane.SetSize(_paneWidth + extraWidth, _paneHeight + extraHeight);
                    _pane.SetXY(_paneX - half, _paneY - moveUp);
                }
            }

            foreach (Snapshot child in _paneChildren) {
                if (ReferenceEquals(child.Object, _background)) continue;
                if (child.Width > _paneWidth * .9f && child.Height > _paneHeight * .85f) continue;
                // A full-screen contentPane must stay unchanged. In that case
                // only move its authored content upwards to make lower room.
                child.Shift(fullScreenFrame ? 0f : half,
                    fullScreenFrame ? -moveUp : 0f);
            }
            float newBackgroundX = fullScreenFrame ? _backgroundX - half : _backgroundX;
            float newBackgroundY = fullScreenFrame ? _backgroundY - moveUp : _backgroundY;
            _background.SetXY(newBackgroundX, newBackgroundY);
            _background.SetSize(_backgroundWidth + extraWidth, _backgroundHeight + extraHeight);
            foreach (Snapshot child in _backgroundChildren) {
                bool backdrop = child.Width >= _backgroundWidth * .8f &&
                    child.Height >= _backgroundHeight * .64f &&
                    (child.Object is GGraph || child.Object is GImage);
                if (backdrop) {
                    child.Object.SetSize(child.Width + extraWidth, child.Height + extraHeight);
                    child.Object.SetXY(child.X, child.Y);
                } else {
                    child.Shift(half);
                }
            }

            // The scroll area belongs to the actual window, directly beneath
            // its ORIGINAL height rather than beside it or above the footer.
            _guideLeft = newBackgroundX + 16f;
            _guideTop = newBackgroundY + _backgroundHeight + 6f;
            _guideWidth = _backgroundWidth + extraWidth - 32f;
            _guideHeight = extraHeight - 18f;
            return true;
        }

        internal float GuideLeft => _guideLeft;
        internal float GuideTop => _guideTop;
        internal float GuideWidth => _guideWidth;
        internal float GuideHeight => _guideHeight;

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            if (!_background.isDisposed) {
                _background.SetSize(_backgroundWidth, _backgroundHeight);
                _background.SetXY(_backgroundX, _backgroundY);
                foreach (Snapshot child in _backgroundChildren) child.Restore();
            }
            if (_window != null && !_window.isDisposed) {
                _window.SetSize(_windowWidth, _windowHeight);
                _window.SetXY(_windowX, _windowY);
            }
            if (!_pane.isDisposed) {
                _pane.SetSize(_paneWidth, _paneHeight);
                _pane.SetXY(_paneX, _paneY);
                foreach (Snapshot child in _paneChildren) child.Restore();
            }
        }
    }
}
