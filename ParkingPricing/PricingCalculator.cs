using System;

namespace ParkingPricing
{
    // Extracted price calculation logic following SRP
    public static class PricingCalculator
    {
        public static int CalculateAdjustedPrice(int basePrice, int maxPrice, int minPrice, double utilization)
        {
            if (utilization < ParkingPricingConstants.LOW_UTILIZATION_THRESHOLD)
            {
                return minPrice;
            }

            if (utilization > ParkingPricingConstants.HIGH_UTILIZATION_THRESHOLD)
            {
                return maxPrice;
            }

            // For utilization between 0.2 and 0.8, scale based on distance from 0.5
            if (utilization <= ParkingPricingConstants.TARGET_UTILIZATION)
            {
                // Interpolate between min price (at 0.2) and base price (at 0.5)
                double factor = (utilization - ParkingPricingConstants.LOW_UTILIZATION_THRESHOLD) /
                               (ParkingPricingConstants.TARGET_UTILIZATION - ParkingPricingConstants.LOW_UTILIZATION_THRESHOLD);
                return (int)Math.Round(minPrice + factor * (basePrice - minPrice));
            }
            else
            {
                // Interpolate between base price (at 0.5) and max price (at 0.8)
                double factor = (utilization - ParkingPricingConstants.TARGET_UTILIZATION) /
                               (ParkingPricingConstants.HIGH_UTILIZATION_THRESHOLD - ParkingPricingConstants.TARGET_UTILIZATION);
                return (int)Math.Round(basePrice + factor * (maxPrice - basePrice));
            }
        }

        public static int CalculateMaxPrice(int basePrice, double maxIncreasePct)
        {
            return Math.Min(ParkingPricingConstants.ABSOLUTE_MAX_PRICE,
                basePrice == 0 ?
                (int)Math.Round(maxIncreasePct * 10) :
                basePrice + (int)Math.Ceiling(basePrice * maxIncreasePct)
            );
        }

        public static int CalculateMinPrice(int basePrice, double maxDecreasePct)
        {
            return Math.Max(0, basePrice == 0 ? 0 : (int)Math.Floor(basePrice * (1.0 - maxDecreasePct)));
        }
    }
}