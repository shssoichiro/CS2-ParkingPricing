using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Game.Net;
using Game.Common;
using Game.Areas;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using Colossal.Mathematics;

namespace ParkingPricing
{
    // Job for calculating district utilization in parallel
    [BurstCompile]
    public struct CalculateDistrictUtilizationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> DistrictEntities;
        [ReadOnly] public NativeArray<Entity> ParkingLanes;
        [ReadOnly] public ComponentLookup<Game.Net.ParkingLane> ParkingLaneData;
        [ReadOnly] public ComponentLookup<Game.Common.Owner> OwnerData;
        [ReadOnly] public ComponentLookup<Game.Areas.BorderDistrict> BorderDistrictData;
        [ReadOnly] public BufferLookup<LaneObject> LaneObjectData;
        [ReadOnly] public BufferLookup<LaneOverlap> LaneOverlapData;
        [ReadOnly] public ComponentLookup<Lane> LaneData;
        [ReadOnly] public ComponentLookup<PrefabRef> PrefabRefData;
        [ReadOnly] public ComponentLookup<Curve> CurveData;
        [ReadOnly] public ComponentLookup<ParkingLaneData> ParkingLaneDataComponents;
        [ReadOnly] public ComponentLookup<ParkedCar> ParkedCarData;
        [ReadOnly] public ComponentLookup<Unspawned> UnspawnedData;
        [ReadOnly] public ComponentLookup<ObjectGeometryData> ObjectGeometryData;
        [ReadOnly] public BufferLookup<Game.Net.SubLane> SubLanes;
        [ReadOnly] public ComponentLookup<Game.Net.CarLane> CarLaneData;

        [WriteOnly] public NativeArray<DistrictUtilizationResult> Results;

        public void Execute(int index)
        {
            Entity districtEntity = DistrictEntities[index];
            double totalCapacity = 0;
            double totalOccupied = 0;

            // Check all parking lanes to see which belong to this district
            for (int i = 0; i < ParkingLanes.Length; i++)
            {
                var laneEntity = ParkingLanes[i];

                if (!ParkingLaneData.HasComponent(laneEntity) || !OwnerData.HasComponent(laneEntity))
                    continue;

                var parkingLane = ParkingLaneData[laneEntity];

                // Skip virtual lanes
                if ((parkingLane.m_Flags & ParkingLaneFlags.VirtualLane) != 0)
                    continue;

                var owner = OwnerData[laneEntity];
                Entity roadEntity = owner.m_Owner;

                // Check if the road has a BorderDistrict component
                if (!BorderDistrictData.HasComponent(roadEntity))
                    continue;

                var borderDistrict = BorderDistrictData[roadEntity];
                bool leftMatch = borderDistrict.m_Left == districtEntity;
                bool rightMatch = borderDistrict.m_Right == districtEntity;

                // Only process this lane if it belongs to our district
                if (leftMatch || rightMatch)
                {
                    // Get lane data directly from the lane entity
                    if (LaneObjectData.HasBuffer(laneEntity) &&
                        LaneOverlapData.HasBuffer(laneEntity) &&
                        LaneData.HasComponent(laneEntity))
                    {
                        var laneOverlaps = LaneOverlapData[laneEntity];
                        var laneData = LaneData[laneEntity];
                        var blockedRange = GetBlockedRange(owner, laneData);

                        int laneCapacity = 0;
                        int laneOccupied = 0;
                        GetStreetParkingLaneCapacity(laneEntity, parkingLane, laneOverlaps, blockedRange, ref laneCapacity, ref laneOccupied);

                        // Determine weight based on district ownership
                        double weight = (leftMatch && rightMatch) ? 1.0 : 0.5;
                        totalCapacity += laneCapacity * weight;
                        totalOccupied += laneOccupied * weight;
                    }
                }
            }

            Results[index] = new DistrictUtilizationResult
            {
                DistrictEntity = districtEntity,
                Utilization = totalCapacity > 0 ? totalOccupied / totalCapacity : 0.0
            };
        }

