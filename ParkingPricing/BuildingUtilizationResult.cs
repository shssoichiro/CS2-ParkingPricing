using Unity.Entities;

namespace ParkingPricing {
    // Result structure for job communication
    public struct BuildingUtilizationResult {
        public Entity BuildingEntity;
        public double Utilization;
    }
}
