using Colossal.IO.AssetDatabase;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace ParkingPricing
{
    public class Mod : IMod
    {
        public static ModSettings m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            LogUtil.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
                LogUtil.Info($"Current mod asset at {asset.path}");

            m_Setting = new ModSettings(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));

            AssetDatabase.global.LoadSettings(nameof(ParkingPricing), m_Setting, new ModSettings(this));

            updateSystem.UpdateAt<ParkingPricingSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<PolicyUpdateSystem>(SystemUpdatePhase.GameSimulation);
        }

        public void OnDispose()
        {
            LogUtil.Info(nameof(OnDispose));
            if (m_Setting == null) return;
            m_Setting.UnregisterInOptionsUI();
            m_Setting = null;
        }
    }
}