        private void GetStreetParkingLaneCapacity(Entity subLane, Game.Net.ParkingLane parkingLane, DynamicBuffer<LaneOverlap> laneOverlaps, Bounds1 blockedRange, ref int slotCapacity, ref int parkedCars)
        {
            // Get parking slot count using game's method
            var prefab = PrefabRefData[subLane].m_Prefab;
            var curve = CurveData[subLane];
            var parkingLaneData = ParkingLaneDataComponents[prefab];
            var laneObjects = LaneObjectData[subLane];

            if (parkingLaneData.m_SlotInterval != 0f)
            {
                int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
                slotCapacity += parkingSlotCount;

                // Count parked cars in this lane
                for (int j = 0; j < laneObjects.Length; j++)
                {
                    if (ParkedCarData.HasComponent(laneObjects[j].m_LaneObject))
                    {
                        parkedCars++;
                    }
                }
                return;
            }

            // Complex capacity calculation for lanes without slot intervals
            float standardCarLength = parkingLaneData.m_MaxCarLength != 0f ? parkingLaneData.m_MaxCarLength : ParkingPricingConstants.STANDARD_CAR_LENGTH;

            int freeParkingSpaces = 0;
            float2 currentOffsets = math.select(0f, 0.5f, (parkingLane.m_Flags & ParkingLaneFlags.StartingLane) == 0);
            float3 currentPosition = curve.m_Bezier.a;

            // Initialize variables for tracking parked cars along the curve
            float nextCarPosition = 2f; // 2f means no more cars (beyond curve end)
            float2 nextCarOffsets = 0f;
            int carIndex = 0;

            // Find the first parked car along the curve
            while (carIndex < laneObjects.Length)
            {
                var currentLaneObject = laneObjects[carIndex++];
                if (ParkedCarData.HasComponent(currentLaneObject.m_LaneObject) && !UnspawnedData.HasComponent(currentLaneObject.m_LaneObject))
                {
                    nextCarPosition = currentLaneObject.m_CurvePosition.x;
                    nextCarOffsets = VehicleUtils.GetParkingOffsets(currentLaneObject.m_LaneObject, ref PrefabRefData, ref ObjectGeometryData) + 1f;
                    break;
                }
            }

            // Initialize variables for tracking lane overlaps (intersections, etc.)
            float2 nextOverlapRange = 2f; // 2f means no more overlaps
            int overlapIndex = 0;

            // Find the first lane overlap
            if (overlapIndex < laneOverlaps.Length)
            {
                var currentOverlap = laneOverlaps[overlapIndex++];
                nextOverlapRange = new float2((int)currentOverlap.m_ThisStart, (int)currentOverlap.m_ThisEnd) * ParkingPricingConstants.LANE_POSITION_MULTIPLIER;
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
                        var nextLaneObject = laneObjects[carIndex++];
                        if (ParkedCarData.HasComponent(nextLaneObject.m_LaneObject) && !UnspawnedData.HasComponent(nextLaneObject.m_LaneObject))
                        {
                            nextCarPosition = nextLaneObject.m_CurvePosition.x;
                            nextCarOffsets = VehicleUtils.GetParkingOffsets(nextLaneObject.m_LaneObject, ref PrefabRefData, ref ObjectGeometryData) + 1f;
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
                        var nextOverlap = laneOverlaps[overlapIndex++];
                        float2 nextOverlapNormalized = new float2((int)nextOverlap.m_ThisStart, (int)nextOverlap.m_ThisEnd) * ParkingPricingConstants.LANE_POSITION_MULTIPLIER;
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

                // Calculate how many cars can fit in this segment and add to total
                if (segmentLength > 0)
                {
                    int spacesInSegment = (int)math.floor(segmentLength / standardCarLength);
                    freeParkingSpaces += spacesInSegment;
                }

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

            // Calculate spaces in the final segment and add to total
            if (segmentLength > 0)
            {
                int spacesInFinalSegment = (int)math.floor(segmentLength / standardCarLength);
                freeParkingSpaces += spacesInFinalSegment;
            }

            // Update slot capacity with the calculated total parking spaces
            slotCapacity += freeParkingSpaces + laneObjects.Length;

            // Count all objects in the lane as parked cars for utilization calculation
            parkedCars += laneObjects.Length;
        }

        private Bounds1 GetBlockedRange(Owner owner, Lane laneData)
        {
            Bounds1 result = new Bounds1(2f, -1f);
            if (SubLanes.HasBuffer(owner.m_Owner))
            {
                var dynamicBuffer = SubLanes[owner.m_Owner];
                for (int i = 0; i < dynamicBuffer.Length; i++)
                {
                    Entity subLane = dynamicBuffer[i].m_SubLane;
                    Lane lane = LaneData[subLane];
                    if (laneData.m_StartNode.EqualsIgnoreCurvePos(lane.m_MiddleNode) && CarLaneData.HasComponent(subLane))
                    {
                        var carLane = CarLaneData[subLane];
                        if (carLane.m_BlockageEnd >= carLane.m_BlockageStart)
                        {
                            Bounds1 blockageBounds = carLane.blockageBounds;
                            blockageBounds.min = math.select(blockageBounds.min - ParkingPricingConstants.BLOCKED_RANGE_BUFFER, 0f, blockageBounds.min <= ParkingPricingConstants.LANE_BOUNDARY_INVERSE_THRESHOLD);
                            blockageBounds.max = math.select(blockageBounds.max + ParkingPricingConstants.BLOCKED_RANGE_BUFFER, 1f, blockageBounds.max >= ParkingPricingConstants.LANE_BOUNDARY_THRESHOLD);
                            result |= blockageBounds;
                        }
                    }
                }
            }
            return result;
        }
    }

    // Job for calculating building utilization in parallel
    [BurstCompile]
    public struct CalculateBuildingUtilizationJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<Entity> BuildingEntities;
        [ReadOnly] public NativeArray<Entity> ParkingLanes;
        [ReadOnly] public NativeArray<Entity> GarageLanes;
        [ReadOnly] public ComponentLookup<Game.Net.ParkingLane> ParkingLaneData;
        [ReadOnly] public ComponentLookup<Game.Net.GarageLane> GarageLaneData;
        [ReadOnly] public ComponentLookup<Game.Common.Owner> OwnerData;
        [ReadOnly] public ComponentLookup<Game.Buildings.Building> BuildingData;
        [ReadOnly] public BufferLookup<LaneObject> LaneObjectData;
        [ReadOnly] public ComponentLookup<PrefabRef> PrefabRefData;
        [ReadOnly] public ComponentLookup<Curve> CurveData;
        [ReadOnly] public ComponentLookup<ParkingLaneData> ParkingLaneDataComponents;
        [ReadOnly] public ComponentLookup<ParkedCar> ParkedCarData;

        [WriteOnly] public NativeArray<BuildingUtilizationResult> Results;

        public void Execute(int index)
        {
            Entity buildingEntity = BuildingEntities[index];
            int slotCapacity = 0;
            int parkedCars = 0;

            // Check parking lanes that belong to this building
            for (int i = 0; i < ParkingLanes.Length; i++)
            {
                Entity laneEntity = ParkingLanes[i];
                if (DoesLaneBelongToBuilding(laneEntity, buildingEntity))
                {
                    if (ParkingLaneData.HasComponent(laneEntity))
                    {
                        var parkingLane = ParkingLaneData[laneEntity];

                        // Skip virtual lanes
                        if ((parkingLane.m_Flags & ParkingLaneFlags.VirtualLane) != 0)
                            continue;

                        GetBuildingParkingLaneCounts(laneEntity, parkingLane, ref slotCapacity, ref parkedCars);
                    }
                }
            }

            // Check garage lanes that belong to this building
            for (int i = 0; i < GarageLanes.Length; i++)
            {
                Entity laneEntity = GarageLanes[i];
                if (DoesLaneBelongToBuilding(laneEntity, buildingEntity))
                {
                    if (GarageLaneData.HasComponent(laneEntity))
                    {
                        var garageLane = GarageLaneData[laneEntity];
                        slotCapacity += garageLane.m_VehicleCapacity;
                        parkedCars += garageLane.m_VehicleCount;
                    }
                }
            }

            Results[index] = new BuildingUtilizationResult
            {
                BuildingEntity = buildingEntity,
                Utilization = slotCapacity > 0 ? (double)parkedCars / slotCapacity : 0.0
            };
        }

        private void GetBuildingParkingLaneCounts(Entity subLane, Game.Net.ParkingLane parkingLane, ref int slotCapacity, ref int parkedCars)
        {
            // Get parking slot count using game's method
            Entity prefab = PrefabRefData[subLane].m_Prefab;
            Curve curve = CurveData[subLane];
            var parkingLaneData = ParkingLaneDataComponents[prefab];

            if (parkingLaneData.m_SlotInterval != 0f)
            {
                int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
                slotCapacity += parkingSlotCount;
            }

            // Count parked cars in this lane
            if (LaneObjectData.HasBuffer(subLane))
            {
                var laneObjects = LaneObjectData[subLane];
                for (int j = 0; j < laneObjects.Length; j++)
                {
                    if (ParkedCarData.HasComponent(laneObjects[j].m_LaneObject))
                    {
                        parkedCars++;
                    }
                }
            }
        }

        private bool DoesLaneBelongToBuilding(Entity laneEntity, Entity targetBuilding)
        {
            Entity currentEntity = laneEntity;
            int depth = 0;

            while (depth < ParkingPricingConstants.MAX_OWNERSHIP_DEPTH && OwnerData.HasComponent(currentEntity))
            {
                var owner = OwnerData[currentEntity];
                currentEntity = owner.m_Owner;

                // Check if current entity is a building
                if (BuildingData.HasComponent(currentEntity))
                {
                    return currentEntity == targetBuilding;
                }

                depth++;
            }

            return false;
        }
    }

