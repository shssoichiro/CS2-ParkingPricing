namespace ParkingPricing
{
    // Constants extracted to improve maintainability and avoid magic numbers
    public static class ParkingPricingConstants
    {
        public const double LOW_UTILIZATION_THRESHOLD = 0.2;
        public const double HIGH_UTILIZATION_THRESHOLD = 0.8;
        public const double TARGET_UTILIZATION = 0.5;
        public const int GAME_TICKS_PER_DAY = 262144;
        public const float STANDARD_CAR_LENGTH = 5f;
        public const float LANE_POSITION_MULTIPLIER = 0.003921569f;
        public const float BLOCKED_RANGE_BUFFER = 0.01f;
        public const float LANE_BOUNDARY_THRESHOLD = 0.49f;
        public const float LANE_BOUNDARY_INVERSE_THRESHOLD = 0.51f;
        public const int MAX_OWNERSHIP_DEPTH = 10;
        public const int ABSOLUTE_MAX_PRICE = 50;
    }
}