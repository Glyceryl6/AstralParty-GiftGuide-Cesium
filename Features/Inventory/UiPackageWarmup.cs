using System;
using System.Collections.Generic;
using CesiumLoader.SDK.Logging;
using FairyGUI;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// The Setting and Common_External packages are not necessarily loaded when an item
    /// details popup first opens. Request them without blocking the game loop.
    /// </summary>
    internal static class UiPackageWarmup {
        private static readonly HashSet<string> Loading = new();
        private static readonly Dictionary<string, DateTime> RetryAfter = new();

        internal static bool Ready() {
            bool setting = Ensure("Setting");
            bool quality = Ensure("Common_External");
            return setting && quality;
        }

        private static bool Ensure(string package) {
            if (UIPackage.GetByName(package) != null) return true;
            if (Loading.Contains(package) ||
                (RetryAfter.TryGetValue(package, out DateTime at) && DateTime.UtcNow < at)) return false;
            Loading.Add(package);
            try {
                var awaiter = UIPackage.AddPackageAsync(package, "").GetAwaiter();
                if (awaiter.IsCompleted) Finish(package, () => { awaiter.GetResult(); });
                else awaiter.OnCompleted(() => Finish(package, () => { awaiter.GetResult(); }));
            } catch (Exception e) { Fail(package, e); }
            return false;
        }

        private static void Finish(string package, Action complete) {
            try { complete(); }
            catch (Exception e) { Fail(package, e); return; }
            Loading.Remove(package);
        }

        private static void Fail(string package, Exception e) {
            Loading.Remove(package);
            RetryAfter[package] = DateTime.UtcNow.AddSeconds(8);
            SdkLog.Warn("GiftGuide", "原版 UI 资源包等待重试：" + package + " · " + e.Message);
        }
    }
}
