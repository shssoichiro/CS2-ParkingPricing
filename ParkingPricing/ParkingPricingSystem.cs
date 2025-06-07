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
using Game.Objects;

namespace ParkingPricing
{
    // Main system class now focused on coordination and ECS management
    public partial class ParkingPricingSystem : GameSystemBase
    {
        // Entity queries - now properly cached
        private EntityQuery m_DistrictQuery;
        private EntityQuery m_BuildingQuery;
        private EntityQuery m_ParkingLaneQuery;
        private EntityQuery m_GarageLaneQuery;
        private EntityQuery m_ConfigQuery;
        private EntityQuery m_PolicyPrefabQuery;

        // Component type handles
        private ComponentTypeHandle<District> m_DistrictType;
        private ComponentTypeHandle<Game.Buildings.ParkingFacility> m_parkingFacility;
        private ComponentTypeHandle<Game.Buildings.Building> m_BuildingType;
        private ComponentTypeHandle<Game.Net.ParkingLane> m_ParkingLaneType;
        private ComponentTypeHandle<Game.Net.GarageLane> m_GarageLaneType;
        private ComponentTypeHandle<Game.Common.Owner> m_OwnerType;
        private BufferTypeHandle<Policy> m_PolicyType;
        private EntityStorageInfoLookup m_EntityLookup;

        // Lookup components
        [ReadOnly] private BufferLookup<Game.Net.SubLane> m_SubLanes;
        private ComponentLookup<ParkedCar> m_ParkedCarData;
        [ReadOnly] private ComponentLookup<Unspawned> m_UnspawnedData;
        [ReadOnly] private ComponentLookup<PrefabRef> m_PrefabRefData;
        [ReadOnly] private ComponentLookup<ObjectGeometryData> m_ObjectGeometryData;
        [ReadOnly] private ComponentLookup<Lane> m_LaneType;
        [ReadOnly] private BufferLookup<LaneObject> m_LaneObjectType;
        [ReadOnly] private BufferLookup<LaneOverlap> m_LaneOverlapType;
        [ReadOnly] private ComponentLookup<Lane> m_LaneData;
        [ReadOnly] private ComponentLookup<Game.Net.CarLane> m_CarLaneData;

        // Cached policy prefab entities
        private Entity m_LotParkingFeePrefab;
        private Entity m_StreetParkingFeePrefab;

        // Extracted components
        private PolicyManager m_PolicyManager;
        private UtilizationCalculator m_UtilizationCalculator;

        protected override void OnCreate()
        {
            base.OnCreate();

            InitializeQueries();
            InitializeComponentHandles();
            InitializeLookupComponents();

            // Initialize cached prefab entities
            m_LotParkingFeePrefab = Entity.Null;
            m_StreetParkingFeePrefab = Entity.Null;

            // Initialize extracted components
            m_PolicyManager = new PolicyManager(EntityManager);
        }

        private void InitializeQueries()
        {
            m_ConfigQuery = GetEntityQuery(ComponentType.ReadOnly<UITransportConfigurationData>());
            m_PolicyPrefabQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            // Cache entity queries to avoid recreating them every frame
            m_DistrictQuery = new EntityQueryBuilder(Allocator.Persistent)
                .WithAll<Game.Areas.District>()
                .Build(this);

            m_BuildingQuery = new EntityQueryBuilder(Allocator.Persistent)
                .WithAll<Game.Buildings.Building>()
                .Build(this);

            m_ParkingLaneQuery = new EntityQueryBuilder(Allocator.Persistent)
                .WithAll<Game.Net.ParkingLane>()
                .WithAll<Game.Common.Owner>()
                .WithAll<LaneObject>()
                .WithAll<LaneOverlap>()
                .WithAll<Lane>()
                .Build(this);

            m_GarageLaneQuery = new EntityQueryBuilder(Allocator.Persistent)
                .WithAll<Game.Net.GarageLane>()
                .WithAll<Game.Common.Owner>()
                .Build(this);
        }

        private void InitializeComponentHandles()
        {
            m_DistrictType = GetComponentTypeHandle<District>(true);
            m_parkingFacility = GetComponentTypeHandle<Game.Buildings.ParkingFacility>(true);
            m_BuildingType = GetComponentTypeHandle<Game.Buildings.Building>(true);
            m_ParkingLaneType = GetComponentTypeHandle<Game.Net.ParkingLane>(true);
            m_GarageLaneType = GetComponentTypeHandle<Game.Net.GarageLane>(true);
            m_OwnerType = GetComponentTypeHandle<Game.Common.Owner>(true);
            m_PolicyType = GetBufferTypeHandle<Policy>(false);
            m_EntityLookup = GetEntityStorageInfoLookup();
        }

