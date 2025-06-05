using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace ParkingPricing
{
    [FileLocation(nameof(ParkingPricing))]
    [SettingsUIGroupOrder(LotGroup, StreetGroup, SettingsGroup)]
    [SettingsUIShowGroupName(LotGroup, StreetGroup, SettingsGroup)]
    public class ModSettings : ModSetting
    {
        public const string ParkingSection = "ParkingSection";
        public const string LotGroup = "LotGroup";
        public const string StreetGroup = "StreetGroup";
        public const string SettingsGroup = "SettingsGroup";

        public ModSettings(IMod mod) : base(mod)
        {
            if (target_occupancy_lot == 0) SetDefaults();
        }

        public override void SetDefaults()
        {
            enable_for_street = false;
            target_occupancy_street = 50;
            standard_price_street = 5;
            max_price_increase_street = 200;
            max_price_discount_street = 50;
            enable_for_lot = true;
            target_occupancy_lot = 50;
            standard_price_lot = 10;
            max_price_increase_lot = 200;
            max_price_discount_lot = 50;
            updateFreq = UpdateFreqEnum.min45;
        }

        [SettingsUISection(ParkingSection, LotGroup)]
        public bool enable_for_lot { get; set; }

        [SettingsUIHidden()]
        public int target_occupancy_lot { get; set; }

        [SettingsUISlider(min = 0, max = 50, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        [SettingsUISection(ParkingSection, LotGroup)]
        [SettingsUIDisableByConditionAttribute(typeof(Setting), nameof(enable_for_lot), true)]
        public int standard_price_lot { get; set; }

        [SettingsUISlider(min = 0, max = 300, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, LotGroup)]
        [SettingsUIDisableByConditionAttribute(typeof(Setting), nameof(enable_for_lot), true)]
        public int max_price_increase_lot { get; set; }

        [SettingsUISlider(min = 0, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, LotGroup)]
        [SettingsUIDisableByConditionAttribute(typeof(Setting), nameof(enable_for_lot), true)]
        public int max_price_discount_lot { get; set; }

        [SettingsUISection(ParkingSection, StreetGroup)]
        public bool enable_for_street { get; set; }

        [SettingsUIHidden()]
        public int target_occupancy_street { get; set; }

        [SettingsUISlider(min = 0, max = 50, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        [SettingsUISection(ParkingSection, StreetGroup)]
        [SettingsUIDisableByConditionAttribute(typeof(Setting), nameof(enable_for_street), true)]
        public int standard_price_street { get; set; }

        [SettingsUISlider(min = 0, max = 300, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, StreetGroup)]
        [SettingsUIDisableByConditionAttribute(typeof(Setting), nameof(enable_for_street), true)]
        public int max_price_increase_street { get; set; }

        [SettingsUISlider(min = 0, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, StreetGroup)]
        [SettingsUIDisableByConditionAttribute(typeof(Setting), nameof(enable_for_street), true)]
        public int max_price_discount_street { get; set; }

        [SettingsUISection(ParkingSection, SettingsGroup)]
        public UpdateFreqEnum updateFreq { get; set; }

        public enum UpdateFreqEnum
        {
            min45 = 32,
            min22 = 64,
            min90 = 16
        }

        [SettingsUIButton]
        [SettingsUISection(ParkingSection, SettingsGroup)]
        public bool ResetButton
        {
            set
            {
                SetDefaults();
            }
        }
    }
}
