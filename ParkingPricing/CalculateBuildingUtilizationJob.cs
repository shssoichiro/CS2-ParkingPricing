using Game.Buildings;
using Game.Common;
using Game.Net;
using Game.Prefabs;
using Game.Vehicles;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using ParkingLane = Game.Net.ParkingLane;

namespace ParkingPricing {
    // Job for calculating building utilization in parallel
    [BurstCompile]
    public struct CalculateBuildingUtilizationJob : IJobParallelFor {
        [ReadOnly] public NativeList<Entity> BuildingEntities;
        [ReadOnly] public NativeArray<Entity> ParkingLanes;
        [ReadOnly] public NativeArray<Entity> GarageLanes;
        [ReadOnly] public ComponentLookup<ParkingLane> ParkingLaneData;
        [ReadOnly] public ComponentLookup<GarageLane> GarageLaneData;
        [ReadOnly] public ComponentLookup<Owner> OwnerData;
        [ReadOnly] public ComponentLookup<Building> BuildingData;
        [ReadOnly] public BufferLookup<LaneObject> LaneObjectData;
        [ReadOnly] public ComponentLookup<PrefabRef> PrefabRefData;
        [ReadOnly] public ComponentLookup<Curve> CurveData;
        [ReadOnly] public ComponentLookup<ParkingLaneData> ParkingLaneDataComponents;
        [ReadOnly] public ComponentLookup<ParkedCar> ParkedCarData;

        [WriteOnly] public NativeList<BuildingUtilizationResult> Results;

        public void Execute(int index) {
            Entity buildingEntity = BuildingEntities[index];
            int slotCapacity = 0;
            int parkedCars = 0;

            // Check parking lanes that belong to this building
            foreach (Entity laneEntity in ParkingLanes) {
                if (!DoesLaneBelongToBuilding(laneEntity, buildingEntity)) {
                    continue;
                }

                if (!ParkingLaneData.TryGetComponent(laneEntity, out ParkingLane parkingLane)) {
                    continue;
                }

                // Skip virtual lanes
                if ((parkingLane.m_Flags & ParkingLaneFlags.VirtualLane) != 0) {
                    continue;
                }

                GetBuildingParkingLaneCounts(laneEntity, parkingLane, ref slotCapacity, ref parkedCars);
            }

            // Check garage lanes that belong to this building
            foreach (Entity laneEntity in GarageLanes) {
                if (!DoesLaneBelongToBuilding(laneEntity, buildingEntity)) {
                    continue;
                }

                if (!GarageLaneData.TryGetComponent(laneEntity, out GarageLane garageLane)) {
                    continue;
                }

                slotCapacity += garageLane.m_VehicleCapacity;
                parkedCars += garageLane.m_VehicleCount;
            }

            Results.Add(
                new BuildingUtilizationResult {
                    BuildingEntity = buildingEntity,
                    Utilization = slotCapacity > 0 ? (double)parkedCars / slotCapacity : 0.0
                }
            );
        }

        private void GetBuildingParkingLaneCounts(
            Entity subLane, ParkingLane parkingLane, ref int slotCapacity, ref int parkedCars
        ) {
            // Get parking slot count using game's method
            Entity prefab = PrefabRefData[subLane].m_Prefab;
            Curve curve = CurveData[subLane];
            ParkingLaneData parkingLaneData = ParkingLaneDataComponents[prefab];

            if (parkingLaneData.m_SlotInterval != 0f) {
                int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
                slotCapacity += parkingSlotCount;
            }

            if (!LaneObjectData.TryGetBuffer(subLane, out DynamicBuffer<LaneObject> laneObjects)) {
                return;
            }

            // Count parked cars in this lane
            for (int j = 0; j < laneObjects.Length; j++) {
                if (ParkedCarData.HasComponent(laneObjects[j].m_LaneObject)) {
                    parkedCars++;
                }
            }
        }

        private bool DoesLaneBelongToBuilding(Entity laneEntity, Entity targetBuilding) {
            Entity currentEntity = laneEntity;
            int depth = 0;

            while (depth < ParkingPricingConstants.MaxOwnershipDepth
                   && OwnerData.TryGetComponent(currentEntity, out Owner owner)) {
                currentEntity = owner.m_Owner;

                // Check if current entity is a building
                if (BuildingData.HasComponent(currentEntity)) {
                    return currentEntity == targetBuilding;
                }

                depth++;
            }

            return false;
        }
    }
}
