using AstralProbe.Features.Characters;
using AstralProbe.Features.Inventory;
using CesiumLoader.SDK.Mods;
using UnityEngine.SceneManagement;

namespace GiftGuide {
    /// <summary>礼物指南：安装后自动启用，没有功能开关。</summary>
    public sealed class GiftGuideMod : ModBase {
        private CharacterMaterialFeature? _materials;
        private PreferredGiftBadgeService? _preferred;

        public override string Name => "GiftGuide";
        public override string Version => "0.2.9";
        public override string Description => "礼物指南：角色适用材料、偏好礼物标识";

        public override void OnLoad() => Log.Info("礼物指南已加载");
        public override void OnInitialize() {
            _materials = new CharacterMaterialFeature { Enabled = true };
            _preferred = new PreferredGiftBadgeService { Enabled = true };
        }
        public override void OnUpdate() {
            _materials?.Tick();
            _preferred?.Tick();
        }
        public override void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ClearScene();
        public override void OnSceneUnloaded(Scene scene) => ClearScene();
        private void ClearScene() {
            _materials?.Clear();
            _preferred?.Clear();
        }
        public override void OnUnload() {
            _materials?.Dispose(); _materials = null;
            _preferred?.Dispose(); _preferred = null;
        }
    }
}
