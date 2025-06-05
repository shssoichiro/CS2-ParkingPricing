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

namespace ParkingPricing
{
    public partial class ParkingPricingSystem : GameSystemBase
    {
        private EntityQuery policyQuery;
        private EntityQuery utilizationQuery;
        private EntityQuery garageQuery;
        private EntityQuery m_ConfigQuery;
        private ComponentTypeHandle<District> m_DistrictType;
        private ComponentTypeHandle<Game.Buildings.ParkingFacility> m_parkingFacility;
        private ComponentTypeHandle<Game.Buildings.Building> m_BuildingType;
        private ComponentTypeHandle<Game.Net.ParkingLane> m_ParkingLaneType;
        private ComponentTypeHandle<Game.Net.GarageLane> m_GarageLaneType;
        private ComponentTypeHandle<Game.Common.Owner> m_OwnerType;
        private BufferTypeHandle<Policy> m_PolicyType;
        private EntityStorageInfoLookup m_EntityLookup;

        protected override void OnCreate()
        {
            base.OnCreate();
            m_ConfigQuery = GetEntityQuery(ComponentType.ReadOnly<UITransportConfigurationData>());

            // Initialize component type handles
            m_DistrictType = GetComponentTypeHandle<District>(true);
            m_parkingFacility = GetComponentTypeHandle<Game.Buildings.ParkingFacility>(true);
            m_BuildingType = GetComponentTypeHandle<Game.Buildings.Building>(true);
            m_ParkingLaneType = GetComponentTypeHandle<Game.Net.ParkingLane>(true);
            m_GarageLaneType = GetComponentTypeHandle<Game.Net.GarageLane>(true);
            m_OwnerType = GetComponentTypeHandle<Game.Common.Owner>(true);
            m_PolicyType = GetBufferTypeHandle<Policy>(false);
            m_EntityLookup = GetEntityStorageInfoLookup();
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            // One day (or month) in-game is '262144' ticks
            return 262144 / (int)Mod.m_Setting.updateFreq;
        }

        protected override void OnGameLoaded(Context serializationContext)
        {
            if (m_ConfigQuery.IsEmptyIgnoreFilter) return;
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
                    double utilization = CalculateDistrictUtilization(districtEntity, parkingLanes);
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
                    var building = EntityManager.GetComponentData<Game.Buildings.Building>(buildingEntity);
                    bool canHavePaidParking = (building.m_OptionMask & (uint)BuildingOption.PaidParking) != 0;
                    if (!canHavePaidParking)
                    {
                        continue;
                    }

                    // Calculate utilization and price change for this building
                    double utilization = CalculateBuildingUtilization(buildingEntity, parkingLanes, garageLanes);
                    int newPrice = CalculateAdjustedPrice(basePrice, maxPrice, minPrice, utilization);
                    LogUtil.Info($"Building {buildingEntity.Index}: Utilization = {utilization:P2}, New Price = {newPrice}");

                    // Apply the parking fee policy to this building
                    ApplyParkingPolicy(buildingEntity, newPrice);
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

            // Check if building has SubLane buffer (more direct approach like the game uses)
            if (EntityManager.HasBuffer<Game.Net.SubLane>(buildingEntity))
            {
                var subLanes = EntityManager.GetBuffer<Game.Net.SubLane>(buildingEntity, true);
                CheckParkingLanes(subLanes, ref slotCapacity, ref parkedCars);
            }

            // Calculate utilization percentage
            return slotCapacity > 0 ? (double)parkedCars / slotCapacity : 0.0;
        }

