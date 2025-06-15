using Unity.Entities;

namespace ParkingPricing {
    // Result structure for job communication
    public struct DistrictUtilizationResult {
        public Entity DistrictEntity;
        public double Utilization;
    }
}
