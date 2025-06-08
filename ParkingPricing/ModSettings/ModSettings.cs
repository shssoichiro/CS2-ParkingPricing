using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;

namespace ParkingPricing {
    [FileLocation(nameof(ParkingPricing))]
    [SettingsUIGroupOrder(LotGroup, StreetGroup, SettingsGroup)]
    [SettingsUIShowGroupName(LotGroup, StreetGroup, SettingsGroup)]
    public class ModSettings : ModSetting {
        public const string ParkingSection = "ParkingSection";
        public const string LotGroup = "LotGroup";
        public const string StreetGroup = "StreetGroup";
        public const string SettingsGroup = "SettingsGroup";

        public ModSettings(IMod mod) : base(mod) {
            if (TargetOccupancyLot == 0) {
                SetDefaults();
            }
        }

        public sealed override void SetDefaults() {
            EnableForStreet = true;
            TargetOccupancyStreet = 50;
            StandardPriceStreet = 5;
            MaxPriceIncreaseStreet = 300;
            MaxPriceDiscountStreet = 100;
            EnableForLot = true;
            TargetOccupancyLot = 50;
            StandardPriceLot = 10;
            MaxPriceIncreaseLot = 200;
            MaxPriceDiscountLot = 50;
            UpdateFreq = UpdateFreqEnum.Min45;
        }

        [SettingsUISection(ParkingSection, LotGroup)]
        public bool EnableForLot { get; set; }

        [SettingsUIHidden] public int TargetOccupancyLot { get; set; }

        [SettingsUISlider(min = 0, max = 50, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        [SettingsUISection(ParkingSection, LotGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(EnableForLot), true)]
        public int StandardPriceLot { get; set; }

        [SettingsUISlider(min = 0, max = 300, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, LotGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(EnableForLot), true)]
        public int MaxPriceIncreaseLot { get; set; }

        [SettingsUISlider(min = 0, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, LotGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(EnableForLot), true)]
        public int MaxPriceDiscountLot { get; set; }

        [SettingsUISection(ParkingSection, StreetGroup)]
        public bool EnableForStreet { get; set; }

        [SettingsUIHidden] public int TargetOccupancyStreet { get; set; }

        [SettingsUISlider(min = 0, max = 50, step = 1, scalarMultiplier = 1, unit = Unit.kInteger)]
        [SettingsUISection(ParkingSection, StreetGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(EnableForStreet), true)]
        public int StandardPriceStreet { get; set; }

        [SettingsUISlider(min = 0, max = 300, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, StreetGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(EnableForStreet), true)]
        public int MaxPriceIncreaseStreet { get; set; }

        [SettingsUISlider(min = 0, max = 100, step = 1, scalarMultiplier = 1, unit = Unit.kPercentage)]
        [SettingsUISection(ParkingSection, StreetGroup)]
        [SettingsUIDisableByCondition(typeof(Setting), nameof(EnableForStreet), true)]
        public int MaxPriceDiscountStreet { get; set; }

        [SettingsUISection(ParkingSection, SettingsGroup)]
        public UpdateFreqEnum UpdateFreq { get; set; }

        public enum UpdateFreqEnum {
            Min45 = 32,
            Min22 = 64,
            Min90 = 16
        }

        [SettingsUIButton]
        [SettingsUISection(ParkingSection, SettingsGroup)]
        public bool ResetButton {
            set => SetDefaults();
        }
    }
}