        private void CheckParkingLanes(DynamicBuffer<Game.Net.SubLane> subLanes, ref int slotCapacity, ref int parkedCars)
        {
            for (int i = 0; i < subLanes.Length; i++)
            {
                Entity subLane = subLanes[i].m_SubLane;

                // Handle ParkingLane
                if (EntityManager.HasComponent<Game.Net.ParkingLane>(subLane))
                {
                    var parkingLane = EntityManager.GetComponentData<Game.Net.ParkingLane>(subLane);

                    // Skip virtual lanes
                    if ((parkingLane.m_Flags & ParkingLaneFlags.VirtualLane) != 0)
                    {
                        continue;
                    }

                    // Get parking slot count using game's method
                    Entity prefab = EntityManager.GetComponentData<PrefabRef>(subLane).m_Prefab;
                    Curve curve = EntityManager.GetComponentData<Curve>(subLane);
                    var parkingLaneData = EntityManager.GetComponentData<ParkingLaneData>(prefab);

                    if (parkingLaneData.m_SlotInterval != 0f)
                    {
                        int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
                        slotCapacity += parkingSlotCount;
                    }
                    else
                    {
                        slotCapacity = -1000000; // Game's way of handling invalid slots
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
                }
                // Handle GarageLane
                else if (EntityManager.HasComponent<Game.Net.GarageLane>(subLane))
                {
                    var garageLane = EntityManager.GetComponentData<Game.Net.GarageLane>(subLane);
                    slotCapacity += garageLane.m_VehicleCapacity;
                    parkedCars += garageLane.m_VehicleCount;
                }
            }
        }

        private double CalculateDistrictUtilization(Entity districtEntity, NativeArray<Entity> parkingLanes)
        {
            double totalCapacity = 0;
            double totalOccupied = 0;

            // Check all parking lanes to see which belong to this district
            foreach (Entity laneEntity in parkingLanes)
            {
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
                            double laneCapacity = 0;
                            double laneOccupied = 0;

                            // Count parked cars in this lane
                            if (EntityManager.HasBuffer<LaneObject>(laneEntity))
                            {
                                var laneObjects = EntityManager.GetBuffer<LaneObject>(laneEntity, true);
                                for (int j = 0; j < laneObjects.Length; j++)
                                {
                                    if (EntityManager.HasComponent<ParkedCar>(laneObjects[j].m_LaneObject))
                                    {
                                        laneOccupied++;
                                    }
                                }
                            }

                            // Get parking slot count using game's method
                            Entity prefab = EntityManager.GetComponentData<PrefabRef>(laneEntity).m_Prefab;
                            Curve curve = EntityManager.GetComponentData<Curve>(laneEntity);
                            var parkingLaneData = EntityManager.GetComponentData<ParkingLaneData>(prefab);

                            if (parkingLaneData.m_SlotInterval != 0f)
                            {
                                int parkingSlotCount = NetUtils.GetParkingSlotCount(curve, parkingLane, parkingLaneData);
                                laneCapacity += parkingSlotCount;
                            }
                            else
                            {
                                laneCapacity = -1000000; // Game's way of handling invalid slots
                            }

                            // Count parked cars in this lane
                            if (EntityManager.HasBuffer<LaneObject>(laneEntity))
                            {
                                var laneObjects = EntityManager.GetBuffer<LaneObject>(laneEntity, true);
                                for (int j = 0; j < laneObjects.Length; j++)
                                {
                                    if (EntityManager.HasComponent<ParkedCar>(laneObjects[j].m_LaneObject))
                                    {
                                        laneOccupied++;
                                    }
                                }
                            }

                            // Determine weight based on district ownership
                            double weight = 1.0;
                            if (leftMatch && rightMatch)
                            {
                                weight = 1.0; // Full capacity/occupancy if both sides belong to district
                            }
                            else if (leftMatch || rightMatch)
                            {
                                weight = 0.5; // Half capacity/occupancy if only one side belongs to district
                            }

                            totalCapacity += laneCapacity * weight;
                            totalOccupied += laneOccupied * weight;
                        }
                    }
                }
            }

