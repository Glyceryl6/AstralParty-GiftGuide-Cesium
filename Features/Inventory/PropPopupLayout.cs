#nullable enable
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CesiumLoader.SDK.Logging;
using FairyGUI;
using UI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Expands the native frame only to the sides and below its original top.
    /// The frame and its original contents then move upwards together, so the
    /// original title, image and description keep their relative layout.
    /// Snapshots are taken for each display and restored when the guide closes.
    /// </summary>
    internal sealed class PropPopupLayout : IDisposable {
        private const float Edge = 12f;
        private const float Inner = 24f;
        private const float Gap = 5f;
        private const float DetailGapReach = 156f;
        private const float ExtraHeight = 445f;
        private const float PreferredLift = 76f;
        private const float MaxLift = 260f;
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

        private static float VisibleTextBottom(GTextField? field, GComponent pane,
            float current, float originalFrameBottom) {
            if (field == null || field.isDisposed || !field.visible || field.parent != pane)
                return current;
            // A text field can reserve significantly more height than the
            // rendered description. Ignore only that unused lower text space.
            float height = field.textHeight > 0f
                ? Math.Min(field.height, field.textHeight + 10f) : field.height;
            float bottom = field.y + height;
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
            // Grow the frame DOWNWARDS from its original top, then lift the
            // whole native window. Lifting only the frame used to leave a blank
            // strip above the title and steal height from the character list.
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

            // Measure original content BEFORE moving it. Both windows must
            // place the character list below their existing action buttons.
            float originalBottom = y + h;
            float contentBottom = y + 38f;
            contentBottom = afterFooter
                ? ContentBottom(description, pane, contentBottom, originalBottom)
                : VisibleTextBottom(description, pane, contentBottom, originalBottom);
            contentBottom = ContentBottom(preview, pane, contentBottom, originalBottom);
            contentBottom = ContentBottom(name, pane, contentBottom, originalBottom);
            if (content != null) foreach (GObject obj in content)
                contentBottom = ContentBottom(obj, pane, contentBottom, originalBottom);
            float nativeBottom = contentBottom;
            float nativeContentBottom = contentBottom;
            if (footer != null) foreach (GObject obj in footer)
                nativeContentBottom = ContentBottom(obj, pane, nativeContentBottom, originalBottom);
            nativeBottom = nativeContentBottom;
            // The bottom field is declared as GLabel in both native window
            // classes and is not always upgraded to UICom_PopUpWindow_Bottom.
            // Measure visible frame buttons without requiring that subclass.
            foreach (Snapshot snap in _currentDecorations)
                if (snap.Item is GButton button && !button.isDisposed &&
                    button.visible && snap.Y >= h * .5f)
                    nativeBottom = Math.Max(nativeBottom, y + snap.Y + snap.Height);

            // Resize the ORIGINAL frame (no top extension), then translate it
            // and the native content together. Pane children are independent
            // of the bottom label, so moving only the frame does not move them.
            frame.SetSize(w + extraWidth, h + extraHeight, true);
            frame.SetXY(x + dx, y - lift);

            // FairyGUI size relations may have moved other pane children.
            // Restore their original coordinates before applying the SAME
            // vertical translation as the frame. Do not move the full-screen
            // shade/mohu: it belongs to the whole pane, not the popup itself.
            // Restore FairyGUI groups first: moving a GGroup also moves its
            // members. Set final coordinates only on the actual elements to
            // avoid applying the translation twice to grouped content.
            foreach (Snapshot snap in _currentSiblings)
                if (snap.Item is GGroup) snap.Restore();
            foreach (Snapshot snap in _currentSiblings)
                if (!(snap.Item is GGroup)) snap.Restore();
            foreach (Snapshot snap in _currentSiblings) {
                if (snap.Item is GGroup || IsFullscreenOverlay(snap, pane)) continue;
                snap.Item.SetXY(snap.X, snap.Y - lift);
            }
            // Description is the only original text field we may grow, and
            // only inside its original space. Restore height on close.
            if (expandedDescriptionHeight > 0f && description != null &&
                !description.isDisposed)
                description.SetSize(description.width, expandedDescriptionHeight, true);

            foreach (Snapshot snap in _currentDecorations)
                if (snap.Item is GGroup) snap.Restore();
            foreach (Snapshot snap in _currentDecorations) {
                if (snap.Item is GGroup) continue;
                GObject obj = snap.Item;
                if (obj == null || obj.isDisposed) continue;
                bool backdrop = (obj is GImage || obj is GGraph || obj is GLoader) &&
                    snap.Width >= w * .65f && snap.Height >= h * .55f;
                if (backdrop) {
                    // The existing texture/graph covers the extra sides and
                    // bottom. Its top edge stays at the original local Y.
                    obj.SetSize(snap.Width + extraWidth, snap.Height + extraHeight, true);
                    obj.SetXY(snap.X, snap.Y);
                } else if (obj is GButton &&
                    ((frame is UICom_PopUpWindow_Bottom bottom &&
                      ReferenceEquals(obj, bottom.closeButton)) ||
                     (frame.numChildren > 2 &&
                      ReferenceEquals(obj, frame.GetChildAt(2))))) {
                    // Prevent frame-size relations from stretching the button.
                    snap.Restore();
                    // Close button follows the widened frame's right edge.
                    obj.SetXY(snap.X + extraWidth, snap.Y);
                } else {
                    // Prevent size relations from stretching or hiding the
                    // original title, text and action controls.
                    snap.Restore();
                    // Keep their old SCREEN X while widening symmetrically.
                    // Local Y is unchanged, so they move up with the frame.
                    obj.SetXY(snap.X - dx, snap.Y);
                }
            }

            _left = x + dx + Inner;
            _width = w + extraWidth - 2f * Inner;
            // Native item fields moved by -lift too. Use the shifted bottom,
            // otherwise the viewport begins too low and appears very short.
            _top = Math.Max(contentBottom, nativeBottom) - lift + Gap;
            if (!afterFooter) {
                // Detail window: reach farther into the unused native gap,
                // without covering its visible item text, image, or controls.
                float safeTop = nativeContentBottom - lift + Gap;
                _top = Math.Max(safeTop, _top - DetailGapReach);
            }
            float maxBottom = y - lift + h + extraHeight - Inner;
            _height = Math.Max(0f, maxBottom - _top);
            return _width >= 270f && _height >= 105f;
        }

        private static bool IsFullscreenOverlay(Snapshot snap, GComponent pane) {
            // 'mohu' is the native dimming layer behind the popup. Moving it
            // shifts the whole modal screen and can disturb later openings.
            if (string.Equals(snap.Item.name, "mohu", StringComparison.OrdinalIgnoreCase))
                return true;
            return Math.Abs(snap.X) <= Edge && Math.Abs(snap.Y) <= Edge &&
                   snap.Width >= pane.width * .88f &&
                   snap.Height >= pane.height * .88f;
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
