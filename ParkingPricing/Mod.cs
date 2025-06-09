using Colossal.IO.AssetDatabase;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace ParkingPricing {
    public class Mod : IMod {
        public static ModSettings Setting;

        public void OnLoad(UpdateSystem updateSystem) {
            LogUtil.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out ExecutableAsset asset)) {
                LogUtil.Info($"Current mod asset at {asset.path}");
            }

            Setting = new ModSettings(this);
            Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Setting));

            AssetDatabase.global.LoadSettings(nameof(ParkingPricing), Setting, new ModSettings(this));

            updateSystem.UpdateAt<ParkingPricingSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<ParkingPricingEntityCommandBufferSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<PolicyUpdateSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose() {
            LogUtil.Info(nameof(OnDispose));
            if (Setting == null) {
                return;
            }

            Setting.UnregisterInOptionsUI();
            Setting = null;
        }
    }
}
