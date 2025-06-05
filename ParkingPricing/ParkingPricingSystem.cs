using System;
using System.Linq;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Net;
using Game.Policies;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace ParkingPricing
{
    public partial class ParkingPricingSystem : GameSystemBase
    {
        private EntityQuery policyQuery;
        private EntityQuery utilizationQuery;
        private EntityQuery m_ConfigQuery;
        private ComponentTypeHandle<District> m_DistrictType;
        private BufferTypeHandle<Policy> m_PolicyType;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ConfigQuery = GetEntityQuery(ComponentType.ReadOnly<UITransportConfigurationData>());
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // One day (or month) in-game is '262144' ticks
            return 262144 / (int)Mod.m_Setting.updateFreq;
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            if (m_ConfigQuery.IsEmptyIgnoreFilter) return;
        }

        protected override void OnUpdate()
        {
            LogUtil.Info("Updating parking pricing");

            if (Mod.m_Setting.enable_for_street)
            {
                UpdateStreetParking();
            }

            if (Mod.m_Setting.enable_for_lot)
            {
                UpdateBuildingParking();
            }

            LogUtil.Info("Done updating parking pricing");
        }

        private void UpdateStreetParking()
        {
            // DEV NOTES:
            // Each District has an array of `Game.Policies.Policy` but it seems to only return the active ones.
            // `m_Adjustment` is the parking fee.
            // Can we add a new one of these for the parking fee?
            // `Game.Prefabs.PrefabRef` seems to be how we access `Game.Prefabs.DistrictData`.
            // We probably need to do an additional query to get the utilization data.
            policyQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Areas.District>()
                .Build(this);
            RequireForUpdate(policyQuery);

            utilizationQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Net.ParkingLane>()
                .WithAll<Game.Common.Owner>()
                .Build(this);

            var results = policyQuery.ToArchetypeChunkArray(Allocator.Temp);
            LogUtil.Info($"Updating street parking: {results.Length} districts");

            var parkingResults = utilizationQuery.ToEntityArray(Allocator.Temp);

            int basePrice = Mod.m_Setting.standard_price_lot;
            double targetOcc = Mod.m_Setting.target_occupancy_lot / 100.0;
            double maxIncreasePct = Mod.m_Setting.max_price_increase_lot / 100.0;
            double maxDecreasePct = Mod.m_Setting.max_price_discount_lot / 100.0;
            int maxPrice = calcMaxPrice(basePrice, maxIncreasePct);
            int minPrice = calcMinPrice(basePrice, maxDecreasePct);

            foreach (var chunk in results)
            {
                // Game.Areas.District district;
                NativeArray<District> nativeArray = chunk.GetNativeArray(ref m_DistrictType);
                BufferAccessor<Policy> bufferAccessor = chunk.GetBufferAccessor(ref m_PolicyType);
                for (int i = 0; i < nativeArray.Length; i++)
                {
                    // We need to go from what could be either:
                    // ParkingLane -> Owner (road) -> Game.Areas.BorderDistrict -> District.
                    // OR
                    // GarageLane (which has m_VehicleCount and m_VehicleCapacity) -> Owner (which is the building)
                    // The GarageLane counts are clearly in number of vehicles.
                    // I have no idea what the floating point units for ParkingLane m_FreeSpace are.

                    // The ParkingLane Free Space is a float.
                    // var relevantParkingLanes = parkingResults.Where((Game.Net.ParkingLane lane) => lane.);
                    //
                }

                int newPrice = basePrice;
                if (maxPrice != minPrice)
                {
                    // Check occupancy data to determine new price
                    // TODO
                }

                // Apply pricing policy
                // TODO
            }
        }

        private void UpdateBuildingParking()
        {
            // DEV NOTES:
            // Each ParkingFacility has an array of `Game.Policies.Policy`. `m_Adjustment` is the parking fee.
            // There's also a `Game.Buildings.BuildingModifier`. I don't know if we need to update that?
            // There's a `Game.Vehicles.GuestVehicle` array. Is this the parked vehicles? Seems like it's always empty so maybe not?
            // This also has a `Game.Prefabs.PrefabRef`. That includes `Game.Prefabs.ParkingFacilityData` which has the capacity.
            // We probably need to do an additional query to get the utilization data.
            policyQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Buildings.ParkingFacility>()
                .Build(this);
            RequireForUpdate(policyQuery);

            var results = policyQuery.ToEntityArray(Allocator.Temp);
            LogUtil.Info($"Updating building parking: {results.Length} results");

            int basePrice = Mod.m_Setting.standard_price_lot;
            double targetOcc = Mod.m_Setting.target_occupancy_lot / 100.0;
            double maxIncreasePct = Mod.m_Setting.max_price_increase_lot / 100.0;
            double maxDecreasePct = Mod.m_Setting.max_price_discount_lot / 100.0;
            int maxPrice = calcMaxPrice(basePrice, maxIncreasePct);
            int minPrice = calcMinPrice(basePrice, maxDecreasePct);

            foreach (var result in results)
            {
                // Game.Net.GarageLane garageLane;
                Game.Buildings.ParkingFacility parkingFacility;
                // Game.Policies.Policy[] policy;

                // Here we need to go from ParkingLane -> Owner->Owner->Owner until we reach the building.


                // garageLane = EntityManager.GetComponentData<Game.Net.GarageLane>(result);
                parkingFacility = EntityManager.GetComponentData<Game.Buildings.ParkingFacility>(result);
                // policy = EntityManager.GetComponentData<Game.Policies.Policy>(result);

                int newPrice = basePrice;
                if (maxPrice != minPrice)
                {
                    // Check occupancy data to determine new price
                    // TODO
                }

                // Apply pricing policy
                // TODO
            }
        }

        private int calcMaxPrice(int basePrice, double maxIncreasePct)
        {
            return Math.Min(50,
                basePrice == 0 ?
                (int)Math.Round(maxIncreasePct * 10) :
                basePrice + (int)Math.Ceiling(basePrice * maxIncreasePct)
            );
        }

        private int calcMinPrice(int basePrice, double maxDecreasePct)
        {
            return Math.Max(0, basePrice == 0 ? 0 : (int)Math.Floor(basePrice * (1.0 - maxDecreasePct)));
        }
    }
}