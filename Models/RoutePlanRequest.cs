namespace RoutePlannerAPI.Models
{
    public class RoutePlanRequest
    {
        public int TotalDays { get; set; }
        public double WorkingHoursPerDay { get; set; }
        public bool UseFixedStartPoint { get; set; }
        public long FixedStartPointId { get; set; }
        public List<RouteSegmentInput> Segments { get; set; }
    }
}
