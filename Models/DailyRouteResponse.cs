namespace RoutePlannerAPI.Models
{
    public class DailyRouteResponse
    {
        public int DayNumber { get; set; }
        public List<RoutePointResponse> Outlets { get; set; }
        public double TotalTime { get; set; }
        public double TotalCost { get; set; }
    }
}
