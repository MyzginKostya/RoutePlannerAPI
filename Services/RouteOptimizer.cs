using RoutePlannerAPI.Models;

namespace RoutePlannerAPI.Services
{
    public class RouteOptimizer
    {
        public static List<DailyRoute> OptimizeDailyRoutes(
            List<DailyRoute> schedule,
            List<RouteSegment> segments,
            bool useFixedStartPoint)
        {
            var optimizedSchedule = new List<DailyRoute>();

            foreach (var dayRoute in schedule)
            {
                if (dayRoute.Outlets.Count <= 2)
                {
                    optimizedSchedule.Add(dayRoute);
                    continue;
                }

                var optimizedRoute = OptimizeSingleDayRoute(dayRoute, segments, useFixedStartPoint);
                optimizedSchedule.Add(optimizedRoute);
            }

            return optimizedSchedule;
        }

        private static DailyRoute OptimizeSingleDayRoute(
            DailyRoute dayRoute,
            List<RouteSegment> segments,
            bool useFixedStartPoint)
        {
            var points = dayRoute.Outlets.ToList();
            if (points.Count == 0) return dayRoute;

            var startPoint = useFixedStartPoint ? points.First() : null;
            var pointsToOptimize = useFixedStartPoint ? points.Skip(1).ToList() : points.ToList();

            var optimizedSequence = new List<RoutePoint>();
            var remainingPoints = new List<RoutePoint>(pointsToOptimize);

            if (startPoint != null)
            {
                optimizedSequence.Add(startPoint);
            }

            var currentPoint = startPoint ?? remainingPoints.First();
            if (startPoint == null && remainingPoints.Any())
            {
                remainingPoints.Remove(currentPoint);
                optimizedSequence.Add(currentPoint);
            }

            while (remainingPoints.Count > 0)
            {
                var nearest = FindNearestPointWithDistanceLimit(
                    currentPoint.OutletId,
                    remainingPoints,
                    segments,
                    optimizedSequence.Count <= 1); // Разрешаем большой прыжок только для первых двух точек

                optimizedSequence.Add(nearest);
                remainingPoints.Remove(nearest);
                currentPoint = nearest;
            }

            double totalTime = 0;
            double totalCost = 0;

            if (optimizedSequence.Count > 0)
            {
                totalTime += optimizedSequence[0].VisitTime;

                for (int i = 1; i < optimizedSequence.Count; i++)
                {
                    var prev = optimizedSequence[i - 1];
                    var current = optimizedSequence[i];

                    var segment = segments.FirstOrDefault(s =>
                        s.IdOutlet1 == prev.OutletId && s.IdOutlet2 == current.OutletId);

                    if (segment != null)
                    {
                        totalTime += segment.Time + current.VisitTime;
                        totalCost += segment.Cost;
                    }
                }
            }

            return new DailyRoute
            {
                DayNumber = dayRoute.DayNumber,
                Outlets = optimizedSequence,
                TotalTime = totalTime,
                TotalCost = totalCost
            };
        }

        private static RoutePoint FindNearestPointWithDistanceLimit(
            long fromOutletId,
            List<RoutePoint> candidates,
            List<RouteSegment> segments,
            bool allowAnyDistance)
        {
            return candidates
                .Where(p => allowAnyDistance ||
                           (segments.FirstOrDefault(s =>
                               s.IdOutlet1 == fromOutletId &&
                               s.IdOutlet2 == p.OutletId)?.Length ?? double.MaxValue) <= RoutePlannerService.MaxDistanceLimit)
                .OrderBy(p =>
                {
                    var segment = segments.FirstOrDefault(s =>
                        s.IdOutlet1 == fromOutletId && s.IdOutlet2 == p.OutletId);
                    return segment?.Time ?? double.MaxValue;
                })
                .First();
        }

        private static RoutePoint FindNearestPoint(long fromOutletId, List<RoutePoint> candidates, List<RouteSegment> segments)
        {
            return candidates
                .OrderBy(p =>
                {
                    var segment = segments.FirstOrDefault(s =>
                        s.IdOutlet1 == fromOutletId && s.IdOutlet2 == p.OutletId);
                    return segment?.Time ?? double.MaxValue;
                })
                .First();
        }
    }
}