    // Job for applying price updates to entities using EntityCommandBuffer for immediate application
    [BurstCompile]
    public struct ApplyPricingWithECBJob : IJob
    {
        [ReadOnly] public NativeArray<DistrictUtilizationResult> DistrictResults;
        [ReadOnly] public NativeArray<BuildingUtilizationResult> BuildingResults;
        [ReadOnly] public int BaseStreetPrice;
        [ReadOnly] public int MaxStreetPrice;
        [ReadOnly] public int MinStreetPrice;
        [ReadOnly] public int BaseLotPrice;
        [ReadOnly] public int MaxLotPrice;
        [ReadOnly] public int MinLotPrice;
        [ReadOnly] public Entity StreetParkingFeePrefab;
        [ReadOnly] public Entity LotParkingFeePrefab;

        public EntityCommandBuffer EntityCommandBuffer;

        public void Execute()
        {
            // Process district results
            for (int i = 0; i < DistrictResults.Length; i++)
            {
                var result = DistrictResults[i];
                int newPrice = PricingCalculator.CalculateAdjustedPrice(
                    BaseStreetPrice, MaxStreetPrice, MinStreetPrice, result.Utilization);

                // Use ECB to schedule policy update
                EntityCommandBuffer.AddComponent(result.DistrictEntity, new PolicyUpdateCommand
                {
                    PolicyPrefab = StreetParkingFeePrefab,
                    NewPrice = newPrice,
                    IsDistrict = true,
                    Utilization = result.Utilization
                });
            }

            // Process building results
            for (int i = 0; i < BuildingResults.Length; i++)
            {
                var result = BuildingResults[i];
                int newPrice = PricingCalculator.CalculateAdjustedPrice(
                    BaseLotPrice, MaxLotPrice, MinLotPrice, result.Utilization);

                // Use ECB to schedule policy update
                EntityCommandBuffer.AddComponent(result.BuildingEntity, new PolicyUpdateCommand
                {
                    PolicyPrefab = LotParkingFeePrefab,
                    NewPrice = newPrice,
                    IsDistrict = false,
                    Utilization = result.Utilization
                });
            }
        }
    }

