using System.Collections.Generic;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;

namespace ParkingPricing
{
    [FileLocation(nameof(ParkingPricing))]
    [SettingsUIGroupOrder(SettingsGroup)]
    [SettingsUIShowGroupName(SettingsGroup)]
    public class ModSettings : ModSetting
    {
        public const string ParkingSection = "ParkingSection";
        public const string SettingsGroup = "SettingsGroup";

        public ModSettings(IMod mod) : base(mod)
        {
            if (target_occupancy == 0) SetDefaults();
        }

        public override void SetDefaults()
        {
            target_occupancy = 50;
            standard_price = 10;
            max_price_increase = 200;
            max_price_discount = 50;
            updateFreq = UpdateFreqEnum.min45;
        }

        [SettingsUISlider(min = 10, max = 90, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, SettingsGroup)]
        public int target_occupancy { get; set; }

        [SettingsUISlider(min = 0, max = 50, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        [SettingsUISection(ParkingSection, SettingsGroup)]
        public int standard_price { get; set; }

        [SettingsUISlider(min = 0, max = 300, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, SettingsGroup)]
        public int max_price_increase { get; set; }

        [SettingsUISlider(min = 0, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, SettingsGroup)]
        public int max_price_discount { get; set; }

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
