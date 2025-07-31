namespace RoutePlannerAPI.Models
{
    public class DailyRoute
    {

        public int DayNumber { get; set; }
        public List<RoutePoint> Outlets { get; set; } = new List<RoutePoint>();
        public double TotalTime { get; set; }
        public double TotalCost { get; set; }

    }
}
