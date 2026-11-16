using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// The native store quality/name components live in Common_External.
    /// The scroll viewport uses the game's own GScrollBar, and card status
    /// icons and breakthrough items come from the Hero package.
    /// </summary>
    internal static class UiPackageWarmup {
        private const string RequiredPackage = "Common_External";
        private static readonly HashSet<string> Loading = new();
        private static readonly Dictionary<string, DateTime> RetryAfter = new();

        internal static bool Ready() => Ensure(RequiredPackage) && Ensure("Hero");

        // The mail package contains the game's own vertical scrollbar design.
        // Load it only if no native scrollbar exists in the already-open UI.
        internal static void RequestNativeScrollBar() => Ensure("Mail");

        private static bool Ensure(string package) {
            if (UIPackage.GetByName(package) != null) return true;
            if (Loading.Contains(package) ||
                (RetryAfter.TryGetValue(package, out DateTime at) && DateTime.UtcNow < at)) return false;
            Loading.Add(package);
            try {
                var awaiter = UIPackage.AddPackageAsync(package, "").GetAwaiter();
                if (awaiter.IsCompleted) Finish(package, () => awaiter.GetResult());
                else awaiter.OnCompleted(() => Finish(package, () => awaiter.GetResult()));
            } catch (Exception ex) {
                Fail(package, ex);
            }
            return false;
        }

        private static void Finish(string package, Action complete) {
            try {
                complete();
                Loading.Remove(package);
            } catch (Exception ex) {
                Fail(package, ex);
            }
        }

        private static void Fail(string package, Exception ex) {
            Loading.Remove(package);
            RetryAfter[package] = DateTime.UtcNow.AddSeconds(8);
            SdkLog.Warn("GiftGuide", "原版 UI 资源包等待重试：" + package + " · " + ex);
        }
    }
}