        private void InitializeLookupComponents()
        {
            m_SubLanes = GetBufferLookup<Game.Net.SubLane>(true);
            m_ParkedCarData = GetComponentLookup<ParkedCar>(false);
            m_UnspawnedData = GetComponentLookup<Unspawned>(true);
            m_PrefabRefData = GetComponentLookup<PrefabRef>(true);
            m_ObjectGeometryData = GetComponentLookup<ObjectGeometryData>(true);
            m_LaneType = GetComponentLookup<Lane>(true);
            m_LaneObjectType = GetBufferLookup<LaneObject>(true);
            m_LaneOverlapType = GetBufferLookup<LaneOverlap>(true);
            m_LaneData = GetComponentLookup<Lane>(true);
            m_CarLaneData = GetComponentLookup<Game.Net.CarLane>(true);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            if (Mod.m_Setting == null)
                return ParkingPricingConstants.GAME_TICKS_PER_DAY;

            return ParkingPricingConstants.GAME_TICKS_PER_DAY / (int)Mod.m_Setting.updateFreq;
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            if (m_ConfigQuery.IsEmptyIgnoreFilter) return;
            InitializePolicyPrefabs();

            // Initialize utilization calculator after game loads and prefabs are ready
            m_UtilizationCalculator = new UtilizationCalculator(EntityManager,
                m_SubLanes, m_ParkedCarData, m_UnspawnedData, m_PrefabRefData,
                m_ObjectGeometryData, m_LaneType, m_LaneObjectType, m_LaneOverlapType,
                m_LaneData, m_CarLaneData);
        }

        private void InitializePolicyPrefabs()
        {
            // Only initialize once
            if (m_LotParkingFeePrefab != Entity.Null) return;

            var policyPrefabs = m_PolicyPrefabQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var prefabEntity in policyPrefabs)
                {
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

                if (m_LotParkingFeePrefab == Entity.Null)
                {
                    LogUtil.Warn("Could not find 'Lot Parking Fee' policy prefab");
                }
                if (m_StreetParkingFeePrefab == Entity.Null)
                {
                    LogUtil.Warn("Could not find 'Roadside Parking Fee' policy prefab");
                }
            }
            finally
            {
                policyPrefabs.Dispose();
            }
        }

        protected override void OnUpdate()
        {
            if (Mod.m_Setting == null)
            {
                LogUtil.Warn("Mod settings not initialized, skipping update");
                return;
            }

            LogUtil.Info("Updating parking pricing");

            try
            {
                UpdateComponentHandles();

                if (Mod.m_Setting.enable_for_street)
                {
                    UpdateStreetParking();
                }

                if (Mod.m_Setting.enable_for_lot)
                {
                    UpdateBuildingParking();
                }
            }
            catch (Exception ex)
            {
                LogUtil.Exception(ex);
            }

            LogUtil.Info("Done updating parking pricing");
        }

        private void UpdateComponentHandles()
        {
            // Only update handles that are actually used
            m_DistrictType.Update(this);
            m_BuildingType.Update(this);
            m_ParkingLaneType.Update(this);
            m_GarageLaneType.Update(this);
            m_OwnerType.Update(this);
            m_PolicyType.Update(this);
            m_EntityLookup.Update(this);

            // Update lookup components
            m_SubLanes.Update(this);
            m_ParkedCarData.Update(this);
            m_UnspawnedData.Update(this);
            m_PrefabRefData.Update(this);
            m_ObjectGeometryData.Update(this);
            m_LaneType.Update(this);
            m_LaneObjectType.Update(this);
            m_LaneOverlapType.Update(this);
            m_LaneData.Update(this);
            m_CarLaneData.Update(this);
        }

        private void UpdateStreetParking()
        {
            if (m_UtilizationCalculator == null)
            {
                LogUtil.Warn("UtilizationCalculator not initialized");
                return;
            }

            RequireForUpdate(m_DistrictQuery);

            var districtChunks = m_DistrictQuery.ToArchetypeChunkArray(Allocator.Temp);
            var parkingLanes = m_ParkingLaneQuery.ToEntityArray(Allocator.Temp);

            try
            {
                LogUtil.Info($"Updating street parking: {districtChunks.Length} district chunks, {parkingLanes.Length} parking lanes");

                var settings = Mod.m_Setting;
                int basePrice = settings.standard_price_street;
                double maxIncreasePct = settings.max_price_increase_street / 100.0;
                double maxDecreasePct = settings.max_price_discount_street / 100.0;
                int maxPrice = PricingCalculator.CalculateMaxPrice(basePrice, maxIncreasePct);
                int minPrice = PricingCalculator.CalculateMinPrice(basePrice, maxDecreasePct);

                ProcessDistrictChunks(districtChunks, parkingLanes, basePrice, maxPrice, minPrice);
            }
            finally
            {
                districtChunks.Dispose();
                parkingLanes.Dispose();
            }
        }

