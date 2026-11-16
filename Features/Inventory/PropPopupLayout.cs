#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Expands the existing native popup without moving its authored item
    /// artwork/text. The frame's baseline is persistent, while the *current*
    /// content snapshots are captured per display: unrelated items must never
    /// be reset to positions from another item or controller state.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private const float Edge = 12f;
        private const float Inner = 16f;
        private const float Gap = 5f;
        private const float ExtraHeight = 390f;
        private const float PreferredLift = 76f;
        private const float MaxLift = 160f;
        private static readonly ConditionalWeakTable<GComponent, Baseline> Originals =
            new ConditionalWeakTable<GComponent, Baseline>();
        private readonly Baseline _baseline;
        private readonly List<Snapshot> _currentSiblings = new List<Snapshot>();
        private readonly List<Snapshot> _currentDecorations = new List<Snapshot>();
        private bool _disposed;
        private float _left, _top, _width, _height;

        private sealed class Snapshot {
            internal readonly GObject Item;
            internal readonly float X, Y, Width, Height;
            internal Snapshot(GObject item) {
                Item = item;
                X = item.x; Y = item.y; Width = item.width; Height = item.height;
            }
            internal void Restore() {
                if (Item == null || Item.isDisposed) return;
                if (Math.Abs(Item.width - Width) > .01f || Math.Abs(Item.height - Height) > .01f)
                    Item.SetSize(Width, Height, true);
                Item.SetXY(X, Y);
            }
        }

        private sealed class Baseline {
            internal readonly GComponent Pane;
            internal readonly GLabel Frame;
            internal readonly Snapshot Window;
            internal Baseline(GComponent pane, GLabel frame) {
                Pane = pane; Frame = frame; Window = new Snapshot(frame);
            }
            internal void Restore() {
                if (Pane == null || Pane.isDisposed || Frame == null || Frame.isDisposed) return;
                Window.Restore();
            }
        }

        private PropPopupLayout(Baseline baseline) {
            _baseline = baseline;
            // A pooled pane may already have refreshed its item or changed
            // controller pages. Capture sibling positions NOW rather than
            // caching those of the first item for the pane's entire lifetime.
            foreach (GObject obj in baseline.Pane.GetChildren())
                if (obj != null && !obj.isDisposed &&
                    !ReferenceEquals(obj, baseline.Frame))
                    _currentSiblings.Add(new Snapshot(obj));
            foreach (GObject obj in baseline.Frame.GetChildren())
                if (obj != null && !obj.isDisposed)
                    _currentDecorations.Add(new Snapshot(obj));
        }

        internal static PropPopupLayout? Expand(GComponent pane, GLabel background,
            IReadOnlyList<GObject> footer, IReadOnlyList<GObject> content,
            GTextField? description, GButton? preview, GTextField? name, bool afterFooter) {
            if (pane == null || pane.isDisposed || background == null || background.isDisposed ||
                background.parent != pane || background.width < 260f || background.height < 170f)
                return null;
            if (!Originals.TryGetValue(pane, out Baseline baseline) ||
                !ReferenceEquals(baseline.Frame, background)) {
                Originals.Remove(pane);
                baseline = new Baseline(pane, background);
                Originals.Add(pane, baseline);
            } else if (Math.Abs(background.width - baseline.Window.Width) > .5f ||
                       Math.Abs(background.height - baseline.Window.Height) > .5f) {
                // Only repair an interrupted previous expansion. Do not reset
                // unmodified content when the user opens a different item.
                baseline.Restore();
            }
            var layout = new PropPopupLayout(baseline);
            try {
                if (layout.Apply(footer, content, description, preview, name, afterFooter))
                    return layout;
            } catch (Exception ex) {
                SdkLog.Warn("GiftGuide", "扩展原版物品弹窗失败：" + ex);
            }
            layout.Dispose();
            return null;
        }

        private static float ParentOffset(GObject obj, bool horizontal) {
            float value = 0f;
            for (GObject? p = obj.parent; p != null && !(p is GRoot); p = p.parent)
                value += horizontal ? p.x : p.y;
            return value;
        }

        private static float ContentBottom(GObject? obj, GComponent pane, float current,
            float originalFrameBottom) {
            if (obj == null || obj.isDisposed || !obj.visible || obj.parent != pane ||
                (obj is GList list && list.numItems <= 0)) return current;
            // Some invisible controller pages report a huge authored height.
            // They cannot be used as the lower edge of the visible detail.
            float bottom = obj.y + obj.height;
            return bottom <= originalFrameBottom + 16f ? Math.Max(current, bottom) : current;
        }

        private bool Apply(IReadOnlyList<GObject> footer, IReadOnlyList<GObject> content,
            GTextField? description, GButton? preview, GTextField? name, bool afterFooter) {
            GRoot root = GRoot.inst;
            if (root == null || root.width < 640f || root.height < 440f) return false;
            Baseline original = _baseline;
            GComponent pane = original.Pane;
            GLabel frame = original.Frame;
            float x = original.Window.X, y = original.Window.Y;
            float w = original.Window.Width, h = original.Window.Height;
            float screenX = ParentOffset(frame, true) + x;
            float screenY = ParentOffset(frame, false) + y;
            float extraWidth = Math.Max(0f, Math.Min(520f, root.width - 2f * Edge - w));
            float newScreenX = Math.Max(Edge,
                Math.Min(screenX - extraWidth * .5f, root.width - Edge - w - extraWidth));
            float dx = newScreenX - screenX;
            float wantedExtraHeight = Math.Max(0f, Math.Min(ExtraHeight,
                root.height - 2f * Edge - h));
            // This can be negative if the native popup already extends beyond
            // a low-resolution viewport. Preserve the overflow when choosing
            // the lift, otherwise the enlarged frame can still run offscreen.
            float roomBelow = root.height - Edge - (screenY + h);
            // Expand upwards as well as downwards, and lift the native frame
            // so its original artwork is not pressed against the top edge.
            // The content remains stationary in pane coordinates below.
            float lift = Math.Min(Math.Max(0f, screenY - Edge),
                Math.Min(MaxLift, Math.Max(PreferredLift,
                    wantedExtraHeight - roomBelow)));
            float extraHeight = Math.Max(0f, Math.Min(wantedExtraHeight, roomBelow + lift));
            if (extraWidth < 65f || extraHeight < 115f) return false;

            // Widening the frame does not make the native description taller.
            // Expand it only within the original text area; the baseline
            // snapshots restore its native height on close.
            float expandedDescriptionHeight = 0f;
            if (description != null && !description.isDisposed && description.parent == pane) {
                float reserveBottom = y + h - 72f;
                if (footer != null) foreach (GObject control in footer)
                    if (control != null && !control.isDisposed && control.parent == pane &&
                        control.visible && control.y > description.y)
                        reserveBottom = Math.Min(reserveBottom, control.y - Gap);
                float maxHeight = Math.Max(description.height, reserveBottom - description.y);
                float desired = Math.Max(description.height, description.textHeight + 7f);
                if (desired > description.height + 1f && maxHeight > description.height + 1f) {
                    expandedDescriptionHeight = Math.Min(maxHeight, desired);
                    description.SetSize(description.width, expandedDescriptionHeight, true);
                }
            }

            // Compute the actual bottom of authored content BEFORE changing
            // coordinates. The purchase list must appear after its buttons.
            float originalBottom = y + h;
            float contentBottom = y + 38f;
            contentBottom = ContentBottom(description, pane, contentBottom, originalBottom);
            contentBottom = ContentBottom(preview, pane, contentBottom, originalBottom);
            contentBottom = ContentBottom(name, pane, contentBottom, originalBottom);
            if (content != null) foreach (GObject obj in content)
                contentBottom = ContentBottom(obj, pane, contentBottom, originalBottom);
            float footerBottom = contentBottom;
            if (footer != null) foreach (GObject obj in footer)
                footerBottom = ContentBottom(obj, pane, footerBottom, originalBottom);
            float buttonReserve = 0f;
            if (frame is UICom_PopUpWindow_Bottom native) {
                GButton?[] nativeButtons = { native.btn_Sure, native.btn_Sure_Only, native.btn_Cancel };
                foreach (GButton? button in nativeButtons) {
                    if (button == null || button.isDisposed || !button.visible) continue;
                    if (afterFooter)
                        footerBottom = Math.Max(footerBottom, y + button.y + button.height);
                    else
                        buttonReserve = Math.Max(buttonReserve, h - button.y);
                }
            }

            // The expanded frame is centered around the *existing content*.
            // FairyGUI can move siblings through size relations. Immediately
            // undo that automatic movement; the item's image and text must
            // remain at their original absolute positions (no +dx/-lift).
            frame.SetSize(w + extraWidth, h + extraHeight, true);
            frame.SetXY(x + dx, y - lift);
            foreach (Snapshot snap in _currentSiblings) snap.Restore();
            // Reapply the extra text height after restoring FairyGUI relation
            // side effects, or the snapshot will silently undo the expansion.
            if (expandedDescriptionHeight > 0f && description != null &&
                !description.isDisposed)
                description.SetSize(description.width, expandedDescriptionHeight, true);
            foreach (Snapshot snap in _currentSiblings)
                if (!ReferenceEquals(snap.Item, description)) snap.Restore();
            foreach (Snapshot snap in _currentDecorations) {
                GObject obj = snap.Item;
                if (obj == null || obj.isDisposed) continue;
                bool backdrop = (obj is GImage || obj is GGraph || obj is GLoader) &&
                    snap.Width >= w * .65f && snap.Height >= h * .55f;
                if (backdrop) {
                    obj.SetXY(snap.X, snap.Y);
                    obj.SetSize(snap.Width + extraWidth, snap.Height + extraHeight, true);
                } else if (frame is UICom_PopUpWindow_Bottom bottom &&
                    ReferenceEquals(obj, bottom.closeButton)) {
                    obj.SetXY(snap.X + extraWidth, snap.Y);
                } else if (!afterFooter && frame is UICom_PopUpWindow_Bottom actions &&
                    (ReferenceEquals(obj, actions.btn_Sure) ||
                     ReferenceEquals(obj, actions.btn_Sure_Only) ||
                     ReferenceEquals(obj, actions.btn_Cancel))) {
                    // Detail-window action buttons belong below the guide,
                    // rather than creating a blank strip above the guide.
                    obj.SetXY(snap.X - dx, snap.Y + lift + extraHeight);
                } else {
                    // Offset the *local* coordinates to cancel out the new
                    // frame origin. This retains the exact old visual position
                    // of the title and embedded buttons; only the frame grows.
                    obj.SetXY(snap.X - dx, snap.Y + lift);
                }
            }

            _left = x + dx + Inner;
            _width = w + extraWidth - 2f * Inner;
            // Existing pane children were not lifted: use their REAL bottom.
            _top = Math.Max(contentBottom, footerBottom) + Gap;
            float maxBottom = y - lift + h + extraHeight - Inner;
            _height = Math.Max(0f, maxBottom - _top -
                (afterFooter ? 0f : buttonReserve));
            return _width >= 270f && _height >= 105f;
        }

        internal float GuideLeft => _left;
        internal float GuideTop => _top;
        internal float GuideWidth => _width;
        internal float GuideHeight => _height;

        public void Dispose() {
            if (_disposed) return;
            _disposed = true;
            _baseline.Restore();
            foreach (Snapshot snap in _currentDecorations) snap.Restore();
            foreach (Snapshot snap in _currentSiblings) snap.Restore();
        }
    }
}
