using System.Collections.Generic;
using Colossal;

namespace ParkingPricing {
    public class LocaleEN : IDictionarySource {
        private readonly ModSettings _setting;

        public LocaleEN(ModSettings setting) {
            _setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts
        ) {
            return new Dictionary<string, string> {
                { _setting.GetSettingsLocaleID(), "Parking Pricing" },
                { _setting.GetOptionTabLocaleID(ModSettings.ParkingSection), "Parking" },
                { _setting.GetOptionGroupLocaleID(ModSettings.LotGroup), "Parking Lots/Garages" },
                { _setting.GetOptionGroupLocaleID(ModSettings.StreetGroup), "Street Parking" },
                { _setting.GetOptionGroupLocaleID(ModSettings.SettingsGroup), "Settings" }, {
                    _setting.GetOptionLabelLocaleID(nameof(ModSettings.EnableForLot)),
                    "Enable Parking Building Dynamic Pricing"
                }, {
                    _setting.GetOptionDescLocaleID(nameof(ModSettings.EnableForLot)),
                    "Whether to have this mod update the prices of parking lots and buildings."
                },
                { _setting.GetOptionLabelLocaleID(nameof(ModSettings.StandardPriceLot)), "Standard Parking Price" }, {
                    _setting.GetOptionDescLocaleID(nameof(ModSettings.StandardPriceLot)),
                    "Base price for parking when the occupancy is at the target."
                },
                { _setting.GetOptionLabelLocaleID(nameof(ModSettings.MaxPriceIncreaseLot)), "Max. Price Increase" }, {
                    _setting.GetOptionDescLocaleID(nameof(ModSettings.MaxPriceIncreaseLot)),
                    "Max % of the base price to increase price by during high demand. If you want permanently free parking, you need to set both the standard price and the max price increase to 0. Otherwise this setting will increase the price above 0 during high-demand times."
                },
                { _setting.GetOptionLabelLocaleID(nameof(ModSettings.MaxPriceDiscountLot)), "Max. Price Discount" }, {
                    _setting.GetOptionDescLocaleID(nameof(ModSettings.MaxPriceDiscountLot)),
                    "Max % of the base price to decrease price by during low demand. Setting this to 100 will result in parking being free during low-demand times."
                }, {
                    _setting.GetOptionLabelLocaleID(nameof(ModSettings.EnableForStreet)),
                    "Enable Street Parking Dynamic Pricing"
                }, {
                    _setting.GetOptionDescLocaleID(nameof(ModSettings.EnableForStreet)),
                    "Whether to have this mod update the prices of street parking."
                },
                { _setting.GetOptionLabelLocaleID(nameof(ModSettings.StandardPriceStreet)), "Standard Parking Price" }, {
                    _setting.GetOptionDescLocaleID(nameof(ModSettings.StandardPriceStreet)),
                    "Base price for parking when the occupancy is at the target."
                },
                { _setting.GetOptionLabelLocaleID(nameof(ModSettings.MaxPriceIncreaseStreet)), "Max. Price Increase" }, {
                    _setting.GetOptionDescLocaleID(nameof(ModSettings.MaxPriceIncreaseStreet)),
                    "Max % of the base price to increase price by during high demand. If you want permanently free parking, you need to set both the standard price and the max price increase to 0. Otherwise this setting will increase the price above 0 during high-demand times."
                },
                { _setting.GetOptionLabelLocaleID(nameof(ModSettings.MaxPriceDiscountStreet)), "Max. Price Discount" }, {
                    _setting.GetOptionDescLocaleID(nameof(ModSettings.MaxPriceDiscountStreet)),
                    "Max % of the base price to decrease price by during low demand. Setting this to 100 will result in parking being free during low-demand times."
                }, {
                    _setting.GetOptionLabelLocaleID(nameof(ModSettings.UpdateFreq)),
                    "Update Frequency (In-game minutes)"
                }, {
                    _setting.GetOptionDescLocaleID(nameof(ModSettings.UpdateFreq)),
                    "How frequently Parking Pricing will evaluate each lot to update prices. Time is in in-game minutes. Note that if you are using the slow feature from the Realistic Trips mod, this time is based on the vanilla game, you should increase the update frequency for this mod."
                },
                { _setting.GetEnumValueLocaleID(ModSettings.UpdateFreqEnum.Min22), "22" },
                { _setting.GetEnumValueLocaleID(ModSettings.UpdateFreqEnum.Min45), "45" },
                { _setting.GetEnumValueLocaleID(ModSettings.UpdateFreqEnum.Min90), "90" },
                { _setting.GetOptionLabelLocaleID(nameof(ModSettings.ResetButton)), "Reset Settings" },
                { _setting.GetOptionDescLocaleID(nameof(ModSettings.ResetButton)), $"Reset settings to default values" }
            };
        }

        public void Unload() {
        }
    }
}
