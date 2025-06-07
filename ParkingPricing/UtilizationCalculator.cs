using Colossal.Mathematics;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace ParkingPricing
{
    // Extracted utilization calculation logic following SRP
    public class UtilizationCalculator
    {
        private readonly EntityManager entityManager;
        private readonly BufferLookup<Game.Net.SubLane> m_SubLanes;
        private readonly ComponentLookup<ParkedCar> m_ParkedCarData;
        private readonly ComponentLookup<Unspawned> m_UnspawnedData;
        private readonly ComponentLookup<PrefabRef> m_PrefabRefData;
        private readonly ComponentLookup<ObjectGeometryData> m_ObjectGeometryData;
        private readonly ComponentLookup<Lane> m_LaneType;
        private readonly BufferLookup<LaneObject> m_LaneObjectType;
        private readonly BufferLookup<LaneOverlap> m_LaneOverlapType;
        private readonly ComponentLookup<Lane> m_LaneData;
        private readonly ComponentLookup<Game.Net.CarLane> m_CarLaneData;

        public UtilizationCalculator(EntityManager entityManager,
            BufferLookup<Game.Net.SubLane> subLanes,
            ComponentLookup<ParkedCar> parkedCarData,
            ComponentLookup<Unspawned> unspawnedData,
            ComponentLookup<PrefabRef> prefabRefData,
            ComponentLookup<ObjectGeometryData> objectGeometryData,
            ComponentLookup<Lane> laneType,
            BufferLookup<LaneObject> laneObjectType,
            BufferLookup<LaneOverlap> laneOverlapType,
            ComponentLookup<Lane> laneData,
            ComponentLookup<Game.Net.CarLane> carLaneData)
        {
            this.entityManager = entityManager;
            m_SubLanes = subLanes;
            m_ParkedCarData = parkedCarData;
            m_UnspawnedData = unspawnedData;
            m_PrefabRefData = prefabRefData;
            m_ObjectGeometryData = objectGeometryData;
            m_LaneType = laneType;
            m_LaneObjectType = laneObjectType;
            m_LaneOverlapType = laneOverlapType;
            m_LaneData = laneData;
            m_CarLaneData = carLaneData;
        }

        public double CalculateBuildingUtilization(Entity buildingEntity, NativeArray<Entity> parkingLanes, NativeArray<Entity> garageLanes)
        {
            int slotCapacity = 0;
            int parkedCars = 0;

            // Check parking lanes that belong to this building
            foreach (Entity laneEntity in parkingLanes)
            {
                if (DoesLaneBelongToBuilding(laneEntity, buildingEntity))
                {
                    var parkingLane = entityManager.GetComponentData<Game.Net.ParkingLane>(laneEntity);

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
                    var garageLane = entityManager.GetComponentData<Game.Net.GarageLane>(laneEntity);
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
            Entity prefab = entityManager.GetComponentData<PrefabRef>(subLane).m_Prefab;
            Curve curve = entityManager.GetComponentData<Curve>(subLane);
            var parkingLaneData = entityManager.GetComponentData<ParkingLaneData>(prefab);

            if (parkingLaneData.m_SlotInterval != 0f)
            {
                int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
                slotCapacity += parkingSlotCount;
            }

            // Count parked cars in this lane
            if (entityManager.HasBuffer<LaneObject>(subLane))
            {
                var laneObjects = entityManager.GetBuffer<LaneObject>(subLane, true);
                for (int j = 0; j < laneObjects.Length; j++)
                {
                    if (entityManager.HasComponent<ParkedCar>(laneObjects[j].m_LaneObject))
                    {
                        parkedCars++;
                    }
                }
            }
            LogUtil.Debug($"Found building parking lane: SlotCapacity={slotCapacity}, ParkedCars={parkedCars}");
        }

        public double CalculateDistrictUtilization(ArchetypeChunk chunk, Entity districtEntity, NativeArray<Entity> parkingLanes)
        {
            double totalCapacity = 0;
            double totalOccupied = 0;

            // Check all parking lanes to see which belong to this district
            for (int i = 0; i < parkingLanes.Length; i++)
            {
                var laneEntity = parkingLanes[i];
                if (entityManager.HasComponent<Game.Net.ParkingLane>(laneEntity) &&
                    entityManager.HasComponent<Game.Common.Owner>(laneEntity))
                {
                    var parkingLane = entityManager.GetComponentData<Game.Net.ParkingLane>(laneEntity);

                    // Skip virtual lanes
                    if ((parkingLane.m_Flags & ParkingLaneFlags.VirtualLane) != 0)
                    {
                        continue;
                    }

                    var owner = entityManager.GetComponentData<Game.Common.Owner>(laneEntity);
                    Entity roadEntity = owner.m_Owner;

                    // Check if the road has a BorderDistrict component
                    if (entityManager.HasComponent<Game.Areas.BorderDistrict>(roadEntity))
                    {
                        var borderDistrict = entityManager.GetComponentData<Game.Areas.BorderDistrict>(roadEntity);

                        bool leftMatch = borderDistrict.m_Left == districtEntity;
                        bool rightMatch = borderDistrict.m_Right == districtEntity;

                        // Only process this lane if it belongs to our district
                        if (leftMatch || rightMatch)
                        {
                            // Calculate capacity and occupancy for this lane
                            int laneCapacity = 0;
                            int laneOccupied = 0;

                            // Get lane data directly from the lane entity
                            if (entityManager.HasBuffer<LaneObject>(laneEntity) &&
                                entityManager.HasBuffer<LaneOverlap>(laneEntity) &&
                                entityManager.HasComponent<Lane>(laneEntity))
                            {
                                DynamicBuffer<LaneOverlap> laneOverlaps = entityManager.GetBuffer<LaneOverlap>(laneEntity, true);
                                Lane laneData = entityManager.GetComponentData<Lane>(laneEntity);
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

        private void GetStreetParkingLaneCapacity(Entity subLane, Game.Net.ParkingLane parkingLane, DynamicBuffer<LaneOverlap> laneOverlaps, Bounds1 blockedRange, ref int slotCapacity, ref int parkedCars)
        {
            // Get parking slot count using game's method
            Entity prefab = entityManager.GetComponentData<PrefabRef>(subLane).m_Prefab;
            Curve curve = entityManager.GetComponentData<Curve>(subLane);
            var parkingLaneData = entityManager.GetComponentData<ParkingLaneData>(prefab);
            var laneObjects = entityManager.GetBuffer<LaneObject>(subLane, true);

            if (parkingLaneData.m_SlotInterval != 0f)
            {
                int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
                slotCapacity += parkingSlotCount;

                // Count parked cars in this lane
                for (int j = 0; j < laneObjects.Length; j++)
                {
                    if (entityManager.HasComponent<ParkedCar>(laneObjects[j].m_LaneObject))
                    {
                        parkedCars++;
                    }
                }
                return;
            }

            // Determine the standard car length for calculating parking spaces
            float standardCarLength = parkingLaneData.m_MaxCarLength != 0f ? parkingLaneData.m_MaxCarLength : ParkingPricingConstants.STANDARD_CAR_LENGTH;

            // Initialize variables for calculating total parking spaces between obstacles
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
                LaneObject currentLaneObject = laneObjects[carIndex++];
                if (m_ParkedCarData.HasComponent(currentLaneObject.m_LaneObject) && !m_UnspawnedData.HasComponent(currentLaneObject.m_LaneObject))
                {
                    nextCarPosition = currentLaneObject.m_CurvePosition.x;
                    var tempPrefabRef = m_PrefabRefData;
                    var tempObjectGeometry = m_ObjectGeometryData;
                    nextCarOffsets = VehicleUtils.GetParkingOffsets(currentLaneObject.m_LaneObject, ref tempPrefabRef, ref tempObjectGeometry) + 1f;
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
                        LaneObject nextLaneObject = laneObjects[carIndex++];
                        if (m_ParkedCarData.HasComponent(nextLaneObject.m_LaneObject) && !m_UnspawnedData.HasComponent(nextLaneObject.m_LaneObject))
                        {
                            nextCarPosition = nextLaneObject.m_CurvePosition.x;
                            var tempPrefabRef = m_PrefabRefData;
                            var tempObjectGeometry = m_ObjectGeometryData;
                            nextCarOffsets = VehicleUtils.GetParkingOffsets(nextLaneObject.m_LaneObject, ref tempPrefabRef, ref tempObjectGeometry) + 1f;
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

            LogUtil.Debug($"Found street parking lane: SlotCapacity={slotCapacity}, ParkedCars={parkedCars}");
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
                            blockageBounds.min = math.select(blockageBounds.min - ParkingPricingConstants.BLOCKED_RANGE_BUFFER, 0f, blockageBounds.min <= ParkingPricingConstants.LANE_BOUNDARY_INVERSE_THRESHOLD);
                            blockageBounds.max = math.select(blockageBounds.max + ParkingPricingConstants.BLOCKED_RANGE_BUFFER, 1f, blockageBounds.max >= ParkingPricingConstants.LANE_BOUNDARY_THRESHOLD);
                            result |= blockageBounds;
                        }
                    }
                }
            }
            return result;
        }

        private bool DoesLaneBelongToBuilding(Entity laneEntity, Entity targetBuilding)
        {
            Entity currentEntity = laneEntity;
            int depth = 0;

            while (depth < ParkingPricingConstants.MAX_OWNERSHIP_DEPTH && entityManager.HasComponent<Game.Common.Owner>(currentEntity))
            {
                var owner = entityManager.GetComponentData<Game.Common.Owner>(currentEntity);
                currentEntity = owner.m_Owner;

                // Check if current entity is a building
                if (entityManager.HasComponent<Game.Buildings.Building>(currentEntity))
                {
                    return currentEntity == targetBuilding;
                }

                depth++;
            }

            return false;
        }
    }
}