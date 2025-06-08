using System;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Net;
using Game.Policies;
using Game.Prefabs;
using Game.Buildings;
using Game.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Game.Vehicles;
using Game.Objects;

namespace ParkingPricing
{
    // System to process policy update commands immediately after ECB playback
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(EndSimulationEntityCommandBufferSystem))]
    public partial class PolicyUpdateSystem : GameSystemBase
    {
        private PolicyManager m_PolicyManager;
        private EntityQuery m_PolicyUpdateQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_PolicyManager = new PolicyManager(EntityManager);
            m_PolicyUpdateQuery = GetEntityQuery(ComponentType.ReadOnly<PolicyUpdateCommand>());
        }

        protected override void OnUpdate()
        {
            if (m_PolicyUpdateQuery.IsEmpty) return;

            int appliedUpdates = 0;
            var policyUpdateCommands = m_PolicyUpdateQuery.ToComponentDataArray<PolicyUpdateCommand>(Allocator.Temp);
            var entities = m_PolicyUpdateQuery.ToEntityArray(Allocator.Temp);

            try
            {
                for (int i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var command = policyUpdateCommands[i];

                    try
                    {
                        // Remove the command component
                        EntityManager.RemoveComponent<PolicyUpdateCommand>(entity);

                        // Apply the policy update
                        m_PolicyManager.UpdateOrAddPolicy(entity, command.PolicyPrefab, command.NewPrice);

                        if (command.IsDistrict)
                        {
                            LogUtil.Info($"Updated street parking policy for district {entity.Index}: ${command.NewPrice} (Utilization: {command.Utilization:P2})");
                        }
                        else
                        {
                            LogUtil.Info($"Updated parking policy for building {entity.Index}: ${command.NewPrice} (Utilization: {command.Utilization:P2})");
                        }
                        appliedUpdates++;
                    }
                    catch (Exception ex)
                    {
                        LogUtil.Error($"Failed to apply pricing update to entity {entity.Index}: {ex.Message}");
                    }
                }

                if (appliedUpdates > 0)
                {
                    LogUtil.Info($"Applied {appliedUpdates} pricing updates immediately via ECB");
                }
            }
            finally
            {
                policyUpdateCommands.Dispose();
                entities.Dispose();
            }
        }
    }

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
        [ReadOnly] private ComponentLookup<Lane> m_LaneData;
        [ReadOnly] private ComponentLookup<Game.Net.CarLane> m_CarLaneData;

        // Cached policy prefab entities
        private Entity m_LotParkingFeePrefab;
        private Entity m_StreetParkingFeePrefab;

        // Entity Command Buffer System for immediate updates
        private EndSimulationEntityCommandBufferSystem m_EndSimulationECBSystem;

        // Extracted components
        private PolicyManager m_PolicyManager;

        protected override void OnCreate()
        {
            base.OnCreate();

            InitializeQueries();
            InitializeComponentHandles();
            InitializeLookupComponents();

            // Initialize cached prefab entities
            m_LotParkingFeePrefab = Entity.Null;
            m_StreetParkingFeePrefab = Entity.Null;

            // Initialize ECB system for immediate updates
            m_EndSimulationECBSystem = World.GetOrCreateSystemManaged<EndSimulationEntityCommandBufferSystem>();

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
            m_LaneData = GetComponentLookup<Lane>(true);
            m_CarLaneData = GetComponentLookup<Game.Net.CarLane>(true);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            if (Mod.m_Setting == null)
            {
                LogUtil.Warn("Failed to load mod settings");
                return ParkingPricingConstants.GAME_TICKS_PER_DAY / 45;
            }

            return ParkingPricingConstants.GAME_TICKS_PER_DAY / (int)Mod.m_Setting.updateFreq;
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            if (m_ConfigQuery.IsEmptyIgnoreFilter) return;
            InitializePolicyPrefabs();
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

            // Start async update with immediate application via ECB
            try
            {
                UpdateComponentHandles();
                StartAsyncUpdate();
            }
            catch (Exception ex)
            {
                LogUtil.Exception(ex);
            }
        }

        private void StartAsyncUpdate()
        {
            LogUtil.Info("Starting async parking pricing update");

            var settings = Mod.m_Setting;
            bool enableStreet = settings.enable_for_street;
            bool enableLot = settings.enable_for_lot;

            if (!enableStreet && !enableLot)
                return;

            // Get ECB from the system for immediate updates
            var ecb = m_EndSimulationECBSystem.CreateCommandBuffer();

            // Get entities to process
            var districtEntities = enableStreet ? m_DistrictQuery.ToEntityArray(Allocator.TempJob) : new NativeArray<Entity>(0, Allocator.TempJob);
            var buildingEntities = enableLot ? GetBuildingsWithParkingPolicy() : new NativeArray<Entity>(0, Allocator.TempJob);
            var parkingLanes = m_ParkingLaneQuery.ToEntityArray(Allocator.TempJob);
            var garageLanes = m_GarageLaneQuery.ToEntityArray(Allocator.TempJob);

            // Prepare result arrays
            var districtResults = new NativeArray<DistrictUtilizationResult>(districtEntities.Length, Allocator.TempJob);
            var buildingResults = new NativeArray<BuildingUtilizationResult>(buildingEntities.Length, Allocator.TempJob);

            JobHandle combinedJobHandle = default;

            // Schedule district utilization job
            if (enableStreet && districtEntities.Length > 0)
            {
                var districtJob = new CalculateDistrictUtilizationJob
                {
                    DistrictEntities = districtEntities,
                    ParkingLanes = parkingLanes,
                    ParkingLaneData = GetComponentLookup<Game.Net.ParkingLane>(true),
                    OwnerData = GetComponentLookup<Game.Common.Owner>(true),
                    BorderDistrictData = GetComponentLookup<Game.Areas.BorderDistrict>(true),
                    LaneObjectData = GetBufferLookup<LaneObject>(true),
                    LaneOverlapData = GetBufferLookup<LaneOverlap>(true),
                    LaneData = GetComponentLookup<Lane>(true),
                    PrefabRefData = GetComponentLookup<PrefabRef>(true),
                    CurveData = GetComponentLookup<Curve>(true),
                    ParkingLaneDataComponents = GetComponentLookup<ParkingLaneData>(true),
                    ParkedCarData = GetComponentLookup<ParkedCar>(true),
                    UnspawnedData = GetComponentLookup<Unspawned>(true),
                    ObjectGeometryData = GetComponentLookup<ObjectGeometryData>(true),
                    SubLanes = GetBufferLookup<Game.Net.SubLane>(true),
                    CarLaneData = GetComponentLookup<Game.Net.CarLane>(true),
                    Results = districtResults
                };

                var districtJobHandle = districtJob.Schedule(districtEntities.Length, 1, default);
                combinedJobHandle = JobHandle.CombineDependencies(combinedJobHandle, districtJobHandle);
            }

            // Schedule building utilization job
            if (enableLot && buildingEntities.Length > 0)
            {
                var buildingJob = new CalculateBuildingUtilizationJob
                {
                    BuildingEntities = buildingEntities,
                    ParkingLanes = parkingLanes,
                    GarageLanes = garageLanes,
                    ParkingLaneData = GetComponentLookup<Game.Net.ParkingLane>(true),
                    GarageLaneData = GetComponentLookup<Game.Net.GarageLane>(true),
                    OwnerData = GetComponentLookup<Game.Common.Owner>(true),
                    BuildingData = GetComponentLookup<Game.Buildings.Building>(true),
                    LaneObjectData = GetBufferLookup<LaneObject>(true),
                    PrefabRefData = GetComponentLookup<PrefabRef>(true),
                    CurveData = GetComponentLookup<Curve>(true),
                    ParkingLaneDataComponents = GetComponentLookup<ParkingLaneData>(true),
                    ParkedCarData = GetComponentLookup<ParkedCar>(true),
                    Results = buildingResults
                };

                var buildingJobHandle = buildingJob.Schedule(buildingEntities.Length, 1, default);
                combinedJobHandle = JobHandle.CombineDependencies(combinedJobHandle, buildingJobHandle);
            }

            // Schedule immediate pricing application job with ECB
            if (districtEntities.Length > 0 || buildingEntities.Length > 0)
            {
                var applyPricingJob = new ApplyPricingWithECBJob
                {
                    DistrictResults = districtResults,
                    BuildingResults = buildingResults,
                    BaseStreetPrice = settings.standard_price_street,
                    MaxStreetPrice = PricingCalculator.CalculateMaxPrice(settings.standard_price_street, settings.max_price_increase_street / 100.0),
                    MinStreetPrice = PricingCalculator.CalculateMinPrice(settings.standard_price_street, settings.max_price_discount_street / 100.0),
                    BaseLotPrice = settings.standard_price_lot,
                    MaxLotPrice = PricingCalculator.CalculateMaxPrice(settings.standard_price_lot, settings.max_price_increase_lot / 100.0),
                    MinLotPrice = PricingCalculator.CalculateMinPrice(settings.standard_price_lot, settings.max_price_discount_lot / 100.0),
                    StreetParkingFeePrefab = m_StreetParkingFeePrefab,
                    LotParkingFeePrefab = m_LotParkingFeePrefab,
                    EntityCommandBuffer = ecb
                };

                var applyJobHandle = applyPricingJob.Schedule(combinedJobHandle);
                combinedJobHandle = applyJobHandle;

                // Register the job with the ECB system for completion and playback
                m_EndSimulationECBSystem.AddJobHandleForProducer(combinedJobHandle);
            }

            // Dispose input arrays (they're no longer needed after scheduling)
            districtEntities.Dispose();
            buildingEntities.Dispose();
            parkingLanes.Dispose();
            garageLanes.Dispose();
        }

        private NativeArray<Entity> GetBuildingsWithParkingPolicy()
        {
            var tempList = new NativeList<Entity>(Allocator.Temp);
            var buildingChunks = m_BuildingQuery.ToArchetypeChunkArray(Allocator.Temp);

            foreach (var chunk in buildingChunks)
            {
                var buildingEntities = chunk.GetNativeArray(EntityManager.GetEntityTypeHandle());

                for (int i = 0; i < buildingEntities.Length; i++)
                {
                    Entity buildingEntity = buildingEntities[i];

                    // Only include buildings that can have parking fee policy
                    if (m_PolicyManager.TryGetPolicy(buildingEntity, m_LotParkingFeePrefab, out _))
                    {
                        tempList.Add(buildingEntity);
                    }
                }
            }

            buildingChunks.Dispose();
            var result = tempList.ToArray(Allocator.TempJob);
            tempList.Dispose();
            return result;
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
            m_LaneData.Update(this);
            m_CarLaneData.Update(this);
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