namespace RoutePlannerAPI.Models
{
    /// <summary>
    /// финальный ответ от API
    /// </summary>
    public class RoutePlanResponse
    {
        public List<DailyRouteResponse> Schedule { get; set; }
    }
}
