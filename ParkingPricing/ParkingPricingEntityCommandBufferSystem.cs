using Unity.Entities;

namespace ParkingPricing {
    // Custom ECB system for parking pricing updates
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ParkingPricingSystem))]
    public partial class ParkingPricingEntityCommandBufferSystem : EntityCommandBufferSystem {
    }
}
