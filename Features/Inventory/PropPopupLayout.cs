using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Keep the native dialog's title, item art and description as one unit.
    /// Only extend its native Bottom background downwards, relocating the
    /// confirm/cancel buttons into the new footer. All coordinates stay in the
    /// objects' own parent space (no LocalToGlobal on uninitialized objects).
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private const float Margin = 14f;
        private const float FooterReserve = 62f;
        private readonly GComponent _pane;
        private readonly UICom_PopUpWindow_Bottom _background;
        private readonly Window? _window;
        private readonly Snapshot _paneSnapshot;
        private readonly Snapshot _backgroundSnapshot;
        private readonly Snapshot? _windowSnapshot;
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
            internal void Position(float dx, float dy) {
                if (Object != null && !Object.isDisposed) Object.SetXY(X + dx, Y + dy);
            }
        }

        private PropPopupLayout(GComponent pane, UICom_PopUpWindow_Bottom background) {
            _pane = pane;
            _background = background;
            _window = pane.parent as Window;
            _paneSnapshot = new Snapshot(pane);
            _backgroundSnapshot = new Snapshot(background);
            if (_window != null) _windowSnapshot = new Snapshot(_window);
            foreach (GObject child in pane.GetChildren())
                if (child != null && !child.isDisposed) _paneChildren.Add(new Snapshot(child));
            foreach (GObject child in background.GetChildren())
                if (child != null && !child.isDisposed) _backgroundChildren.Add(new Snapshot(child));
        }

        internal static PropPopupLayout? Expand(GComponent pane, GLabel background,
            IReadOnlyList<GObject> footer) {
            // The native dialogs both use Common/Com_PopUpWindow_Bottom.
            // Refuse unknown templates instead of moving the game's HUD.
            if (pane == null || pane.isDisposed ||
                !(background is UICom_PopUpWindow_Bottom bottom) || bottom.isDisposed ||
                bottom.parent != pane || bottom.width < 260f || bottom.height < 170f) return null;
            var layout = new PropPopupLayout(pane, bottom);
            try {
                if (layout.Apply()) return layout;
            } catch (Exception ex) {
                SdkLog.Warn("GiftGuide", "原版物品弹窗调整失败：" + ex);
            }
            layout.Dispose();
            return null;
        }

        private bool Apply() {
            GRoot root = GRoot.inst;
            if (root == null || root.width < 640f || root.height < 440f) return false;
            float rootWidth = root.width;
            float rootHeight = root.height;
            float oldWidth = _backgroundSnapshot.Width;
            float oldHeight = _backgroundSnapshot.Height;
            float globalLeft = (_windowSnapshot?.X ?? 0f) + _paneSnapshot.X + _backgroundSnapshot.X;
            float globalTop = (_windowSnapshot?.Y ?? 0f) + _paneSnapshot.Y + _backgroundSnapshot.Y;
            float sideRoom = Math.Max(0f, Math.Min(globalLeft - Margin,
                rootWidth - globalLeft - oldWidth - Margin));
            float halfExtra = Math.Min(360f, sideRoom);
            float extraWidth = halfExtra * 2f;

            // Never grow the dialog above the screen. Shift the *complete*
            // native contents upwards only if the larger dialog needs room.
            float maxExtraHeight = Math.Max(0f, rootHeight - oldHeight - Margin * 2f);
            float wantedExtraHeight = Math.Min(Math.Min(325f, rootHeight * .33f), maxExtraHeight);
            float moveUp = Math.Min(Math.Max(0f, globalTop + oldHeight + wantedExtraHeight -
                (rootHeight - Margin)), Math.Max(0f, globalTop - Margin));
            float extraHeight = Math.Min(wantedExtraHeight,
                Math.Max(0f, rootHeight - Margin - (globalTop - moveUp) - oldHeight));
            if (extraHeight < 100f || oldWidth + extraWidth < 480f) return false;

            bool screenPane = _paneSnapshot.Width >= rootWidth * .85f ||
                (_windowSnapshot != null && _windowSnapshot.Width >= rootWidth * .85f);
            if (!screenPane) {
                if (_windowSnapshot != null) {
                    _window!.SetSize(_windowSnapshot.Width + extraWidth,
                        _windowSnapshot.Height + extraHeight, true);
                    _window.SetXY(_windowSnapshot.X - halfExtra, _windowSnapshot.Y - moveUp);
                    _pane.SetSize(_paneSnapshot.Width + extraWidth,
                        _paneSnapshot.Height + extraHeight, true);
                    _pane.SetXY(_paneSnapshot.X, _paneSnapshot.Y);
                } else {
                    _pane.SetSize(_paneSnapshot.Width + extraWidth,
                        _paneSnapshot.Height + extraHeight, true);
                    _pane.SetXY(_paneSnapshot.X - halfExtra, _paneSnapshot.Y - moveUp);
                }
            }

            // Restore the unmodified native item layout after container resize.
            // The modal backdrop must continue to fill the screen.
            foreach (Snapshot child in _paneChildren) {
                if (ReferenceEquals(child.Object, _background)) continue;
                child.Restore();
                bool isModalBackdrop = child.Width >= _paneSnapshot.Width * .85f &&
                    child.Height >= _paneSnapshot.Height * .85f;
                if (!isModalBackdrop) child.Position(screenPane ? 0f : halfExtra,
                    screenPane ? -moveUp : 0f);
            }

            _background.SetSize(oldWidth + extraWidth, oldHeight + extraHeight, true);
            _background.SetXY(_backgroundSnapshot.X + (screenPane ? -halfExtra : 0f),
                _backgroundSnapshot.Y + (screenPane ? -moveUp : 0f));
            foreach (Snapshot child in _backgroundChildren) {
                child.Restore();
                bool backdrop = (child.Object is GGraph || child.Object is GImage) &&
                    child.Width >= oldWidth * .65f && child.Height >= oldHeight * .55f;
                if (backdrop) {
                    child.Object.SetSize(child.Width + extraWidth, child.Height + extraHeight, true);
                } else {
                    // Undo the background's leftward enlargement, keeping its
                    // title and close button over the original item content.
                    bool lowerButton = ReferenceEquals(child.Object, _background.btn_Sure) ||
                        ReferenceEquals(child.Object, _background.btn_Sure_Only) ||
                        ReferenceEquals(child.Object, _background.btn_Cancel);
                    child.Position(halfExtra, lowerButton ? extraHeight : 0f);
                }
            }

            // The guide occupies the extended lower part, while the native
            // confirm/cancel buttons occupy the final reserved footer row.
            _guideLeft = _background.x + 18f;
            _guideTop = _background.y + oldHeight - 20f;
            _guideWidth = oldWidth + extraWidth - 36f;
            _guideHeight = extraHeight - FooterReserve + 4f;
            return _guideHeight >= 40f;
        }

        internal float GuideLeft => _guideLeft;
        internal float GuideTop => _guideTop;
        internal float GuideWidth => _guideWidth;
        internal float GuideHeight => _guideHeight;

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            // Restore the parent's native size first; then restore exact child
            // coordinates so FairyGUI relations cannot leave altered offsets.
            _windowSnapshot?.Restore();
            _paneSnapshot.Restore();
            _backgroundSnapshot.Restore();
            foreach (Snapshot child in _paneChildren) child.Restore();
            foreach (Snapshot child in _backgroundChildren) child.Restore();
        }
    }
}
