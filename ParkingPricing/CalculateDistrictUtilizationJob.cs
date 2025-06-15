using Colossal.Mathematics;
using Game.Areas;
using Game.Common;
using Game.Net;
using Game.Objects;
using Game.Prefabs;
using Game.Vehicles;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using CarLane = Game.Net.CarLane;
using ParkingLane = Game.Net.ParkingLane;
using SubLane = Game.Net.SubLane;

namespace ParkingPricing {
    // Job for calculating district utilization in parallel
    [BurstCompile]
    public struct CalculateDistrictUtilizationJob : IJobParallelFor {
        [ReadOnly] public NativeArray<Entity> DistrictEntities;
        [ReadOnly] public NativeArray<Entity> ParkingLanes;
        [ReadOnly] public ComponentLookup<ParkingLane> ParkingLaneData;
        [ReadOnly] public ComponentLookup<Owner> OwnerData;
        [ReadOnly] public ComponentLookup<BorderDistrict> BorderDistrictData;
        [ReadOnly] public BufferLookup<LaneObject> LaneObjectData;
        [ReadOnly] public BufferLookup<LaneOverlap> LaneOverlapData;
        [ReadOnly] public ComponentLookup<Lane> LaneData;
        [ReadOnly] public ComponentLookup<PrefabRef> PrefabRefData;
        [ReadOnly] public ComponentLookup<Curve> CurveData;
        [ReadOnly] public ComponentLookup<ParkingLaneData> ParkingLaneDataComponents;
        [ReadOnly] public ComponentLookup<ParkedCar> ParkedCarData;
        [ReadOnly] public ComponentLookup<Unspawned> UnspawnedData;
        [ReadOnly] public ComponentLookup<ObjectGeometryData> ObjectGeometryData;
        [ReadOnly] public BufferLookup<SubLane> SubLanes;
        [ReadOnly] public ComponentLookup<CarLane> CarLaneData;

        [WriteOnly] public NativeList<DistrictUtilizationResult> Results;

        public void Execute(int index) {
            Entity districtEntity = DistrictEntities[index];
            double totalCapacity = 0;
            double totalOccupied = 0;

            // Check all parking lanes to see which belong to this district
            foreach (Entity laneEntity in ParkingLanes) {
                if (!ParkingLaneData.TryGetComponent(laneEntity, out ParkingLane parkingLane)
                    || !OwnerData.TryGetComponent(laneEntity, out Owner owner)) {
                    continue;
                }

                // Skip virtual lanes
                if ((parkingLane.m_Flags & ParkingLaneFlags.VirtualLane) != 0) {
                    continue;
                }

                Entity roadEntity = owner.m_Owner;

                // Check if the road has a BorderDistrict component
                if (!BorderDistrictData.TryGetComponent(roadEntity, out BorderDistrict borderDistrict)) {
                    continue;
                }

                bool leftMatch = borderDistrict.m_Left == districtEntity;
                bool rightMatch = borderDistrict.m_Right == districtEntity;

                // Only process this lane if it belongs to our district
                if (!leftMatch && !rightMatch) {
                    continue;
                }

                if (!LaneObjectData.TryGetBuffer(laneEntity, out DynamicBuffer<LaneObject> laneObjects)
                    || !LaneOverlapData.TryGetBuffer(laneEntity, out DynamicBuffer<LaneOverlap> laneOverlaps)
                    || !LaneData.TryGetComponent(laneEntity, out Lane laneData)) {
                    continue;
                }

                int laneCapacity = 0;
                int laneOccupied = 0;
                Bounds1 blockedRange = GetBlockedRange(owner, laneData);
                GetStreetParkingLaneCapacity(
                    laneEntity, parkingLane, laneObjects, laneOverlaps, blockedRange, ref laneCapacity, ref laneOccupied
                );

                // Determine weight based on district ownership
                double weight = leftMatch && rightMatch ? 1.0 : 0.5;
                totalCapacity += laneCapacity * weight;
                totalOccupied += laneOccupied * weight;
            }

            Results.Add(
                new DistrictUtilizationResult {
                    DistrictEntity = districtEntity,
                    Utilization = totalCapacity > 0 ? totalOccupied / totalCapacity : 0.0
                }
            );
        }

