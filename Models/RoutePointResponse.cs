namespace RoutePlannerAPI.Models
{
    /// <summary>
    /// представляет обогащенную информацию о точке маршрута для конечного ответа API
    /// </summary>
    public class RoutePointResponse
    {
        public int RoutePointNumber { get; set; } // порядковый номер точки в маршруте дня 
        public long OutletId { get; set; }
        public int Priority { get; set; }
        public int Frequency { get; set; }
        public int FrequencyPriority { get; set; }
        public double VisitTime { get; set; }
        public double TravelTime { get; set; }
    }
}
