using CesiumLoader.SDK.Manifests;
using CesiumLoader.SDK.Mods;

namespace GiftGuide {
    public static class ModEntry {
        public static void Main() {
            SdkManifest.ExportSidecar();
            ModBase.Run(new GiftGuideMod(), tag: "礼物指南");
        }
    }
}