        private void ProcessDistrictChunks(NativeArray<ArchetypeChunk> districtChunks,
            NativeArray<Entity> parkingLanes, int basePrice, int maxPrice, int minPrice)
        {
            foreach (var chunk in districtChunks)
            {
                var districtEntities = chunk.GetNativeArray(EntityManager.GetEntityTypeHandle());

                for (int i = 0; i < districtEntities.Length; i++)
                {
                    Entity districtEntity = districtEntities[i];

                    try
                    {
                        double utilization = m_UtilizationCalculator.CalculateDistrictUtilization(chunk, districtEntity, parkingLanes);
                        int newPrice = PricingCalculator.CalculateAdjustedPrice(basePrice, maxPrice, minPrice, utilization);
                        LogUtil.Info($"District {districtEntity.Index}: Utilization = {utilization:P2}, New Price = {newPrice}");

                        ApplyDistrictParkingPolicy(districtEntity, newPrice);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.Error($"Error processing district {districtEntity.Index}: {ex.Message}");
                    }
                }
            }
        }

        private void UpdateBuildingParking()
        {
            if (m_UtilizationCalculator == null)
            {
                LogUtil.Warn("UtilizationCalculator not initialized");
                return;
            }

            RequireForUpdate(m_BuildingQuery);

            var buildingChunks = m_BuildingQuery.ToArchetypeChunkArray(Allocator.Temp);
            var parkingLanes = m_ParkingLaneQuery.ToEntityArray(Allocator.Temp);
            var garageLanes = m_GarageLaneQuery.ToEntityArray(Allocator.Temp);

            try
            {
                LogUtil.Info($"Updating building parking: {buildingChunks.Length} building chunks, {parkingLanes.Length} parking lanes, {garageLanes.Length} garage lanes");

                var settings = Mod.m_Setting;
                int basePrice = settings.standard_price_lot;
                double maxIncreasePct = settings.max_price_increase_lot / 100.0;
                double maxDecreasePct = settings.max_price_discount_lot / 100.0;
                int maxPrice = PricingCalculator.CalculateMaxPrice(basePrice, maxIncreasePct);
                int minPrice = PricingCalculator.CalculateMinPrice(basePrice, maxDecreasePct);

                ProcessBuildingChunks(buildingChunks, parkingLanes, garageLanes, basePrice, maxPrice, minPrice);
            }
            finally
            {
                buildingChunks.Dispose();
                parkingLanes.Dispose();
                garageLanes.Dispose();
            }
        }

        private void ProcessBuildingChunks(NativeArray<ArchetypeChunk> buildingChunks,
            NativeArray<Entity> parkingLanes, NativeArray<Entity> garageLanes,
            int basePrice, int maxPrice, int minPrice)
        {
            foreach (var chunk in buildingChunks)
            {
                var buildingEntities = chunk.GetNativeArray(EntityManager.GetEntityTypeHandle());

                for (int i = 0; i < buildingEntities.Length; i++)
                {
                    Entity buildingEntity = buildingEntities[i];

                    try
                    {
                        // Only evaluate buildings that can have parking fee
                        if (!m_PolicyManager.TryGetPolicy(buildingEntity, m_LotParkingFeePrefab, out _))
                        {
                            continue;
                        }

                        double utilization = m_UtilizationCalculator.CalculateBuildingUtilization(buildingEntity, parkingLanes, garageLanes);
                        int newPrice = PricingCalculator.CalculateAdjustedPrice(basePrice, maxPrice, minPrice, utilization);
                        LogUtil.Info($"Building {buildingEntity.Index}: Utilization = {utilization:P2}, New Price = {newPrice}");

                        ApplyLotParkingPolicy(buildingEntity, newPrice);
                    }
                    catch (Exception ex)
                    {
                        LogUtil.Error($"Error processing building {buildingEntity.Index}: {ex.Message}");
                    }
                }
            }
        }

        private void ApplyDistrictParkingPolicy(Entity districtEntity, int newPrice)
        {
            m_PolicyManager.UpdateOrAddPolicy(districtEntity, m_StreetParkingFeePrefab, newPrice);
            LogUtil.Info($"Updated street parking policy for district {districtEntity.Index}: ${newPrice}");
        }

        private void ApplyLotParkingPolicy(Entity buildingEntity, int newPrice)
        {
            m_PolicyManager.UpdateOrAddPolicy(buildingEntity, m_LotParkingFeePrefab, newPrice);
            LogUtil.Info($"Updated parking policy for building {buildingEntity.Index}: ${newPrice}");
        }

        protected override void OnDestroy()
        {
            // Dispose persistent queries - EntityQuery doesn't have IsCreated in this Unity version
            m_DistrictQuery.Dispose();
            m_BuildingQuery.Dispose();
            m_ParkingLaneQuery.Dispose();
            m_GarageLaneQuery.Dispose();

            base.OnDestroy();
        }
    }
}