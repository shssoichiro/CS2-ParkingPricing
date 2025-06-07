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
using Colossal.Mathematics;
using Unity.Mathematics;
using Game.Common;
using Game.Objects;

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
        [ReadOnly]
        private BufferLookup<Game.Net.SubLane> m_SubLanes;
        private ComponentLookup<ParkedCar> m_ParkedCarData;
        [ReadOnly]
        public ComponentLookup<Unspawned> m_UnspawnedData;
        [ReadOnly]
        public ComponentLookup<PrefabRef> m_PrefabRefData;
        [ReadOnly]
        public ComponentLookup<ObjectGeometryData> m_ObjectGeometryData;
        [ReadOnly]
        public ComponentLookup<Lane> m_LaneType;
        [ReadOnly]
        public BufferLookup<LaneObject> m_LaneObjectType;
        [ReadOnly]
        public BufferLookup<LaneOverlap> m_LaneOverlapType;
        [ReadOnly]
        public ComponentLookup<Lane> m_LaneData;
        [ReadOnly]
        public ComponentLookup<Game.Net.CarLane> m_CarLaneData;

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

            // Initialize lookup components that aren't already initialized elsewhere
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
                .WithAll<LaneObject>()
                .WithAll<LaneOverlap>()
                .WithAll<Lane>()
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
                    double utilization = CalculateDistrictUtilization(chunk, districtEntity, parkingLanes);
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

            // Check parking lanes that belong to this building
            foreach (Entity laneEntity in parkingLanes)
            {
                if (DoesLaneBelongToBuilding(laneEntity, buildingEntity))
                {
                    var parkingLane = EntityManager.GetComponentData<Game.Net.ParkingLane>(laneEntity);

                    // Skip virtual lanes
                    if ((parkingLane.m_Flags & ParkingLaneFlags.VirtualLane) != 0)
                    {
                        continue;
                    }

                    GetBuildingParkingLaneCounts(laneEntity, parkingLane, ref slotCapacity, ref parkedCars);
                }
            }

            // Check garage lanes that belong to this building
            foreach (Entity laneEntity in garageLanes)
            {
                if (DoesLaneBelongToBuilding(laneEntity, buildingEntity))
                {
                    var garageLane = EntityManager.GetComponentData<Game.Net.GarageLane>(laneEntity);
                    slotCapacity += garageLane.m_VehicleCapacity;
                    parkedCars += garageLane.m_VehicleCount;
                }
            }

            // Calculate utilization percentage
            return slotCapacity > 0 ? (double)parkedCars / slotCapacity : 0.0;
        }

        private void GetBuildingParkingLaneCounts(Entity subLane, Game.Net.ParkingLane parkingLane, ref int slotCapacity, ref int parkedCars)
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

        private void GetStreetParkingLaneCapacity(Entity subLane, Game.Net.ParkingLane parkingLane, DynamicBuffer<LaneOverlap> laneOverlaps, Bounds1 blockedRange, ref int slotCapacity, ref int parkedCars)
        {
            // Get parking slot count using game's method
            Entity prefab = EntityManager.GetComponentData<PrefabRef>(subLane).m_Prefab;
            Curve curve = EntityManager.GetComponentData<Curve>(subLane);
            var parkingLaneData = EntityManager.GetComponentData<ParkingLaneData>(prefab);
            var laneObjects = EntityManager.GetBuffer<LaneObject>(subLane, true);

            if (parkingLaneData.m_SlotInterval != 0f)
            {
                int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
                slotCapacity += parkingSlotCount;

                // Count parked cars in this lane
                for (int j = 0; j < laneObjects.Length; j++)
                {
                    if (EntityManager.HasComponent<ParkedCar>(laneObjects[j].m_LaneObject))
                    {
                        parkedCars++;
                    }
                }
                return;
            }

            // Initialize variables for calculating maximum parking space between obstacles
            float maxParkingSpace = 0f;
            float2 currentOffsets = math.select(0f, 0.5f, (parkingLane.m_Flags & ParkingLaneFlags.StartingLane) == 0);
            float3 currentPosition = curve.m_Bezier.a;

            // Initialize variables for tracking parked cars along the curve
            float nextCarPosition = 2f; // 2f means no more cars (beyond curve end)
            float2 nextCarOffsets = 0f;
            int carIndex = 0;

            // Find the first parked car along the curve
            while (carIndex < laneObjects.Length)
            {
                LaneObject currentLaneObject = laneObjects[carIndex++];
                if (m_ParkedCarData.HasComponent(currentLaneObject.m_LaneObject) && !m_UnspawnedData.HasComponent(currentLaneObject.m_LaneObject))
                {
                    nextCarPosition = currentLaneObject.m_CurvePosition.x;
                    nextCarOffsets = VehicleUtils.GetParkingOffsets(currentLaneObject.m_LaneObject, ref m_PrefabRefData, ref m_ObjectGeometryData) + 1f;
                    break;
                }
            }

            // Initialize variables for tracking lane overlaps (intersections, etc.)
            float2 nextOverlapRange = 2f; // 2f means no more overlaps
            int overlapIndex = 0;

            // Find the first lane overlap
            if (overlapIndex < laneOverlaps.Length)
            {
                LaneOverlap currentOverlap = laneOverlaps[overlapIndex++];
                nextOverlapRange = new float2((int)currentOverlap.m_ThisStart, (int)currentOverlap.m_ThisEnd) * 0.003921569f;
            }

            // Initialize variables for handling blocked ranges (areas where parking is prohibited)
            float3 blockedCenterPosition = default(float3);
            float3 blockedDistances = default(float3);
            if (blockedRange.max >= blockedRange.min)
            {
                blockedCenterPosition = MathUtils.Position(curve.m_Bezier, MathUtils.Center(blockedRange));
                blockedDistances.x = math.distance(MathUtils.Position(curve.m_Bezier, blockedRange.min), blockedCenterPosition);
                blockedDistances.y = math.distance(MathUtils.Position(curve.m_Bezier, blockedRange.max), blockedCenterPosition);
            }

            // Main loop: iterate through all obstacles (cars and overlaps) along the curve
            float segmentLength;
            while (nextCarPosition != 2f || nextOverlapRange.x != 2f)
            {
                float2 obstacleRange;
                float nextObstacleEndOffset;

                // Determine which obstacle comes first: parked car or lane overlap
                if (nextCarPosition <= nextOverlapRange.x)
                {
                    // Process parked car obstacle
                    obstacleRange = nextCarPosition;
                    currentOffsets.y = nextCarOffsets.x;
                    nextObstacleEndOffset = nextCarOffsets.y;
                    nextCarPosition = 2f; // Reset to indicate no more cars until we find the next one

                    // Find next parked car
                    while (carIndex < laneObjects.Length)
                    {
                        LaneObject nextLaneObject = laneObjects[carIndex++];
                        if (m_ParkedCarData.HasComponent(nextLaneObject.m_LaneObject) && !m_UnspawnedData.HasComponent(nextLaneObject.m_LaneObject))
                        {
                            nextCarPosition = nextLaneObject.m_CurvePosition.x;
                            nextCarOffsets = VehicleUtils.GetParkingOffsets(nextLaneObject.m_LaneObject, ref m_PrefabRefData, ref m_ObjectGeometryData) + 1f;
                            break;
                        }
                    }
                }
                else
                {
                    // Process lane overlap obstacle
                    obstacleRange = nextOverlapRange;
                    currentOffsets.y = 0.5f;
                    nextObstacleEndOffset = 0.5f;
                    nextOverlapRange = 2f; // Reset to indicate no more overlaps until we find the next one

                    // Find next lane overlap, merging consecutive overlaps
                    while (overlapIndex < laneOverlaps.Length)
                    {
                        LaneOverlap nextOverlap = laneOverlaps[overlapIndex++];
                        float2 nextOverlapNormalized = new float2((int)nextOverlap.m_ThisStart, (int)nextOverlap.m_ThisEnd) * 0.003921569f;
                        if (nextOverlapNormalized.x <= obstacleRange.y)
                        {
                            // Merge consecutive overlaps
                            obstacleRange.y = math.max(obstacleRange.y, nextOverlapNormalized.y);
                            continue;
                        }
                        nextOverlapRange = nextOverlapNormalized;
                        break;
                    }
                }

                // Calculate the available parking space in the current segment
                float3 segmentEndPosition = MathUtils.Position(curve.m_Bezier, obstacleRange.x);
                segmentLength = math.distance(currentPosition, segmentEndPosition) - math.csum(currentOffsets);

                // Adjust for blocked ranges if they affect this segment
                if (blockedRange.max >= blockedRange.min)
                {
                    float distanceToBlockedStart = math.distance(currentPosition, blockedCenterPosition) - currentOffsets.x - blockedDistances.x;
                    float distanceToBlockedEnd = math.distance(segmentEndPosition, blockedCenterPosition) - currentOffsets.y - blockedDistances.y;
                    segmentLength = math.min(segmentLength, math.max(distanceToBlockedStart, distanceToBlockedEnd));
                }

                // Update the maximum parking space found so far
                maxParkingSpace = math.max(maxParkingSpace, segmentLength);

                // Move to the next segment
                currentOffsets.x = nextObstacleEndOffset;
                currentPosition = MathUtils.Position(curve.m_Bezier, obstacleRange.y);
            }

            // Process the final segment from the last obstacle to the end of the curve
            currentOffsets.y = math.select(0f, 0.5f, (parkingLane.m_Flags & ParkingLaneFlags.EndingLane) == 0);
            segmentLength = math.distance(currentPosition, curve.m_Bezier.d) - math.csum(currentOffsets);

            // Adjust final segment for blocked ranges
            if (blockedRange.max >= blockedRange.min)
            {
                float distanceToBlockedStart = math.distance(currentPosition, blockedCenterPosition) - currentOffsets.x - blockedDistances.x;
                float distanceToBlockedEnd = math.distance(curve.m_Bezier.d, blockedCenterPosition) - currentOffsets.y - blockedDistances.y;
                segmentLength = math.min(segmentLength, math.max(distanceToBlockedStart, distanceToBlockedEnd));
            }

            // Update maximum parking space with the final segment
            maxParkingSpace = math.max(maxParkingSpace, segmentLength);

            // Apply maximum car length constraint if specified
            var availableParkingLength = parkingLaneData.m_MaxCarLength != 0f ? math.min(maxParkingSpace, parkingLaneData.m_MaxCarLength) : maxParkingSpace;

            // Count all objects in the lane as parked cars for utilization calculation
            parkedCars += laneObjects.Length;

            LogUtil.Debug($"Found parking lane: SlotInterval={parkingLaneData.m_SlotInterval:P2}, carsPresent={laneObjects.Length}");
        }

        private double CalculateDistrictUtilization(ArchetypeChunk chunk, Entity districtEntity, NativeArray<Entity> parkingLanes)
        {
            double totalCapacity = 0;
            double totalOccupied = 0;

            // Check all parking lanes to see which belong to this district
            for (int i = 0; i < parkingLanes.Length; i++)
            {
                var laneEntity = parkingLanes[i];
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

                            // Get lane data directly from the lane entity
                            if (EntityManager.HasBuffer<LaneObject>(laneEntity) &&
                                EntityManager.HasBuffer<LaneOverlap>(laneEntity) &&
                                EntityManager.HasComponent<Lane>(laneEntity))
                            {
                                DynamicBuffer<LaneOverlap> laneOverlaps = EntityManager.GetBuffer<LaneOverlap>(laneEntity, true);
                                Lane laneData = EntityManager.GetComponentData<Lane>(laneEntity);
                                Bounds1 blockedRange = GetBlockedRange(owner, laneData);
                                GetStreetParkingLaneCapacity(laneEntity, parkingLane, laneOverlaps, blockedRange, ref laneCapacity, ref laneOccupied);

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
            }

            // Calculate utilization percentage
            return totalCapacity > 0 ? totalOccupied / totalCapacity : 0.0;
        }

        private Bounds1 GetBlockedRange(Owner owner, Lane laneData)
        {
            Bounds1 result = new Bounds1(2f, -1f);
            if (m_SubLanes.HasBuffer(owner.m_Owner))
            {
                DynamicBuffer<Game.Net.SubLane> dynamicBuffer = m_SubLanes[owner.m_Owner];
                for (int i = 0; i < dynamicBuffer.Length; i++)
                {
                    Entity subLane = dynamicBuffer[i].m_SubLane;
                    Lane lane = m_LaneData[subLane];
                    if (laneData.m_StartNode.EqualsIgnoreCurvePos(lane.m_MiddleNode) && m_CarLaneData.HasComponent(subLane))
                    {
                        Game.Net.CarLane carLane = m_CarLaneData[subLane];
                        if (carLane.m_BlockageEnd >= carLane.m_BlockageStart)
                        {
                            Bounds1 blockageBounds = carLane.blockageBounds;
                            blockageBounds.min = math.select(blockageBounds.min - 0.01f, 0f, blockageBounds.min <= 0.51f);
                            blockageBounds.max = math.select(blockageBounds.max + 0.01f, 1f, blockageBounds.max >= 0.49f);
                            result |= blockageBounds;
                        }
                    }
                }
            }
            return result;
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

        private bool DoesLaneBelongToBuilding(Entity laneEntity, Entity targetBuilding)
        {
            Entity currentEntity = laneEntity;
            int maxDepth = 10; // Prevent infinite loops
            int depth = 0;

            while (depth < maxDepth && EntityManager.HasComponent<Game.Common.Owner>(currentEntity))
            {
                var owner = EntityManager.GetComponentData<Game.Common.Owner>(currentEntity);
                currentEntity = owner.m_Owner;

                // Check if current entity is a building
                if (EntityManager.HasComponent<Game.Buildings.Building>(currentEntity))
                {
                    return currentEntity == targetBuilding;
                }

                depth++;
            }

            return false;
        }
    }
}