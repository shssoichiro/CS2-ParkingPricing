using Unity.Entities;

namespace ParkingPricing {
    // Component to signal a policy update request
    public struct PolicyUpdateCommand : IComponentData {
        public Entity PolicyPrefab;
        public int NewPrice;
        public bool IsDistrict;
        public double Utilization;
    }
}