        private void GetStreetParkingLaneCapacity(
            Entity subLane, ParkingLane parkingLane, DynamicBuffer<LaneObject> laneObjects,
            DynamicBuffer<LaneOverlap> laneOverlaps, Bounds1 blockedRange, ref int slotCapacity, ref int parkedCars
        ) {
            // Get parking slot count using game's method
            Entity prefab = PrefabRefData[subLane].m_Prefab;
            Curve curve = CurveData[subLane];
            ParkingLaneData parkingLaneData = ParkingLaneDataComponents[prefab];

            if (parkingLaneData.m_SlotInterval != 0f) {
                int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
                slotCapacity += parkingSlotCount;

                // Count parked cars in this lane
                foreach (LaneObject laneObject in laneObjects) {
                    if (ParkedCarData.HasComponent(laneObject.m_LaneObject)) {
                        parkedCars++;
                    }
                }

                return;
            }

            // Complex capacity calculation for lanes without slot intervals
            float standardCarLength = parkingLaneData.m_MaxCarLength != 0f
                ? parkingLaneData.m_MaxCarLength
                : ParkingPricingConstants.StandardCarLength;

            int freeParkingSpaces = 0;
            float2 currentOffsets = math.select(0f, 0.5f, (parkingLane.m_Flags & ParkingLaneFlags.StartingLane) == 0);
            float3 currentPosition = curve.m_Bezier.a;

            // Initialize variables for tracking parked cars along the curve
            float nextCarPosition = 2f; // 2f means no more cars (beyond curve end)
            float2 nextCarOffsets = 0f;
            int carIndex = 0;

            // Find the first parked car along the curve
            while (carIndex < laneObjects.Length) {
                LaneObject currentLaneObject = laneObjects[carIndex++];
                if (!ParkedCarData.HasComponent(currentLaneObject.m_LaneObject)
                    || UnspawnedData.HasComponent(currentLaneObject.m_LaneObject)) {
                    continue;
                }

                nextCarPosition = currentLaneObject.m_CurvePosition.x;
                nextCarOffsets = VehicleUtils.GetParkingOffsets(
                    currentLaneObject.m_LaneObject, ref PrefabRefData, ref ObjectGeometryData
                ) + 1f;
                break;
            }

            // Initialize variables for tracking lane overlaps (intersections, etc.)
            float2 nextOverlapRange = 2f; // 2f means no more overlaps
            int overlapIndex = 0;

            // Find the first lane overlap
            if (overlapIndex < laneOverlaps.Length) {
                LaneOverlap currentOverlap = laneOverlaps[overlapIndex++];
                nextOverlapRange = new float2(currentOverlap.m_ThisStart, currentOverlap.m_ThisEnd)
                                   * ParkingPricingConstants.LanePositionMultiplier;
            }

            // Initialize variables for handling blocked ranges (areas where parking is prohibited)
            var blockedCenterPosition = default(float3);
            var blockedDistances = default(float3);
            if (blockedRange.max >= blockedRange.min) {
                blockedCenterPosition = MathUtils.Position(curve.m_Bezier, MathUtils.Center(blockedRange));
                blockedDistances.x = math.distance(
                    MathUtils.Position(curve.m_Bezier, blockedRange.min), blockedCenterPosition
                );
                blockedDistances.y = math.distance(
                    MathUtils.Position(curve.m_Bezier, blockedRange.max), blockedCenterPosition
                );
            }

            // Main loop: iterate through all obstacles (cars and overlaps) along the curve
            float segmentLength;
            while (!Mathf.Approximately(nextCarPosition, 2f) || !Mathf.Approximately(nextOverlapRange.x, 2f)) {
                float2 obstacleRange;
                float nextObstacleEndOffset;

                // Determine which obstacle comes first: parked car or lane overlap
                if (nextCarPosition <= nextOverlapRange.x) {
                    // Process parked car obstacle
                    obstacleRange = nextCarPosition;
                    currentOffsets.y = nextCarOffsets.x;
                    nextObstacleEndOffset = nextCarOffsets.y;
                    nextCarPosition = 2f; // Reset to indicate no more cars until we find the next one

                    // Find next parked car
                    while (carIndex < laneObjects.Length) {
                        LaneObject nextLaneObject = laneObjects[carIndex++];
                        if (!ParkedCarData.HasComponent(nextLaneObject.m_LaneObject)
                            || UnspawnedData.HasComponent(nextLaneObject.m_LaneObject)) {
                            continue;
                        }

                        nextCarPosition = nextLaneObject.m_CurvePosition.x;
                        nextCarOffsets = VehicleUtils.GetParkingOffsets(
                            nextLaneObject.m_LaneObject, ref PrefabRefData, ref ObjectGeometryData
                        ) + 1f;
                        break;
                    }
                } else {
                    // Process lane overlap obstacle
                    obstacleRange = nextOverlapRange;
                    currentOffsets.y = 0.5f;
                    nextObstacleEndOffset = 0.5f;
                    nextOverlapRange = 2f; // Reset to indicate no more overlaps until we find the next one

                    // Find next lane overlap, merging consecutive overlaps
                    while (overlapIndex < laneOverlaps.Length) {
                        LaneOverlap nextOverlap = laneOverlaps[overlapIndex++];
                        float2 nextOverlapNormalized = new float2(nextOverlap.m_ThisStart, nextOverlap.m_ThisEnd)
                                                       * ParkingPricingConstants.LanePositionMultiplier;
                        if (nextOverlapNormalized.x <= obstacleRange.y) {
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
                if (blockedRange.max >= blockedRange.min) {
                    float distanceToBlockedStart = math.distance(currentPosition, blockedCenterPosition)
                                                   - currentOffsets.x - blockedDistances.x;
                    float distanceToBlockedEnd = math.distance(segmentEndPosition, blockedCenterPosition)
                                                 - currentOffsets.y - blockedDistances.y;
                    segmentLength = math.min(segmentLength, math.max(distanceToBlockedStart, distanceToBlockedEnd));
                }

                // Calculate how many cars can fit in this segment and add to total
                if (segmentLength > 0) {
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
            if (blockedRange.max >= blockedRange.min) {
                float distanceToBlockedStart = math.distance(currentPosition, blockedCenterPosition) - currentOffsets.x
                    - blockedDistances.x;
                float distanceToBlockedEnd = math.distance(curve.m_Bezier.d, blockedCenterPosition) - currentOffsets.y
                    - blockedDistances.y;
                segmentLength = math.min(segmentLength, math.max(distanceToBlockedStart, distanceToBlockedEnd));
            }

            // Calculate spaces in the final segment and add to total
            if (segmentLength > 0) {
                int spacesInFinalSegment = (int)math.floor(segmentLength / standardCarLength);
                freeParkingSpaces += spacesInFinalSegment;
            }

            // Update slot capacity with the calculated total parking spaces
            slotCapacity += freeParkingSpaces + laneObjects.Length;

            // Count all objects in the lane as parked cars for utilization calculation
            parkedCars += laneObjects.Length;
        }

        private Bounds1 GetBlockedRange(Owner owner, Lane laneData) {
            var result = new Bounds1(2f, -1f);
            if (!SubLanes.TryGetBuffer(owner.m_Owner, out DynamicBuffer<SubLane> dynamicBuffer)) {
                return result;
            }

            for (int i = 0; i < dynamicBuffer.Length; i++) {
                Entity subLane = dynamicBuffer[i].m_SubLane;
                Lane lane = LaneData[subLane];
                if (!laneData.m_StartNode.EqualsIgnoreCurvePos(lane.m_MiddleNode)
                    || !CarLaneData.TryGetComponent(subLane, out CarLane carLane)) {
                    continue;
                }

                if (carLane.m_BlockageEnd < carLane.m_BlockageStart) {
                    continue;
                }

                Bounds1 blockageBounds = carLane.blockageBounds;
                blockageBounds.min = math.select(
                    blockageBounds.min - ParkingPricingConstants.BlockedRangeBuffer, 0f,
                    blockageBounds.min <= ParkingPricingConstants.LaneBoundaryInverseThreshold
                );
                blockageBounds.max = math.select(
                    blockageBounds.max + ParkingPricingConstants.BlockedRangeBuffer, 1f,
                    blockageBounds.max >= ParkingPricingConstants.LaneBoundaryThreshold
                );
                result |= blockageBounds;
            }

            return result;
        }
    }
}
