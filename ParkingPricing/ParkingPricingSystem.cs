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

        // Entity result arrays for reuse
        private NativeArray<Entity> _districtEntities = new(0, Allocator.Persistent);
        private NativeList<Entity> _buildingEntities = new(Allocator.Persistent);
        private NativeArray<Entity> _parkingLanes = new(0, Allocator.Persistent);
        private NativeArray<Entity> _garageLanes = new(0, Allocator.Persistent);

        // Utilization result lists for reuse
        private readonly NativeList<DistrictUtilizationResult> _districtResults = new(Allocator.Persistent);
        private readonly NativeList<BuildingUtilizationResult> _buildingResults = new(Allocator.Persistent);

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
            _districtQuery = SystemAPI.QueryBuilder().WithAll<District>().Build();
            _buildingQuery = SystemAPI.QueryBuilder().WithAll<Building>().Build();
            _parkingLaneQuery = SystemAPI.QueryBuilder().WithAll<ParkingLane>().WithAll<Owner>().WithAll<LaneObject>()
                .WithAll<LaneOverlap>().WithAll<Lane>().Build();
            _garageLaneQuery = SystemAPI.QueryBuilder().WithAll<GarageLane>().WithAll<Owner>().Build();
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

            if (_districtResults.Length > 0 || _buildingResults.Length > 0) {
                // Previous job runs haven't finished yet, this is unlikely
                // but might happen on a large city with a slow CPU.
                return;
            }

            // Get ECB from the system for immediate updates
            EntityCommandBuffer ecb = _parkingPricingECBSystem.CreateCommandBuffer();

            // Ensure we clean up from the previous run
            _districtEntities.Dispose();
            _buildingEntities.Dispose();
            _parkingLanes.Dispose();
            _garageLanes.Dispose();

            // Get entities to process
            _parkingLanes = _parkingLaneQuery.ToEntityArray(Allocator.Persistent);
            if (enableStreet) {
                _districtEntities = _districtQuery.ToEntityArray(Allocator.Persistent);
            }

            if (enableLot) {
                _buildingEntities = GetBuildingsWithParkingPolicy();
                _garageLanes = _garageLaneQuery.ToEntityArray(Allocator.Persistent);
            }

            if (_districtEntities.Length == 0 && _buildingEntities.Length == 0) {
                return;
            }

            JobHandle combinedJobHandle = default;

            // Schedule district utilization job
            if (enableStreet && _districtEntities.Length > 0) {
                var districtJob = new CalculateDistrictUtilizationJob {
                    DistrictEntities = _districtEntities,
                    ParkingLanes = _parkingLanes,
                    ParkingLaneData = SystemAPI.GetComponentLookup<ParkingLane>(true),
                    OwnerData = SystemAPI.GetComponentLookup<Owner>(true),
                    BorderDistrictData = SystemAPI.GetComponentLookup<BorderDistrict>(true),
                    LaneObjectData = SystemAPI.GetBufferLookup<LaneObject>(true),
                    LaneOverlapData = SystemAPI.GetBufferLookup<LaneOverlap>(true),
                    LaneData = SystemAPI.GetComponentLookup<Lane>(true),
                    PrefabRefData = SystemAPI.GetComponentLookup<PrefabRef>(true),
                    CurveData = SystemAPI.GetComponentLookup<Curve>(true),
                    ParkingLaneDataComponents = SystemAPI.GetComponentLookup<ParkingLaneData>(true),
                    ParkedCarData = SystemAPI.GetComponentLookup<ParkedCar>(true),
                    UnspawnedData = SystemAPI.GetComponentLookup<Unspawned>(true),
                    ObjectGeometryData = SystemAPI.GetComponentLookup<ObjectGeometryData>(true),
                    SubLanes = SystemAPI.GetBufferLookup<SubLane>(true),
                    CarLaneData = SystemAPI.GetComponentLookup<CarLane>(true),
                    Results = _districtResults
                };

                JobHandle districtJobHandle = districtJob.Schedule(_districtEntities.Length, 1);
                combinedJobHandle = JobHandle.CombineDependencies(combinedJobHandle, districtJobHandle);
            }

            // Schedule building utilization job
            if (enableLot && _buildingEntities.Length > 0) {
                var buildingJob = new CalculateBuildingUtilizationJob {
                    BuildingEntities = _buildingEntities,
                    ParkingLanes = _parkingLanes,
                    GarageLanes = _garageLanes,
                    ParkingLaneData = SystemAPI.GetComponentLookup<ParkingLane>(true),
                    GarageLaneData = SystemAPI.GetComponentLookup<GarageLane>(true),
                    OwnerData = SystemAPI.GetComponentLookup<Owner>(true),
                    BuildingData = SystemAPI.GetComponentLookup<Building>(true),
                    LaneObjectData = SystemAPI.GetBufferLookup<LaneObject>(true),
                    PrefabRefData = SystemAPI.GetComponentLookup<PrefabRef>(true),
                    CurveData = SystemAPI.GetComponentLookup<Curve>(true),
                    ParkingLaneDataComponents = SystemAPI.GetComponentLookup<ParkingLaneData>(true),
                    ParkedCarData = SystemAPI.GetComponentLookup<ParkedCar>(true),
                    Results = _buildingResults
                };

                JobHandle buildingJobHandle = buildingJob.Schedule(_buildingEntities.Length, 1);
                combinedJobHandle = JobHandle.CombineDependencies(combinedJobHandle, buildingJobHandle);
            }

            // Schedule immediate pricing application job with ECB
            var applyPricingJob = new ApplyPricingWithECBJob {
                DistrictResults = _districtResults,
                BuildingResults = _buildingResults,
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
                $"Scheduled ECB job to add PolicyUpdateCommand to {_districtEntities.Length} districts and {_buildingEntities.Length} buildings"
            );
        }

        private NativeList<Entity> GetBuildingsWithParkingPolicy() {
            var result = new NativeList<Entity>(Allocator.Temp);
            NativeArray<ArchetypeChunk> buildingChunks = _buildingQuery.ToArchetypeChunkArray(Allocator.Temp);

            foreach (ArchetypeChunk chunk in buildingChunks) {
                NativeArray<Entity> buildingEntities = chunk.GetNativeArray(EntityManager.GetEntityTypeHandle());

                foreach (Entity buildingEntity in buildingEntities) {
                    // Only include buildings that can have parking fee policy
                    if (_policyManager.HasPolicy(buildingEntity, _lotParkingFeePrefab)) {
                        result.Add(buildingEntity);
                    }
                }
            }

            buildingChunks.Dispose();
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
