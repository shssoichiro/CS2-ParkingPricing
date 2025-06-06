using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Net;
using Game.Policies;
using Game.Prefabs;
using Game.Buildings;
using Unity.Collections;
using Unity.Entities;
using Game.Vehicles;

namespace ParkingPricing
{
    public partial class ParkingPricingSystem : GameSystemBase
    {
        private EntityQuery policyQuery;
        private EntityQuery utilizationQuery;
        private EntityQuery garageQuery;
        private EntityQuery m_ConfigQuery;
        private EntityQuery m_PolicyPrefabQuery;
        private ComponentTypeHandle<District> m_DistrictType;
        private ComponentTypeHandle<Game.Buildings.ParkingFacility> m_parkingFacility;
        private ComponentTypeHandle<Game.Buildings.Building> m_BuildingType;
        private ComponentTypeHandle<Game.Net.ParkingLane> m_ParkingLaneType;
        private ComponentTypeHandle<Game.Net.GarageLane> m_GarageLaneType;
        private ComponentTypeHandle<Game.Common.Owner> m_OwnerType;
        private BufferTypeHandle<Policy> m_PolicyType;
        private EntityStorageInfoLookup m_EntityLookup;

        // Cached policy prefab entities
        private Entity m_LotParkingFeePrefab;
        private Entity m_StreetParkingFeePrefab;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ConfigQuery = GetEntityQuery(ComponentType.ReadOnly<UITransportConfigurationData>());

            // Initialize policy prefab query  
            m_PolicyPrefabQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            // Initialize component type handles
            m_DistrictType = GetComponentTypeHandle<District>(true);
            m_parkingFacility = GetComponentTypeHandle<Game.Buildings.ParkingFacility>(true);
            m_BuildingType = GetComponentTypeHandle<Game.Buildings.Building>(true);
            m_ParkingLaneType = GetComponentTypeHandle<Game.Net.ParkingLane>(true);
            m_GarageLaneType = GetComponentTypeHandle<Game.Net.GarageLane>(true);
            m_OwnerType = GetComponentTypeHandle<Game.Common.Owner>(true);
            m_PolicyType = GetBufferTypeHandle<Policy>(false);
            m_EntityLookup = GetEntityStorageInfoLookup();

            // Initialize cached prefab entities as null
            m_LotParkingFeePrefab = Entity.Null;
            m_StreetParkingFeePrefab = Entity.Null;
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // One day (or month) in-game is '262144' ticks
            return 262144 / (int)Mod.m_Setting.updateFreq;
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            if (m_ConfigQuery.IsEmptyIgnoreFilter) return;

            // Initialize policy prefabs after game loads
            InitializePolicyPrefabs();
        }

        private void InitializePolicyPrefabs()
        {
            // Only initialize once
            if (m_LotParkingFeePrefab != Entity.Null) return;

            var policyPrefabs = m_PolicyPrefabQuery.ToEntityArray(Allocator.Temp);

            foreach (var prefabEntity in policyPrefabs)
            {
                // Look for BuildingOptionData or DistrictOptionData to identify policy types
                if (EntityManager.HasComponent<BuildingOptionData>(prefabEntity))
                {
                    var buildingOptionData = EntityManager.GetComponentData<BuildingOptionData>(prefabEntity);
                    if (BuildingUtils.HasOption(buildingOptionData, BuildingOption.PaidParking))
                    {
                        m_LotParkingFeePrefab = prefabEntity;
                        LogUtil.Info($"Found Lot Parking Fee prefab: {prefabEntity.Index}");
                    }
                }
                else if (EntityManager.HasComponent<DistrictOptionData>(prefabEntity))
                {
                    var districtOptionData = EntityManager.GetComponentData<DistrictOptionData>(prefabEntity);
                    if (AreaUtils.HasOption(districtOptionData, DistrictOption.PaidParking))
                    {
                        m_StreetParkingFeePrefab = prefabEntity;
                        LogUtil.Info($"Found Street Parking Fee prefab: {prefabEntity.Index}");
                    }
                }
            }

            policyPrefabs.Dispose();

            if (m_LotParkingFeePrefab == Entity.Null)
            {
                LogUtil.Warn("Could not find 'Lot Parking Fee' policy prefab");
            }
            if (m_StreetParkingFeePrefab == Entity.Null)
            {
                LogUtil.Warn("Could not find 'Roadside Parking Fee' policy prefab");
            }
        }

