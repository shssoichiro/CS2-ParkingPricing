using Colossal;
using System.Collections.Generic;

namespace ParkingPricing
{
    public class LocaleEN : IDictionarySource
    {
        private readonly ModSettings m_Setting;
        public LocaleEN(ModSettings setting)
        {
            m_Setting = setting;
        }
        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Parking Pricing" },
                { m_Setting.GetOptionTabLocaleID(ModSettings.ParkingSection), "Parking" },
                { m_Setting.GetOptionGroupLocaleID(ModSettings.LotGroup), "Parking Lots/Garages" },
                { m_Setting.GetOptionGroupLocaleID(ModSettings.StreetGroup), "Street Parking" },
                { m_Setting.GetOptionGroupLocaleID(ModSettings.SettingsGroup), "Settings" },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.enable_for_lot)), "Enable Parking Building Dynamic Pricing" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.enable_for_lot)), "Whether to have this mod update the prices of parking lots and buildings."},
                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.target_occupancy_lot)), "Target Occupancy" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.target_occupancy_lot)), "Desired target percentage of filled parking spots."},
                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.standard_price_lot)), "Standard Parking Price" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.standard_price_lot)), "Base price for parking when the occupancy is at the target."},
                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.max_price_increase_lot)), "Max. Price Increase" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.max_price_increase_lot)), "Max % of the base price to increase price by during high demand. If you want permanently free parking, you need to set both the standard price and the max price increase to 0. Otherwise this setting will increase the price above 0 during high-demand times."},
                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.max_price_discount_lot)), "Max. Price Discount" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.max_price_discount_lot)), "Max % of the base price to decrease price by during low demand. Setting this to 100 will result in parking being free during low-demand times."},

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.enable_for_street)), "Enable Street Parking Dynamic Pricing" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.enable_for_street)), "Whether to have this mod update the prices of street parking."},
                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.target_occupancy_street)), "Target Occupancy" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.target_occupancy_street)), "Desired target percentage of filled parking spots."},
                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.standard_price_street)), "Standard Parking Price" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.standard_price_street)), "Base price for parking when the occupancy is at the target."},
                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.max_price_increase_street)), "Max. Price Increase" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.max_price_increase_street)), "Max % of the base price to increase price by during high demand. If you want permanently free parking, you need to set both the standard price and the max price increase to 0. Otherwise this setting will increase the price above 0 during high-demand times."},
                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.max_price_discount_street)), "Max. Price Discount" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.max_price_discount_street)), "Max % of the base price to decrease price by during low demand. Setting this to 100 will result in parking being free during low-demand times."},

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.updateFreq)), "Update Frequency (In-game minutes)" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.updateFreq)), "How frequently Parking Pricing will evaluate each lot to update prices. Time is in in-game minutes. Note that if you are using the slow feature from the Realistic Trips mod, this time is based on the vanilla game, you should increase the update frequency for this mod."},

                { m_Setting.GetEnumValueLocaleID(ModSettings.UpdateFreqEnum.min22), "22" },
                { m_Setting.GetEnumValueLocaleID(ModSettings.UpdateFreqEnum.min45), "45" },
                { m_Setting.GetEnumValueLocaleID(ModSettings.UpdateFreqEnum.min90), "90" },

                { m_Setting.GetOptionLabelLocaleID(nameof(ModSettings.ResetButton)), "Reset Settings" },
                { m_Setting.GetOptionDescLocaleID(nameof(ModSettings.ResetButton)), $"Reset settings to default values" },

            };
        }

        public void Unload()
        {

        }
    }
}