    // Component to signal a policy update request
    public struct PolicyUpdateCommand : IComponentData
    {
        public Entity PolicyPrefab;
        public int NewPrice;
        public bool IsDistrict;
        public double Utilization;
    }

    // Job for applying price updates to entities
    [BurstCompile]
    public struct ApplyPricingUpdatesJob : IJob
    {
        [ReadOnly] public NativeArray<DistrictUtilizationResult> DistrictResults;
        [ReadOnly] public NativeArray<BuildingUtilizationResult> BuildingResults;
        [ReadOnly] public int BaseStreetPrice;
        [ReadOnly] public int MaxStreetPrice;
        [ReadOnly] public int MinStreetPrice;
        [ReadOnly] public int BaseLotPrice;
        [ReadOnly] public int MaxLotPrice;
        [ReadOnly] public int MinLotPrice;

        [WriteOnly] public NativeArray<PricingUpdate> PricingUpdates;

        public void Execute()
        {
            int updateIndex = 0;

            // Process district results
            for (int i = 0; i < DistrictResults.Length; i++)
            {
                var result = DistrictResults[i];
                int newPrice = PricingCalculator.CalculateAdjustedPrice(
                    BaseStreetPrice, MaxStreetPrice, MinStreetPrice, result.Utilization);

                PricingUpdates[updateIndex++] = new PricingUpdate
                {
                    Entity = result.DistrictEntity,
                    NewPrice = newPrice,
                    IsDistrict = true,
                    Utilization = result.Utilization
                };
            }

            // Process building results
            for (int i = 0; i < BuildingResults.Length; i++)
            {
                var result = BuildingResults[i];
                int newPrice = PricingCalculator.CalculateAdjustedPrice(
                    BaseLotPrice, MaxLotPrice, MinLotPrice, result.Utilization);

                PricingUpdates[updateIndex++] = new PricingUpdate
                {
                    Entity = result.BuildingEntity,
                    NewPrice = newPrice,
                    IsDistrict = false,
                    Utilization = result.Utilization
                };
            }
        }
    }

    // Result structures for job communication
    public struct DistrictUtilizationResult
    {
        public Entity DistrictEntity;
        public double Utilization;
    }

    public struct BuildingUtilizationResult
    {
        public Entity BuildingEntity;
        public double Utilization;
    }

    public struct PricingUpdate
    {
        public Entity Entity;
        public int NewPrice;
        public bool IsDistrict;
        public double Utilization;
    }
}