        protected override void OnUpdate()
        {
            LogUtil.Info("Updating parking pricing");

            // Update component type handles each frame
            m_DistrictType.Update(this);
            m_parkingFacility.Update(this);
            m_BuildingType.Update(this);
            m_ParkingLaneType.Update(this);
            m_GarageLaneType.Update(this);
            m_OwnerType.Update(this);
            m_PolicyType.Update(this);
            m_EntityLookup.Update(this);

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
            // Query for districts
            policyQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Areas.District>()
                .Build(this);
            RequireForUpdate(policyQuery);

            // Query for parking lanes on streets
            utilizationQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Net.ParkingLane>()
                .WithAll<Game.Common.Owner>()
                .Build(this);

            var districtChunks = policyQuery.ToArchetypeChunkArray(Allocator.Temp);
            var parkingLanes = utilizationQuery.ToEntityArray(Allocator.Temp);

            LogUtil.Info($"Updating street parking: {districtChunks.Length} district chunks, {parkingLanes.Length} parking lanes");

            int basePrice = Mod.m_Setting.standard_price_street;
            double maxIncreasePct = Mod.m_Setting.max_price_increase_street / 100.0;
            double maxDecreasePct = Mod.m_Setting.max_price_discount_street / 100.0;
            int maxPrice = calcMaxPrice(basePrice, maxIncreasePct);
            int minPrice = calcMinPrice(basePrice, maxDecreasePct);

            // Iterate through district chunks
            foreach (var chunk in districtChunks)
            {
                var districtEntities = chunk.GetNativeArray(EntityManager.GetEntityTypeHandle());

                for (int i = 0; i < districtEntities.Length; i++)
                {
                    Entity districtEntity = districtEntities[i];

                    // Calculate utilization and price change for this district
                    double utilization = CalculateDistrictUtilization(districtEntity, parkingLanes);
                    int newPrice = CalculateAdjustedPrice(basePrice, maxPrice, minPrice, utilization);
                    LogUtil.Info($"District {districtEntity.Index}: Utilization = {utilization:P2}, New Price = {newPrice}");

                    // Apply the parking fee policy to this district
                    ApplyDistrictParkingPolicy(districtEntity, newPrice);
                }
            }

            // Dispose temporary allocations
            districtChunks.Dispose();
            parkingLanes.Dispose();
        }

        private void UpdateBuildingParking()
        {
            // Query for buildings that are parking facilities
            // Unfortunately buildings such as train stations don't have ParkingFacility, so we just need to query all buildings and filter them out for ones with paid parking.
            policyQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Buildings.Building>()
                .Build(this);
            RequireForUpdate(policyQuery);

            // Query for parking lanes (surface parking)
            utilizationQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Net.ParkingLane>()
                .WithAll<Game.Common.Owner>()
                .Build(this);

            // Query for garage lanes (garage parking)
            garageQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<Game.Net.GarageLane>()
                .WithAll<Game.Common.Owner>()
                .Build(this);

            var buildingChunks = policyQuery.ToArchetypeChunkArray(Allocator.Temp);
            var parkingLanes = utilizationQuery.ToEntityArray(Allocator.Temp);
            var garageLanes = garageQuery.ToEntityArray(Allocator.Temp);

            LogUtil.Info($"Updating building parking: {buildingChunks.Length} building chunks, {parkingLanes.Length} parking lanes, {garageLanes.Length} garage lanes");

            int basePrice = Mod.m_Setting.standard_price_lot;
            double maxIncreasePct = Mod.m_Setting.max_price_increase_lot / 100.0;
            double maxDecreasePct = Mod.m_Setting.max_price_discount_lot / 100.0;
            int maxPrice = calcMaxPrice(basePrice, maxIncreasePct);
            int minPrice = calcMinPrice(basePrice, maxDecreasePct);

            // Iterate through building chunks
            foreach (var chunk in buildingChunks)
            {
                var buildingEntities = chunk.GetNativeArray(EntityManager.GetEntityTypeHandle());

                for (int i = 0; i < buildingEntities.Length; i++)
                {
                    Entity buildingEntity = buildingEntities[i];

                    // Only evaluate buildings that can have parking fee.
                    Policy policy;
                    if (!TryGetPolicy(buildingEntity, m_LotParkingFeePrefab, out policy))
                    {
                        continue;
                    }

                    // Calculate utilization and price change for this building
                    double utilization = CalculateBuildingUtilization(buildingEntity, parkingLanes, garageLanes);
                    int newPrice = CalculateAdjustedPrice(basePrice, maxPrice, minPrice, utilization);
                    LogUtil.Info($"Building {buildingEntity.Index}: Utilization = {utilization:P2}, New Price = {newPrice}");

                    // Apply the parking fee policy to this building
                    ApplyLotParkingPolicy(buildingEntity, newPrice);
                }
            }

            // Dispose temporary allocations
            buildingChunks.Dispose();
            parkingLanes.Dispose();
            garageLanes.Dispose();
        }

