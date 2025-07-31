namespace RoutePlannerAPI.Models
{
    public class RoutePointResponse
    {
        public int RoutePointNumber { get; set; }
        public long OutletId { get; set; }
        public int Priority { get; set; }
        public int Frequency { get; set; }
        public int FrequencyPriority { get; set; }
        public double VisitTime { get; set; }
        public double TravelTime { get; set; }
    }
}
