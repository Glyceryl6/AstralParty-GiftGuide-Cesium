#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Enlarges the *native* Com_PopUpWindow_Bottom frame. A PropDetailWindow or
    /// PurchaseWindow reuses its contentPane across Show/Hide. Its original
    /// positions must therefore be captured once per pane, not from the result
    /// of the previous resize (or FairyGUI's relations will accumulate offsets).
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private const float Edge = 12f;
        private const float Inner = 16f;
        private const float Gap = 9f;
        private const float ExtraHeight = 310f;
        private const float MaxLift = 24f;
        private static readonly ConditionalWeakTable<GComponent, Baseline> Originals =
            new ConditionalWeakTable<GComponent, Baseline>();
        private readonly Baseline _baseline;
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
            internal readonly List<Snapshot> Siblings = new List<Snapshot>();
            internal readonly List<Snapshot> Decorations = new List<Snapshot>();
            internal Baseline(GComponent pane, GLabel frame) {
                Pane = pane; Frame = frame; Window = new Snapshot(frame);
                foreach (GObject obj in pane.GetChildren())
                    if (obj != null && !obj.isDisposed && !ReferenceEquals(obj, frame))
                        Siblings.Add(new Snapshot(obj));
                foreach (GObject obj in frame.GetChildren())
                    if (obj != null && !obj.isDisposed) Decorations.Add(new Snapshot(obj));
            }
            internal void Restore() {
                if (Pane == null || Pane.isDisposed || Frame == null || Frame.isDisposed) return;
                // SetSize may run FairyGUI relations synchronously. Restore
                // child coordinates AFTER restoring the parent size and pose.
                Window.Restore();
                foreach (Snapshot snap in Siblings) snap.Restore();
                foreach (Snapshot snap in Decorations) snap.Restore();
            }
        }

        private PropPopupLayout(Baseline baseline) { _baseline = baseline; }

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
            } else {
                // The panel is pooled by the original game. Normalize even if
                // a prior Hide was interrupted by a controller transition.
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
            float roomBelow = Math.Max(0f, root.height - Edge - (screenY + h));
            float lift = Math.Min(MaxLift, Math.Min(Math.Max(0f, screenY - Edge),
                Math.Max(0f, wantedExtraHeight - roomBelow)));
            float extraHeight = Math.Max(0f, Math.Min(wantedExtraHeight, roomBelow + lift));
            if (extraWidth < 65f || extraHeight < 115f) return false;

            // Widening the frame does not make the native description taller.
            // Expand it only within the original text area; the baseline
            // snapshots restore its native height on close.
            if (description != null && !description.isDisposed && description.parent == pane) {
                float reserveBottom = y + h - 72f;
                if (footer != null) foreach (GObject control in footer)
                    if (control != null && !control.isDisposed && control.parent == pane &&
                        control.visible && control.y > description.y)
                        reserveBottom = Math.Min(reserveBottom, control.y - Gap);
                float maxHeight = Math.Max(description.height, reserveBottom - description.y);
                float desired = Math.Max(description.height, description.textHeight + 7f);
                if (desired > description.height + 1f && maxHeight > description.height + 1f)
                    description.SetSize(description.width, Math.Min(maxHeight, desired), true);
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
            if (frame is UICom_PopUpWindow_Bottom native) {
                GButton?[] nativeButtons = { native.btn_Sure, native.btn_Sure_Only, native.btn_Cancel };
                foreach (GButton? button in nativeButtons) {
                    if (button == null || button.isDisposed || !button.visible) continue;
                    footerBottom = Math.Max(footerBottom, y + button.y + button.height);
                }
            }

            // Move the *whole original window* uniformly. Never move the item
            // loader, description or footer independently, and never push a
            // footer down by the expansion height. This prevents the old
            // image/text offsets accumulating on each reopening.
            frame.SetSize(w + extraWidth, h + extraHeight, true);
            frame.SetXY(x + dx, y - lift);
            foreach (Snapshot snap in original.Siblings) {
                GObject obj = snap.Item;
                if (obj == null || obj.isDisposed) continue;
                bool fullScreenVeil = snap.Width >= pane.width * .85f &&
                    snap.Height >= pane.height * .85f;
                if (!fullScreenVeil) obj.SetXY(snap.X + dx, snap.Y - lift);
                else snap.Restore();
            }
            foreach (Snapshot snap in original.Decorations) {
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
                } else {
                    // Keep the authored title and footer in the center of the
                    // expanded frame, at their ORIGINAL vertical position.
                    obj.SetXY(snap.X + extraWidth * .5f, snap.Y);
                }
            }

            _left = x + dx + Inner;
            _width = w + extraWidth - 2f * Inner;
            _top = Math.Max(contentBottom, footerBottom) + Gap + (-lift);
            float maxBottom = y - lift + h + extraHeight - Inner;
            _height = Math.Max(0f, maxBottom - _top);
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
        }
    }
}