        private int CalculateAdjustedPrice(int basePrice, int maxPrice, int minPrice, double utilization)
        {
            // If utilization is below 0.2, use min price
            if (utilization < 0.2)
            {
                return minPrice;
            }

            // If utilization is above 0.8, use max price
            if (utilization > 0.8)
            {
                return maxPrice;
            }

            // For utilization between 0.2 and 0.8, scale based on distance from 0.5
            if (utilization <= 0.5)
            {
                // Interpolate between min price (at 0.2) and base price (at 0.5)
                double factor = (utilization - 0.2) / (0.5 - 0.2); // Factor ranges from 0 to 1
                return (int)Math.Round(minPrice + factor * (basePrice - minPrice));
            }
            else
            {
                // Interpolate between base price (at 0.5) and max price (at 0.8)
                double factor = (utilization - 0.5) / (0.8 - 0.5); // Factor ranges from 0 to 1
                return (int)Math.Round(basePrice + factor * (maxPrice - basePrice));
            }
        }

        private double CalculateBuildingUtilization(Entity buildingEntity, NativeArray<Entity> parkingLanes, NativeArray<Entity> garageLanes)
        {
            int slotCapacity = 0;
            int parkedCars = 0;

            // Check if building has SubLane buffer (more direct approach like the game uses)
            if (EntityManager.HasBuffer<Game.Net.SubLane>(buildingEntity))
            {
                var subLanes = EntityManager.GetBuffer<Game.Net.SubLane>(buildingEntity, true);
                CheckParkingLanes(subLanes, ref slotCapacity, ref parkedCars);
            }

            // Calculate utilization percentage
            return slotCapacity > 0 ? (double)parkedCars / slotCapacity : 0.0;
        }

