namespace RoutePlannerAPI.Models
{
    /// <summary>
    /// содержит минимально необходимую информацию о посещении конкретной точки в конкретный момент времени маршрута
    /// </summary>
    public class RoutePoint
    {

        public long OutletId { get; set; }
        public double VisitTime { get; set; } // время, которое будет затрачено на посещение точки
        public double TravelTime { get; set; } // время, затраченное на дорогу до этой точки от предыдущей

    }
}
