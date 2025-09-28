using RoutePlannerAPI.Models;

namespace RoutePlannerAPI.Services
{
    /// <summary>
    /// Класс RouteOptimizer выполняет оптимизацию порядка посещения точек внутри каждого дня
    /// </summary>
    public class RouteOptimizer
    {
        public static List<DailyRoute> OptimizeDailyRoutes(
            List<DailyRoute> schedule, // полученная последовательность точек
            List<RouteSegment> segments, // Все сегменты для расчета расстояний
            bool useFixedStartPoint)
        {
            var optimizedSchedule = new List<DailyRoute>();

            foreach (var dayRoute in schedule)
            {
                if (dayRoute.Outlets.Count <= 2) // Оптимизация не нужна, если точек меньше 3
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


            // Если есть фиксированная стартовая точка, она остается на месте, оптимизируется только остальной маршрут
            var startPoint = useFixedStartPoint ? points.First() : null;
            var pointsToOptimize = useFixedStartPoint ? points.Skip(1).ToList() : points.ToList();

            var optimizedSequence = new List<RoutePoint>();
            var remainingPoints = new List<RoutePoint>(pointsToOptimize);

            // добавляю стартовую точку (если есть)
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

            //  Жадный алгоритм: всегда идем к ближайшей точке
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
                totalTime += optimizedSequence[0].VisitTime; // Время на первой точке

                for (int i = 1; i < optimizedSequence.Count; i++)
                {
                    var prev = optimizedSequence[i - 1];
                    var current = optimizedSequence[i];

                    var segment = segments.FirstOrDefault(s =>
                        s.IdOutlet1 == prev.OutletId && s.IdOutlet2 == current.OutletId);

                    if (segment != null)
                    {
                        totalTime += segment.Time + current.VisitTime; // Путь + визит
                        totalCost += segment.Cost; // Стоимость пути
                    }
                }
            }
            // возврат результата
            return new DailyRoute
            {
                DayNumber = dayRoute.DayNumber,
                Outlets = optimizedSequence,
                TotalTime = totalTime,
                TotalCost = totalCost
            };
        }

        // алгоритм ближайшего соседа
        private static RoutePoint FindNearestPointWithDistanceLimit(
            long fromOutletId, // точка из которой нужно уйти до следующей
            List<RoutePoint> candidates, // кандидаты
            List<RouteSegment> segments, // сегменты
            bool allowAnyDistance) // ограничение по дистанции
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