        private void CheckParkingLanes(DynamicBuffer<Game.Net.SubLane> subLanes, ref int slotCapacity, ref int parkedCars)
        {
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity subLane = subLanes[i].m_SubLane;

                // Handle ParkingLane
                if (EntityManager.HasComponent<Game.Net.ParkingLane>(subLane))
                {
                    var parkingLane = EntityManager.GetComponentData<Game.Net.ParkingLane>(subLane);

                    // Skip virtual lanes
                    if ((parkingLane.m_Flags & ParkingLaneFlags.VirtualLane) != 0)
                    {
                        continue;
                    }

                    GetParkingLaneCounts(subLane, parkingLane, ref slotCapacity, ref parkedCars);
                }
                // Handle GarageLane
                else if (EntityManager.HasComponent<Game.Net.GarageLane>(subLane))
                {
                    var garageLane = EntityManager.GetComponentData<Game.Net.GarageLane>(subLane);
                    slotCapacity += garageLane.m_VehicleCapacity;
                    parkedCars += garageLane.m_VehicleCount;
                }
            }
        }

        private void GetParkingLaneCounts(Entity subLane, Game.Net.ParkingLane parkingLane, ref int slotCapacity, ref int parkedCars)
        {
            // Get parking slot count using game's method
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(subLane).m_Prefab;
            Curve curve = EntityManager.GetComponentData<Curve>(subLane);
            var parkingLaneData = EntityManager.GetComponentData<ParkingLaneData>(prefab);

            if (parkingLaneData.m_SlotInterval != 0f)
            {
                int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
                slotCapacity += parkingSlotCount;
            }

            // Count parked cars in this lane
            if (EntityManager.HasBuffer<LaneObject>(subLane))
            {
                var laneObjects = EntityManager.GetBuffer<LaneObject>(subLane, true);
                for (int j = 0; j < laneObjects.Length; j++)
                {
                    if (EntityManager.HasComponent<ParkedCar>(laneObjects[j].m_LaneObject))
                    {
                        parkedCars++;
                    }
                }
            }
            LogUtil.Debug($"Found parking lane: SlotInterval={parkingLaneData.m_SlotInterval:P2}, SlotCapacity={slotCapacity}, ParkedCars={parkedCars}");
        }

        private double CalculateDistrictUtilization(Entity districtEntity, NativeArray<Entity> parkingLanes)
        {
            double totalCapacity = 0;
            double totalOccupied = 0;

            // Check all parking lanes to see which belong to this district
            foreach (Entity laneEntity in parkingLanes)
            {
                if (EntityManager.HasComponent<Game.Net.ParkingLane>(laneEntity) &&
                    EntityManager.HasComponent<Game.Common.Owner>(laneEntity))
                {
                    var parkingLane = EntityManager.GetComponentData<Game.Net.ParkingLane>(laneEntity);

                    // Skip virtual lanes
                    if ((parkingLane.m_Flags & ParkingLaneFlags.VirtualLane) != 0)
                    {
                        continue;
                    }

                    var owner = EntityManager.GetComponentData<Game.Common.Owner>(laneEntity);
                    Entity roadEntity = owner.m_Owner;

                    // Check if the road has a BorderDistrict component
                    if (EntityManager.HasComponent<Game.Areas.BorderDistrict>(roadEntity))
                    {
                        var borderDistrict = EntityManager.GetComponentData<Game.Areas.BorderDistrict>(roadEntity);

                        bool leftMatch = borderDistrict.m_Left == districtEntity;
                        bool rightMatch = borderDistrict.m_Right == districtEntity;

                        // Only process this lane if it belongs to our district
                        if (leftMatch || rightMatch)
                        {
                            // Calculate capacity and occupancy for this lane
                            int laneCapacity = 0;
                            int laneOccupied = 0;

                            // Get parking slot count using game's method
                            GetParkingLaneCounts(laneEntity, parkingLane, ref laneCapacity, ref laneOccupied);

                            // Determine weight based on district ownership
                            double weight;
                            if (leftMatch && rightMatch)
                            {
                                weight = 1.0; // Full capacity/occupancy if both sides belong to district
                            }
                            else
                            {
                                weight = 0.5; // Half capacity/occupancy if only one side belongs to district
                            }

                            totalCapacity += laneCapacity * weight;
                            totalOccupied += laneOccupied * weight;
                        }
                    }
                }
            }

            // Calculate utilization percentage
            return totalCapacity > 0 ? totalOccupied / totalCapacity : 0.0;
        }

        private void ApplyDistrictParkingPolicy(Entity districtEntity, int newPrice)
        {
            if (!TryUpdatePolicy(districtEntity, m_StreetParkingFeePrefab, newPrice))
            {
                LogUtil.Warn("Failed to find street parking fee policy");
                return;
            }

            LogUtil.Info($"Updated street parking policy for district {districtEntity.Index}: ${newPrice}");
        }

        private void ApplyLotParkingPolicy(Entity buildingEntity, int newPrice)
        {
            if (!TryUpdatePolicy(buildingEntity, m_LotParkingFeePrefab, newPrice))
            {
                LogUtil.Warn("Failed to find lot parking fee policy");
                return;
            }

            LogUtil.Info($"Updated parking policy for building {buildingEntity.Index}: ${newPrice}");
        }

        private bool TryGetPolicy(Entity entity, Entity policyType, out Policy policy)
        {
            var buffer = base.EntityManager.GetBuffer<Policy>(entity);
            for (var index = 0; index < buffer.Length; index++)
            {
                if (buffer[index].m_Policy == policyType)
                {
                    policy = buffer[index];
                    return true;
                }
            }
            policy = default(Policy);
            return false;
        }

        private bool TryUpdatePolicy(Entity entity, Entity policyType, int newPrice)
        {
            var buffer = base.EntityManager.GetBuffer<Policy>(entity);
            for (var index = 0; index < buffer.Length; index++)
            {
                if (buffer[index].m_Policy == policyType)
                {
                    var policy = buffer[index];
                    policy.m_Adjustment = newPrice;
                    policy.m_Flags = newPrice > 0 ? PolicyFlags.Active : 0;
                    buffer[index] = policy;
                    return true;
                }
            }
            return false;
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