            // Calculate utilization percentage
            return totalCapacity > 0 ? totalOccupied / totalCapacity : 0.0;
        }

        private void ApplyDistrictParkingPolicy(Entity districtEntity, int newPrice)
        {
            // Check if the district has a policy buffer
            if (EntityManager.HasBuffer<Policy>(districtEntity))
            {
                var policyBuffer = EntityManager.GetBuffer<Policy>(districtEntity);

                // Look for existing parking fee policy
                bool policyFound = false;
                for (int i = 0; i < policyBuffer.Length; i++)
                {
                    var policy = policyBuffer[i];

                    // Check if this is a parking fee policy
                    if (IsStreetParkingFeePolicy(policy))
                    {
                        // Update existing policy
                        policy.m_Adjustment = newPrice;
                        policyBuffer[i] = policy;
                        policyFound = true;
                        LogUtil.Info($"Updated street parking policy for district {districtEntity.Index}: ${newPrice}");
                        break;
                    }
                }

                // If no existing policy found, add a new one
                if (!policyFound)
                {
                    // Create new street parking fee policy
                    var newPolicy = CreateStreetParkingFeePolicy(newPrice);
                    policyBuffer.Add(newPolicy);
                    LogUtil.Info($"Added new street parking policy for district {districtEntity.Index}: ${newPrice}");
                }
            }
            else
            {
                LogUtil.Info($"District {districtEntity.Index} does not have a policy buffer");
            }
        }

        private bool IsStreetParkingFeePolicy(Policy policy)
        {
            // Check if this policy has DistrictOption.PaidParking flag
            if (EntityManager.HasComponent<DistrictOptionData>(policy.m_Policy))
            {
                var districtOptionData = EntityManager.GetComponentData<DistrictOptionData>(policy.m_Policy);
                return AreaUtils.HasOption(districtOptionData, DistrictOption.PaidParking);
            }
            return false;
        }

        private Policy CreateStreetParkingFeePolicy(int price)
        {
            return new Policy
            {
                m_Adjustment = price,
                m_Flags = PolicyFlags.Active,
                m_Policy = Entity.Null // TODO: Set to the correct street parking policy prefab entity
            };
        }

        private void ApplyParkingPolicy(Entity buildingEntity, int newPrice)
        {
            // Check if the building has a policy buffer
            if (EntityManager.HasBuffer<Policy>(buildingEntity))
            {
                var policyBuffer = EntityManager.GetBuffer<Policy>(buildingEntity);

                // Look for existing parking fee policy
                bool policyFound = false;
                for (int i = 0; i < policyBuffer.Length; i++)
                {
                    var policy = policyBuffer[i];

                    // Check if this is a parking fee policy (you may need to adjust this condition)
                    // This is a placeholder - you'll need to identify the correct policy type
                    if (IsLotParkingFeePolicy(policy))
                    {
                        // Update existing policy
                        policy.m_Adjustment = newPrice;
                        policyBuffer[i] = policy;
                        policyFound = true;
                        LogUtil.Info($"Updated parking policy for building {buildingEntity.Index}: ${newPrice}");
                        break;
                    }
                }

                // If no existing policy found, add a new one
                if (!policyFound)
                {
                    // Create new parking fee policy
                    // You'll need to implement this based on the game's policy system
                    var newPolicy = CreateLotParkingFeePolicy(newPrice);
                    policyBuffer.Add(newPolicy);
                    LogUtil.Info($"Added new parking policy for building {buildingEntity.Index}: ${newPrice}");
                }
            }
            else
            {
                LogUtil.Info($"Building {buildingEntity.Index} does not have a policy buffer");
            }
        }

        private bool IsLotParkingFeePolicy(Policy policy)
        {
            // Check if this policy has BuildingOption.PaidParking flag
            if (EntityManager.HasComponent<BuildingOptionData>(policy.m_Policy))
            {
                var buildingOptionData = EntityManager.GetComponentData<BuildingOptionData>(policy.m_Policy);
                return BuildingUtils.HasOption(buildingOptionData, BuildingOption.PaidParking);
            }
            return false;
        }

        private Policy CreateLotParkingFeePolicy(int price)
        {
            return new Policy
            {
                m_Adjustment = price,
                m_Flags = PolicyFlags.Active,
                m_Policy = Entity.Null // TODO: Set to the correct PaidParking policy prefab entity
            };
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
    }
}