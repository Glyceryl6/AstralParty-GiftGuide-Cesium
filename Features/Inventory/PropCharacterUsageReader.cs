using System;
using System.Collections.Generic;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Shared material-usage snapshot for shop and bag item windows.
    /// Keep the original inventory feature boundary while using managed
    /// AstralParty.Runtime data instead of native pointer reflection.
    /// </summary>
    internal static class PropCharacterUsageReader {
        internal static bool TryBuildForItem(int itemId, out List<CharacterMaterialUsage> usages) {
            if (!CharacterMaterialUsageReader.TryRead(itemId, out usages)) return false;
            // Always work on a materialized snapshot; callbacks should not retain
            // references to a mutable Protobuf list or a live hero-card container.
            if (usages.Count > 512) usages.RemoveRange(512, usages.Count - 512);
            return true;
        }
    }
}
