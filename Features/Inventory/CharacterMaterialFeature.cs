using System;
using CesiumLoader.SDK.Logging;

namespace AstralProbe.Features.Inventory {
    /// <summary>
    /// Retains the original shop/detail service split. The Cesium lifecycle
    /// owns the services, and UI containers are removed on scene transitions.
    /// </summary>
    internal sealed class CharacterMaterialFeature : IDisposable {
        private readonly StoreItemUsageService _shop = new StoreItemUsageService();
        private readonly PropUsageDescriptionService _detail = new PropUsageDescriptionService();
        private readonly BoxPropUsageService _box = new BoxPropUsageService();
        private DateTime _next;
        private DateTime _retry;
        private DateTime _warnAt;
        private bool _enabled;

        internal bool Enabled {
            get => _enabled;
            set {
                if (_enabled == value) return;
                _enabled = value;
                _shop.Enabled = value;
                _detail.Enabled = value;
                _box.Enabled = value;
                if (!value) Clear();
            }
        }

        internal void Tick() {
            if (!Enabled || DateTime.UtcNow < _next || DateTime.UtcNow < _retry) return;
            _next = DateTime.UtcNow.AddMilliseconds(350);
            try {
                _shop.Tick();
                _detail.Tick();
                _box.Tick();
            } catch (Exception e) {
                Clear();
                _retry = DateTime.UtcNow.AddSeconds(10);
                if (DateTime.UtcNow >= _warnAt) {
                    _warnAt = DateTime.UtcNow.AddSeconds(30);
                    SdkLog.Warn("GiftGuide", "背包角色材料列表暂不可用：" + e.Message);
                }
            }
        }

        internal void Clear() {
            _shop.Shutdown();
            _detail.Shutdown();
            _box.Shutdown();
        }

        public void Dispose() {
            _shop.Dispose();
            _detail.Dispose();
            _box.Dispose();
        }
    }
}
