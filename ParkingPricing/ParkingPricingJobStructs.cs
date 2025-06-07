using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Game.Net;
using Game.Common;
using Game.Areas;

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
        [ReadOnly] public ComponentLookup<Game.Net.ParkingLane> ParkingLaneComponentData;

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
                    // Calculate capacity and occupancy for this lane (simplified for job)
                    int laneCapacity = 0;
                    int laneOccupied = 0;

                    if (LaneObjectData.HasBuffer(laneEntity))
                    {
                        var laneObjects = LaneObjectData[laneEntity];
                        laneCapacity = math.max(10, laneObjects.Length); // Simplified capacity calculation
                        laneOccupied = laneObjects.Length;
                    }

                    // Determine weight based on district ownership
                    double weight = (leftMatch && rightMatch) ? 1.0 : 0.5;
                    totalCapacity += laneCapacity * weight;
                    totalOccupied += laneOccupied * weight;
                }
            }

            Results[index] = new DistrictUtilizationResult
            {
                DistrictEntity = districtEntity,
                Utilization = totalCapacity > 0 ? totalOccupied / totalCapacity : 0.0
            };
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

                        // Simplified capacity calculation for job
                        if (LaneObjectData.HasBuffer(laneEntity))
                        {
                            var laneObjects = LaneObjectData[laneEntity];
                            slotCapacity += math.max(5, laneObjects.Length); // Simplified
                            parkedCars += laneObjects.Length;
                        }
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