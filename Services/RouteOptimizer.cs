using RoutePlannerAPI.Models;

namespace RoutePlannerAPI.Services
{
    /// <summary>
    /// Класс RouteOptimizer выполняет оптимизацию порядка посещения точек внутри каждого дня
    /// </summary>
    public class RouteOptimizer
    {
        // это костыль, который я чуть позже хочу оптимизировать, чтобы перебор вариантов работал быстрее
        private const int TimeLimitSeconds = 1; // лимит времени на перебор вариантов
        private const int MaxPermutations = 1000; // лимит на количество итераций

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
            if (points.Count == 0) 
                return dayRoute;

            // вариант маршрута который был составлен в GenerateScheduler
            var originalPoints = points.ToList();

            // Если есть фиксированная стартовая точка, она остается на месте, оптимизируется только остальной маршрут
            RoutePoint fixedStartPoint = null;
            List<RoutePoint> pointsToOptimize;

            if (useFixedStartPoint)
            {
                fixedStartPoint = points.First();
                pointsToOptimize = points.Skip(1).ToList();
            }
            else
            {
                pointsToOptimize = points.ToList();
            }

            // Генерируем все возможные перестановки точек
            var allPermutations = GenerateAllPermutations(pointsToOptimize, MaxPermutations);

            DailyRoute bestRoute = null;
            double bestTime = double.MaxValue;
            var startTime = DateTime.Now;

            // Перебираем все перестановки и находим оптимальную
            foreach (var permutation in allPermutations)
            {
                // Проверяем таймаут
                if ((DateTime.Now - startTime).TotalSeconds > TimeLimitSeconds)
                {
                    Console.WriteLine($"Таймаут оптимизации для дня {dayRoute.DayNumber}");
                    break;
                }

                // Собираем полный маршрут
                var fullRoute = new List<RoutePoint>();
                if (fixedStartPoint != null)
                {
                    fullRoute.Add(fixedStartPoint);
                }
                fullRoute.AddRange(permutation);

                // Проверяем валидность маршрута и вычисляем время
                var route = BuildDailyRouteFromPath(fullRoute, segments, dayRoute.DayNumber);

                if (route != null && route.TotalTime < bestTime)
                {
                    bestRoute = route;
                    bestTime = route.TotalTime;
                }
            }

            // Если нашли лучший маршрут, возвращаем его, иначе возвращаем оригинальный
            return bestRoute ?? dayRoute;
        }

        private static List<List<RoutePoint>> GenerateAllPermutations(List<RoutePoint> points, int maxPermutations)
        {
            var result = new List<List<RoutePoint>>();

            // Для больших наборов точек используем эвристики, а не полный перебор
            if (points.Count > 10)
            {
                return GenerateHeuristicPermutations(points, maxPermutations);
            }

            // Для малых наборов генерируем все перестановки
            GeneratePermutationsRecursive(points, 0, result, maxPermutations);
            return result;
        }

        private static void GeneratePermutationsRecursive(List<RoutePoint> points, int start,
            List<List<RoutePoint>> result, int maxPermutations)
        {
            if (result.Count >= maxPermutations) return;

            if (start >= points.Count)
            {
                result.Add(new List<RoutePoint>(points));
                return;
            }

            for (int i = start; i < points.Count; i++)
            {
                if (result.Count >= maxPermutations) break;

                Swap(points, start, i);
                GeneratePermutationsRecursive(points, start + 1, result, maxPermutations);
                Swap(points, start, i);
            }
        }

        private static List<List<RoutePoint>> GenerateHeuristicPermutations(List<RoutePoint> points, int maxPermutations)
        {
            var result = new List<List<RoutePoint>>();
            var random = new Random();

            // Оригинальный порядок
            result.Add(new List<RoutePoint>(points));

            // Обратный порядок
            result.Add(new List<RoutePoint>(points.AsEnumerable().Reverse()));

            // Случайные перестановки
            for (int i = 0; i < Math.Min(maxPermutations - 2, 100); i++)
            {
                var shuffled = points.OrderBy(x => random.Next()).ToList();
                if (!result.Any(r => r.SequenceEqual(shuffled)))
                {
                    result.Add(shuffled);
                }
            }

            return result;
        }

        private static void Swap(List<RoutePoint> list, int i, int j)
        {
            var temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }

        private static DailyRoute BuildDailyRouteFromPath(
            List<RoutePoint> path,
            List<RouteSegment> segments,
            int dayNumber)
        {
            if (path.Count == 0) return null;

            double totalTime = 0;
            double totalCost = 0;
            var routePoints = new List<RoutePoint>();

            // Первая точка
            routePoints.Add(new RoutePoint
            {
                OutletId = path[0].OutletId,
                VisitTime = path[0].VisitTime,
                TravelTime = 0
            });
            totalTime += path[0].VisitTime;

            // Проверяем все сегменты маршрута
            for (int i = 1; i < path.Count; i++)
            {
                var prev = path[i - 1];
                var current = path[i];

                var segment = segments.FirstOrDefault(s =>
                    s.IdOutlet1 == prev.OutletId && s.IdOutlet2 == current.OutletId);

                // Если сегмент не существует, маршрут невалиден
                if (segment == null)
                    return null;

                // Проверяем ограничение по расстоянию (кроме первого перехода)
                if (i > 1 && segment.Length > RoutePlannerService.MaxDistanceLimit)
                    return null;

                routePoints.Add(new RoutePoint
                {
                    OutletId = current.OutletId,
                    VisitTime = current.VisitTime,
                    TravelTime = segment.Time
                });

                totalTime += segment.Time + current.VisitTime;
                totalCost += segment.Cost;
            }

            return new DailyRoute
            {
                DayNumber = dayNumber,
                Outlets = routePoints,
                TotalTime = totalTime,
                TotalCost = totalCost
            };
        }

    }
}
