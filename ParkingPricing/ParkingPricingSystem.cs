using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Net;
using Game.Prefabs;
using Unity.Collections;
using Unity.Entities;

namespace ParkingPricing
{
    public partial class ParkingPricingSystem : GameSystemBase
    {
        private EntityQuery _query;
        private EntityQuery m_ConfigQuery;

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
            _query = GetEntityQuery(new EntityQueryDesc()
            {
                All = new[] {
                    ComponentType.ReadWrite<Game.Net.ParkingLane>(),
                    ComponentType.ReadWrite<Game.Areas.District>(),
                }
            });
            RequireForUpdate(_query);

            var results = _query.ToEntityArray(Allocator.Temp);
            LogUtil.Info($"Updating street parking: {results.Length} results");

            int basePrice = Mod.m_Setting.standard_price_lot;
            double targetOcc = Mod.m_Setting.target_occupancy_lot / 100.0;
            double maxIncreasePct = Mod.m_Setting.max_price_increase_lot / 100.0;
            double maxDecreasePct = Mod.m_Setting.max_price_discount_lot / 100.0;
            int maxPrice = calcMaxPrice(basePrice, maxIncreasePct);
            int minPrice = calcMinPrice(basePrice, maxDecreasePct);

            foreach (var result in results)
            {
                Game.Net.ParkingLane parkingLane;
                Game.Areas.District district;

                parkingLane = EntityManager.GetComponentData<Game.Net.ParkingLane>(result);
                district = EntityManager.GetComponentData<Game.Areas.District>(result);

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
            _query = GetEntityQuery(new EntityQueryDesc()
            {
                All = new[] {
                    ComponentType.ReadWrite<Game.Net.GarageLane>(),
                    ComponentType.ReadOnly<Game.Buildings.ParkingFacility>(),
                    ComponentType.ReadOnly<Game.Buildings.Building>(),
                }
            });
            RequireForUpdate(_query);

            var results = _query.ToEntityArray(Allocator.Temp);
            LogUtil.Info($"Updating street parking: {results.Length} results");

            int basePrice = Mod.m_Setting.standard_price_lot;
            double targetOcc = Mod.m_Setting.target_occupancy_lot / 100.0;
            double maxIncreasePct = Mod.m_Setting.max_price_increase_lot / 100.0;
            double maxDecreasePct = Mod.m_Setting.max_price_discount_lot / 100.0;
            int maxPrice = calcMaxPrice(basePrice, maxIncreasePct);
            int minPrice = calcMinPrice(basePrice, maxDecreasePct);

            foreach (var result in results)
            {
                Game.Net.GarageLane garageLane;
                Game.Buildings.ParkingFacility parkingFacility;
                Game.Buildings.Building building;

                garageLane = EntityManager.GetComponentData<Game.Net.GarageLane>(result);
                parkingFacility = EntityManager.GetComponentData<Game.Buildings.ParkingFacility>(result);
                building = EntityManager.GetComponentData<Game.Buildings.Building>(result);

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