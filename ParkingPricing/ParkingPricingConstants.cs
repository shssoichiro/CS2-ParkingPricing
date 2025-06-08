namespace ParkingPricing {
    // Constants extracted to improve maintainability and avoid magic numbers
    public static class ParkingPricingConstants {
        public const double LowUtilizationThreshold = 0.2;
        public const double HighUtilizationThreshold = 0.8;
        public const double TargetUtilization = 0.5;
        public const int GameTicksPerDay = 262144;
        public const float StandardCarLength = 5f;
        public const float LanePositionMultiplier = 0.003921569f;
        public const float BlockedRangeBuffer = 0.01f;
        public const float LaneBoundaryThreshold = 0.49f;
        public const float LaneBoundaryInverseThreshold = 0.51f;
        public const int MaxOwnershipDepth = 10;
        public const int AbsoluteMaxPrice = 50;
    }
}
