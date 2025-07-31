using RoutePlannerAPI.Models;

namespace RoutePlannerAPI.Services
{
    public interface IRoutePlannerService
    {
        RoutePlanResponse GenerateSchedule(
            RoutePlanRequest request);

        List<DailyRoute> OptimizeDailyRoutes(
            List<DailyRoute> schedule,
            List<RouteSegment> segments,
            bool useFixedStartPoint);
    }
}
