using System;
using Colossal.Entities;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using CarLane = Game.Net.CarLane;
using ParkingLane = Game.Net.ParkingLane;
using SubLane = Game.Net.SubLane;

namespace ParkingPricing {
    // Custom ECB system for parking pricing updates
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ParkingPricingSystem))]
    public partial class ParkingPricingEntityCommandBufferSystem : EntityCommandBufferSystem {
    }

    // System to process policy update commands immediately after ECB playback
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ParkingPricingEntityCommandBufferSystem))]
    public partial class PolicyUpdateSystem : GameSystemBase {
        private PolicyManager _policyManager;
        private EntityQuery _policyUpdateQuery;

        protected override void OnCreate() {
            base.OnCreate();
            _policyManager = new PolicyManager(EntityManager);
            _policyUpdateQuery = GetEntityQuery(ComponentType.ReadOnly<PolicyUpdateCommand>());
        }

        protected override void OnUpdate() {
            if (_policyUpdateQuery.IsEmpty) {
                return;
            }

            LogUtil.Debug(
                $"PolicyUpdateSystem.OnUpdate: Query has {_policyUpdateQuery.CalculateEntityCount()} entities"
            );
            int appliedUpdates = 0;
            NativeArray<PolicyUpdateCommand> policyUpdateCommands =
                _policyUpdateQuery.ToComponentDataArray<PolicyUpdateCommand>(Allocator.Temp);
            NativeArray<Entity> entities = _policyUpdateQuery.ToEntityArray(Allocator.Temp);

            try {
                for (int i = 0; i < entities.Length; i++) {
                    Entity entity = entities[i];
                    PolicyUpdateCommand command = policyUpdateCommands[i];

                    try {
                        // Remove the command component
                        EntityManager.RemoveComponent<PolicyUpdateCommand>(entity);

                        // Apply the policy update
                        _policyManager.UpdateOrAddPolicy(entity, command.PolicyPrefab, command.NewPrice);

                        LogUtil.Info(
                            command.IsDistrict
                                ? $"Updated street parking policy for district {entity.Index}: ${command.NewPrice} (Utilization: {command.Utilization:P2})"
                                : $"Updated parking policy for building {entity.Index}: ${command.NewPrice} (Utilization: {command.Utilization:P2})"
                        );

                        appliedUpdates++;
                    } catch (Exception ex) {
                        LogUtil.Error($"Failed to apply pricing update to entity {entity.Index}: {ex.Message}");
                    }
                }

                if (appliedUpdates > 0) {
                    LogUtil.Info($"Applied {appliedUpdates} pricing updates immediately via ECB");
                }
            } finally {
                policyUpdateCommands.Dispose();
                entities.Dispose();
            }
        }
    }

    // Main system class now focused on coordination and ECS management
    public partial class ParkingPricingSystem : GameSystemBase {
        // Entity queries
        private EntityQuery _districtQuery;
        private EntityQuery _buildingQuery;
        private EntityQuery _parkingLaneQuery;
        private EntityQuery _garageLaneQuery;
        private EntityQuery _configQuery;
        private EntityQuery _policyPrefabQuery;

        // Cached policy prefab entities
        private Entity _lotParkingFeePrefab;
        private Entity _streetParkingFeePrefab;

        // Entity Command Buffer System for immediate updates
        private ParkingPricingEntityCommandBufferSystem _parkingPricingECBSystem;

        // Extracted components
        private PolicyManager _policyManager;

        protected override void OnCreate() {
            base.OnCreate();

            InitializeQueries();

            // Initialize cached prefab entities
            _lotParkingFeePrefab = Entity.Null;
            _streetParkingFeePrefab = Entity.Null;

            // Initialize ECB system for immediate updates
            _parkingPricingECBSystem = World.GetOrCreateSystemManaged<ParkingPricingEntityCommandBufferSystem>();

            // Initialize extracted components
            _policyManager = new PolicyManager(EntityManager);
        }

        private void InitializeQueries() {
            _configQuery = GetEntityQuery(ComponentType.ReadOnly<UITransportConfigurationData>());
            _policyPrefabQuery = GetEntityQuery(ComponentType.ReadOnly<PrefabData>());

            // Cache entity queries to avoid recreating them every frame
            _districtQuery = new EntityQueryBuilder(Allocator.Persistent).WithAll<District>().Build(this);

            _buildingQuery = new EntityQueryBuilder(Allocator.Persistent).WithAll<Building>().Build(this);

            _parkingLaneQuery = new EntityQueryBuilder(Allocator.Persistent).WithAll<ParkingLane>().WithAll<Owner>()
                .WithAll<LaneObject>().WithAll<LaneOverlap>().WithAll<Lane>().Build(this);

            _garageLaneQuery = new EntityQueryBuilder(Allocator.Persistent).WithAll<GarageLane>().WithAll<Owner>()
                .Build(this);
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase) {
            if (Mod.Setting == null) {
                LogUtil.Warn("Failed to load mod settings");
                return ParkingPricingConstants.GameTicksPerDay / 45;
            }

            return ParkingPricingConstants.GameTicksPerDay / (int)Mod.Setting.UpdateFreq;
        }

        protected override void OnGameLoaded(Context serializationContext) {
            if (_configQuery.IsEmptyIgnoreFilter) {
                return;
            }

            InitializePolicyPrefabs();
        }

        private void InitializePolicyPrefabs() {
            // Only initialize once
            if (_lotParkingFeePrefab != Entity.Null) {
                return;
            }

            NativeArray<Entity> policyPrefabs = _policyPrefabQuery.ToEntityArray(Allocator.Temp);
            try {
                foreach (Entity prefabEntity in policyPrefabs) {
                    if (EntityManager.TryGetComponent(prefabEntity, out BuildingOptionData buildingOptionData)) {
                        if (!BuildingUtils.HasOption(buildingOptionData, BuildingOption.PaidParking)) {
                            continue;
                        }

                        _lotParkingFeePrefab = prefabEntity;
                        LogUtil.Info($"Found Lot Parking Fee prefab: {prefabEntity.Index}");
                    } else if (EntityManager.TryGetComponent(prefabEntity, out DistrictOptionData districtOptionData)) {
                        if (!AreaUtils.HasOption(districtOptionData, DistrictOption.PaidParking)) {
                            continue;
                        }

                        _streetParkingFeePrefab = prefabEntity;
                        LogUtil.Info($"Found Street Parking Fee prefab: {prefabEntity.Index}");
                    }
                }

                if (_lotParkingFeePrefab == Entity.Null) {
                    LogUtil.Warn("Could not find 'Lot Parking Fee' policy prefab");
                }

                if (_streetParkingFeePrefab == Entity.Null) {
                    LogUtil.Warn("Could not find 'Roadside Parking Fee' policy prefab");
                }
            } finally {
                policyPrefabs.Dispose();
            }
        }

        protected override void OnUpdate() {
            if (Mod.Setting == null) {
                LogUtil.Warn("Mod settings not initialized, skipping update");
                return;
            }

            // Start async update with immediate application via ECB
            try {
                StartAsyncUpdate();
            } catch (Exception ex) {
                LogUtil.Exception(ex);
            }
        }

        private void StartAsyncUpdate() {
            LogUtil.Info("Starting async parking pricing update");

            ModSettings settings = Mod.Setting;
            bool enableStreet = settings.EnableForStreet;
            bool enableLot = settings.EnableForLot;

            if (!enableStreet && !enableLot) {
                return;
            }

            // Get ECB from the system for immediate updates
            EntityCommandBuffer ecb = _parkingPricingECBSystem.CreateCommandBuffer();

            // Get entities to process
            NativeArray<Entity> districtEntities = enableStreet
                ? _districtQuery.ToEntityArray(Allocator.TempJob)
                : new NativeArray<Entity>(0, Allocator.TempJob);
            NativeArray<Entity> buildingEntities =
                enableLot ? GetBuildingsWithParkingPolicy() : new NativeArray<Entity>(0, Allocator.TempJob);
            NativeArray<Entity> parkingLanes = _parkingLaneQuery.ToEntityArray(Allocator.TempJob);
            NativeArray<Entity> garageLanes = _garageLaneQuery.ToEntityArray(Allocator.TempJob);

            // Prepare result arrays
            var districtResults =
                new NativeArray<DistrictUtilizationResult>(districtEntities.Length, Allocator.Persistent);
            var buildingResults =
                new NativeArray<BuildingUtilizationResult>(buildingEntities.Length, Allocator.Persistent);

            JobHandle combinedJobHandle = default;

            // Schedule district utilization job
            if (enableStreet && districtEntities.Length > 0) {
                var districtJob = new CalculateDistrictUtilizationJob {
                    DistrictEntities = districtEntities,
                    ParkingLanes = parkingLanes,
                    ParkingLaneData = GetComponentLookup<ParkingLane>(true),
                    OwnerData = GetComponentLookup<Owner>(true),
                    BorderDistrictData = GetComponentLookup<BorderDistrict>(true),
                    LaneObjectData = GetBufferLookup<LaneObject>(true),
                    LaneOverlapData = GetBufferLookup<LaneOverlap>(true),
                    LaneData = GetComponentLookup<Lane>(true),
                    PrefabRefData = GetComponentLookup<PrefabRef>(true),
                    CurveData = GetComponentLookup<Curve>(true),
                    ParkingLaneDataComponents = GetComponentLookup<ParkingLaneData>(true),
                    ParkedCarData = GetComponentLookup<ParkedCar>(true),
                    UnspawnedData = GetComponentLookup<Unspawned>(true),
                    ObjectGeometryData = GetComponentLookup<ObjectGeometryData>(true),
                    SubLanes = GetBufferLookup<SubLane>(true),
                    CarLaneData = GetComponentLookup<CarLane>(true),
                    Results = districtResults
                };

                JobHandle districtJobHandle = districtJob.Schedule(districtEntities.Length, 1);
                combinedJobHandle = JobHandle.CombineDependencies(combinedJobHandle, districtJobHandle);
            }

            // Schedule building utilization job
            if (enableLot && buildingEntities.Length > 0) {
                var buildingJob = new CalculateBuildingUtilizationJob {
                    BuildingEntities = buildingEntities,
                    ParkingLanes = parkingLanes,
                    GarageLanes = garageLanes,
                    ParkingLaneData = GetComponentLookup<ParkingLane>(true),
                    GarageLaneData = GetComponentLookup<GarageLane>(true),
                    OwnerData = GetComponentLookup<Owner>(true),
                    BuildingData = GetComponentLookup<Building>(true),
                    LaneObjectData = GetBufferLookup<LaneObject>(true),
                    PrefabRefData = GetComponentLookup<PrefabRef>(true),
                    CurveData = GetComponentLookup<Curve>(true),
                    ParkingLaneDataComponents = GetComponentLookup<ParkingLaneData>(true),
                    ParkedCarData = GetComponentLookup<ParkedCar>(true),
                    Results = buildingResults
                };

                JobHandle buildingJobHandle = buildingJob.Schedule(buildingEntities.Length, 1);
                combinedJobHandle = JobHandle.CombineDependencies(combinedJobHandle, buildingJobHandle);
            }

            // Schedule immediate pricing application job with ECB
            if (districtEntities.Length > 0 || buildingEntities.Length > 0) {
                var applyPricingJob = new ApplyPricingWithECBJob {
                    DistrictResults = districtResults,
                    BuildingResults = buildingResults,
                    BaseStreetPrice = settings.StandardPriceStreet,
                    MaxStreetPrice =
                        PricingCalculator.CalculateMaxPrice(
                            settings.StandardPriceStreet, settings.MaxPriceIncreaseStreet / 100.0
                        ),
                    MinStreetPrice =
                        PricingCalculator.CalculateMinPrice(
                            settings.StandardPriceStreet, settings.MaxPriceDiscountStreet / 100.0
                        ),
                    BaseLotPrice = settings.StandardPriceLot,
                    MaxLotPrice =
                        PricingCalculator.CalculateMaxPrice(
                            settings.StandardPriceLot, settings.MaxPriceIncreaseLot / 100.0
                        ),
                    MinLotPrice =
                        PricingCalculator.CalculateMinPrice(
                            settings.StandardPriceLot, settings.MaxPriceDiscountLot / 100.0
                        ),
                    StreetParkingFeePrefab = _streetParkingFeePrefab,
                    LotParkingFeePrefab = _lotParkingFeePrefab,
                    EntityCommandBuffer = ecb
                };

                JobHandle applyJobHandle = applyPricingJob.Schedule(combinedJobHandle);
                combinedJobHandle = applyJobHandle;

                // Register the job with the ECB system for completion and playback
                _parkingPricingECBSystem.AddJobHandleForProducer(combinedJobHandle);

                LogUtil.Info(
                    $"Scheduled ECB job to add PolicyUpdateCommand to {districtEntities.Length} districts and {buildingEntities.Length} buildings"
                );
            }

            // Dispose input arrays (they're no longer needed after scheduling)
            districtEntities.Dispose();
            buildingEntities.Dispose();
            parkingLanes.Dispose();
            garageLanes.Dispose();
        }

        private NativeArray<Entity> GetBuildingsWithParkingPolicy() {
            var tempList = new NativeList<Entity>(Allocator.Temp);
            NativeArray<ArchetypeChunk> buildingChunks = _buildingQuery.ToArchetypeChunkArray(Allocator.Temp);

            foreach (ArchetypeChunk chunk in buildingChunks) {
                NativeArray<Entity> buildingEntities = chunk.GetNativeArray(EntityManager.GetEntityTypeHandle());

                foreach (Entity buildingEntity in buildingEntities) {
                    // Only include buildings that can have parking fee policy
                    if (_policyManager.HasPolicy(buildingEntity, _lotParkingFeePrefab)) {
                        tempList.Add(buildingEntity);
                    }
                }
            }

            buildingChunks.Dispose();
            NativeArray<Entity> result = tempList.ToArray(Allocator.TempJob);
            tempList.Dispose();
            return result;
        }

        protected override void OnDestroy() {
            // Dispose persistent queries - EntityQuery doesn't have IsCreated in this Unity version
            _districtQuery.Dispose();
            _buildingQuery.Dispose();
            _parkingLaneQuery.Dispose();
            _garageLaneQuery.Dispose();

            base.OnDestroy();
        }
    }
}
