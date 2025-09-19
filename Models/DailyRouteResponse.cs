namespace RoutePlannerAPI.Models
{
    // класс также как и DailyRoute служит для формирования результатов, только этот возвращает результат и не используется в коде для внесения изменений в процессе работы алгоритма
    public class DailyRouteResponse
    {
        public int DayNumber { get; set; }
        public List<RoutePointResponse> Outlets { get; set; }
        public double TotalTime { get; set; }
        public double TotalCost { get; set; }
    }
}
