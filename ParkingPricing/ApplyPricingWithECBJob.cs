using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace ParkingPricing {
    // Job for applying price updates to entities using EntityCommandBuffer for immediate application
    [BurstCompile]
    public struct ApplyPricingWithECBJob : IJob {
        [ReadOnly] public NativeList<DistrictUtilizationResult> DistrictResults;
        [ReadOnly] public NativeList<BuildingUtilizationResult> BuildingResults;
        [ReadOnly] public int BaseStreetPrice;
        [ReadOnly] public int MaxStreetPrice;
        [ReadOnly] public int MinStreetPrice;
        [ReadOnly] public int BaseLotPrice;
        [ReadOnly] public int MaxLotPrice;
        [ReadOnly] public int MinLotPrice;
        [ReadOnly] public Entity StreetParkingFeePrefab;
        [ReadOnly] public Entity LotParkingFeePrefab;

        public EntityCommandBuffer EntityCommandBuffer;

        public void Execute() {
            // Process district results
            foreach (DistrictUtilizationResult result in DistrictResults) {
                int newPrice = PricingCalculator.CalculateAdjustedPrice(
                    BaseStreetPrice, MaxStreetPrice, MinStreetPrice, result.Utilization
                );

                // Use ECB to schedule policy update
                EntityCommandBuffer.AddComponent(
                    result.DistrictEntity,
                    new PolicyUpdateCommand {
                        PolicyPrefab = StreetParkingFeePrefab,
                        NewPrice = newPrice,
                        IsDistrict = true,
                        Utilization = result.Utilization
                    }
                );
            }

            DistrictResults.Clear();

            // Process building results
            foreach (BuildingUtilizationResult result in BuildingResults) {
                int newPrice = PricingCalculator.CalculateAdjustedPrice(
                    BaseLotPrice, MaxLotPrice, MinLotPrice, result.Utilization
                );

                // Use ECB to schedule policy update
                EntityCommandBuffer.AddComponent(
                    result.BuildingEntity,
                    new PolicyUpdateCommand {
                        PolicyPrefab = LotParkingFeePrefab,
                        NewPrice = newPrice,
                        IsDistrict = false,
                        Utilization = result.Utilization
                    }
                );
            }

            BuildingResults.Clear();
        }
    }
}
