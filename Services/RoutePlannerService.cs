using System.Formats.Asn1;
using System.Globalization;
using System.Text.Json.Serialization;
using CsvHelper;
using CsvHelper.Configuration;
using Newtonsoft.Json;
using RoutePlannerAPI.Models;

namespace RoutePlannerAPI.Services
{
    public class RoutePlannerService
    {
        public const double DefaultVisitTime = 1800; // 30 минут в секундах
        public const int PriorityWeightPenalty = 300;
        public const double TimeTolerance = 600; // 10 минут
        public const int FrequencyPriorityWeight = 200;
        public const double MaxDistanceLimit = 100; // Максимальное расстояние между точками в км (кроме первой пары)

        public enum PlanningPeriod
        {
            Week = 7,
            Month = 30,
            Quarter = 65,
            Custom = 57,
        }

        static List<RouteSegment> ReadRouteSegmentsFromCsv(string filePath)
        {
            var config = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = ";",
                MissingFieldFound = null,
                HeaderValidated = null,
                BadDataFound = null,
            };

            using (var reader = new StreamReader(filePath))
            using (var csv = new CsvReader(reader, config))
            {
                return csv.GetRecords<RouteSegment>().ToList();
            }
        }

        public async Task<List<CsvOutput>> GenerateScheduleAsync(RoutePlanRequest request)
        {
            var segments = request
                .Segments.Select(s => new RouteSegment
                {
                    IdOutlet1 = s.IdOutlet1,
                    IdOutlet2 = s.IdOutlet2,
                    Time = s.Time,
                    Length = s.Length,
                    Cost = s.Cost,
                    Priority1 = s.Priority1,
                    Priority2 = s.Priority2,
                    TimeVisit1 = s.TimeVisit1,
                    TimeVisit2 = s.TimeVisit2,
                    Frequency1 = s.Frequency1,
                    Frequency2 = s.Frequency2,
                    FrequencyPriority1 = s.FrequencyPriority1,
                    FrequencyPriority2 = s.FrequencyPriority2,
                })
                .ToList();

            var outlets = CollectAllOutlets(segments);
            var schedule = GenerateScheduleInternal(
                segments,
                request.TotalDays,
                request.WorkingHoursPerDay,
                request.UseFixedStartPoint,
                request.FixedStartPointId,
                outlets
            );

            var optimizedSchedule = RouteOptimizer.OptimizeDailyRoutes(
                schedule,
                segments,
                request.UseFixedStartPoint
            );

            var response = MapToResponse(optimizedSchedule, outlets);
            var result = WriteToCsv(optimizedSchedule, outlets);
            return result;
        }

        private List<DailyRoute> GenerateScheduleInternal(
            List<RouteSegment> segments,
            int totalDays,
            double workingHoursPerDay,
            bool useFixedStartPoint,
            long fixedStartPointId,
            Dictionary<long, OutletInfo> outlets
        )
        {
            var schedule = new List<DailyRoute>();
            var random = new Random();
            var visitTracker = new VisitTracker(outlets, totalDays);
            var routeHistory = new HashSet<string>();

            double maxDailyTime = workingHoursPerDay * 3600;
            long currentStartOutlet = useFixedStartPoint
                ? fixedStartPointId
                : outlets.Keys.ElementAt(random.Next(outlets.Count));

            for (int day = 1; day <= totalDays; day++)
            {
                var dailyRoute = new DailyRoute { DayNumber = day };
                double dailyTimeUsed = 0;
                var visitedToday = new HashSet<long>();
                int attempt = 0;
                const int maxAttempts = 100;

                while (attempt++ < maxAttempts)
                {
                    dailyRoute.Outlets.Clear();
                    dailyTimeUsed = 0;
                    visitedToday.Clear();

                    long currentOutlet = currentStartOutlet;

                    if (useFixedStartPoint || visitTracker.CanVisit(currentOutlet, day))
                    {
                        var visitTime = outlets[currentOutlet].TimeVisit;
                        dailyRoute.Outlets.Add(
                            new RoutePoint
                            {
                                OutletId = currentOutlet,
                                VisitTime = visitTime,
                                TravelTime = 0,
                            }
                        );
                        dailyTimeUsed += visitTime;
                        visitedToday.Add(currentOutlet);
                    }

                    while (dailyTimeUsed < maxDailyTime)
                    {
                        var availableSegments = segments
                            .Where(s => s.IdOutlet1 == currentOutlet)
                            .Where(s =>
                                visitTracker.CanVisit(s.IdOutlet2, day)
                                && !visitedToday.Contains(s.IdOutlet2)
                            )
                            // Применяем ограничение по расстоянию (кроме первого перехода)
                            .Where(s =>
                                dailyRoute.Outlets.Count <= 1 || s.Length <= MaxDistanceLimit
                            )
                            .Where(s => s != null)
                            .ToList();

                        if (!availableSegments.Any())
                        {
                            Console.WriteLine($"Нет доступных точек для дня {day}");
                            break;
                        }

                        var bestSegment = availableSegments
                            .OrderBy(s => visitTracker.GetVisitCount(s.IdOutlet2))
                            .ThenBy(s => CalculateSegmentWeight(s, visitTracker, outlets, day))
                            .First();

                        var nextOutlet = bestSegment.IdOutlet2;
                        var visitTime = outlets[nextOutlet].TimeVisit;
                        var totalTime = bestSegment.Time + visitTime;

                        if (dailyTimeUsed + totalTime > maxDailyTime + TimeTolerance)
                            break;

                        dailyRoute.Outlets.Add(
                            new RoutePoint
                            {
                                OutletId = nextOutlet,
                                VisitTime = visitTime,
                                TravelTime = bestSegment.Time,
                            }
                        );

                        visitedToday.Add(nextOutlet);
                        dailyTimeUsed += totalTime;
                        currentOutlet = nextOutlet;
                    }

                    var routeSignature = string.Join(
                        ",",
                        dailyRoute.Outlets.Select(o => o.OutletId)
                    );
                    if (!routeHistory.Contains(routeSignature) && dailyRoute.Outlets.Count >= 2)
                    {
                        routeHistory.Add(routeSignature);
                        break;
                    }
                }

                foreach (var point in dailyRoute.Outlets.Skip(useFixedStartPoint ? 1 : 0))
                {
                    visitTracker.RecordVisit(point.OutletId, day);
                }

                if (dailyRoute.Outlets.Count >= 2)
                {
                    dailyRoute.TotalTime = dailyTimeUsed;
                    schedule.Add(dailyRoute);
                }

                if (!useFixedStartPoint)
                {
                    currentStartOutlet = visitTracker.GetLeastVisitedOutlet(outlets.Keys);
                }
            }

            AddMissingOutlets(schedule, segments, outlets, visitTracker, maxDailyTime);
            return schedule;
        }

        private RoutePlanResponse MapToResponse(
            List<DailyRoute> schedule,
            Dictionary<long, OutletInfo> outlets
        )
        {
            var response = new RoutePlanResponse { Schedule = new List<DailyRouteResponse>() };

            foreach (var route in schedule)
            {
                var dailyRoute = new DailyRouteResponse
                {
                    DayNumber = route.DayNumber,
                    TotalTime = route.TotalTime,
                    TotalCost = route.TotalCost,
                    Outlets = new List<RoutePointResponse>(),
                };

                for (int i = 0; i < route.Outlets.Count; i++)
                {
                    var outletId = route.Outlets[i].OutletId;
                    var outletInfo = outlets[outletId];

                    dailyRoute.Outlets.Add(
                        new RoutePointResponse
                        {
                            RoutePointNumber = i + 1,
                            OutletId = outletId,
                            Priority = outletInfo.Priority,
                            Frequency = outletInfo.Frequency,
                            FrequencyPriority = outletInfo.FrequencyPriority,
                            VisitTime = route.Outlets[i].VisitTime,
                            TravelTime = route.Outlets[i].TravelTime,
                        }
                    );
                }

                response.Schedule.Add(dailyRoute);
            }

            return response;
        }

        static void AddMissingOutlets(
            List<DailyRoute> schedule,
            List<RouteSegment> segments,
            Dictionary<long, OutletInfo> outlets,
            VisitTracker visitTracker,
            double maxDailyTime
        )
        {
            var missingOutlets = outlets
                .Keys.Where(id => visitTracker.GetVisitCount(id) < outlets[id].Frequency)
                .ToList();

            foreach (var outletId in missingOutlets)
            {
                var outlet = outlets[outletId];
                int visitsNeeded = outlet.Frequency - visitTracker.GetVisitCount(outletId);

                for (int visit = 0; visit < visitsNeeded; visit++)
                {
                    foreach (var dayRoute in schedule.OrderBy(r => r.TotalTime))
                    {
                        if (visitTracker.GetVisitCount(outletId) >= outlet.Frequency)
                            break;

                        for (int i = 0; i < dayRoute.Outlets.Count - 1; i++)
                        {
                            var current = dayRoute.Outlets[i];
                            var next = dayRoute.Outlets[i + 1];

                            var segmentToMissing = segments.FirstOrDefault(s =>
                                s.IdOutlet1 == current.OutletId && s.IdOutlet2 == outletId
                            );

                            var segmentFromMissing = segments.FirstOrDefault(s =>
                                s.IdOutlet1 == outletId && s.IdOutlet2 == next.OutletId
                            );

                            if (segmentToMissing != null && segmentFromMissing != null)
                            {
                                // Проверяем ограничение по расстоянию (кроме первого перехода)
                                bool isFirstPair = (i == 0 && dayRoute.Outlets.Count == 1);
                                if (
                                    !isFirstPair
                                    && (
                                        segmentToMissing.Length > MaxDistanceLimit
                                        || segmentFromMissing.Length > MaxDistanceLimit
                                    )
                                )
                                {
                                    continue;
                                }

                                var totalAddedTime =
                                    segmentToMissing.Time
                                    + outlet.TimeVisit
                                    + segmentFromMissing.Time;
                                var originalTime = segments
                                    .First(s =>
                                        s.IdOutlet1 == current.OutletId
                                        && s.IdOutlet2 == next.OutletId
                                    )
                                    .Time;

                                if (
                                    dayRoute.TotalTime + totalAddedTime - originalTime
                                    <= maxDailyTime + TimeTolerance
                                )
                                {
                                    dayRoute.Outlets.Insert(
                                        i + 1,
                                        new RoutePoint
                                        {
                                            OutletId = outletId,
                                            VisitTime = outlet.TimeVisit,
                                            TravelTime = segmentToMissing.Time,
                                        }
                                    );
                                    dayRoute.TotalTime += totalAddedTime - originalTime;
                                    visitTracker.RecordVisit(outletId, dayRoute.DayNumber);
                                    break;
                                }
                            }
                        }
                    }
                }
            }
        }

        static double CalculateSegmentWeight(
            RouteSegment segment,
            VisitTracker tracker,
            Dictionary<long, OutletInfo> outlets,
            int currentDay
        )
        {
            var outlet = outlets[segment.IdOutlet2];
            double weight = segment.Cost;

            if (outlet.Priority >= 1) // Проверяем, что приоритет задан (>=1)
                weight += PriorityWeightPenalty * (1.0 / outlet.Priority); // Инвертируем приоритет

            var visitsNeeded = tracker.GetRequiredVisits(segment.IdOutlet2, currentDay);
            var visitsActual = tracker.GetVisitCount(segment.IdOutlet2);

            if (visitsActual < visitsNeeded)
                weight -= (visitsNeeded - visitsActual) * 500;

            if (visitsActual >= outlet.Frequency)
                weight -= outlet.FrequencyPriority * FrequencyPriorityWeight;

            return weight;
        }

        static Dictionary<long, OutletInfo> CollectAllOutlets(List<RouteSegment> segments)
        {
            var outlets = new Dictionary<long, OutletInfo>();

            foreach (var segment in segments)
            {
                if (!outlets.ContainsKey(segment.IdOutlet1))
                {
                    outlets[segment.IdOutlet1] = new OutletInfo
                    {
                        Id = segment.IdOutlet1,
                        Priority = segment.Priority1,
                        Frequency = segment.Frequency1,
                        FrequencyPriority = segment.FrequencyPriority1,
                        TimeVisit =
                            segment.TimeVisit1 > 0 ? segment.TimeVisit1 * 60 : DefaultVisitTime,
                    };
                }

                if (!outlets.ContainsKey(segment.IdOutlet2))
                {
                    outlets[segment.IdOutlet2] = new OutletInfo
                    {
                        Id = segment.IdOutlet2,
                        Priority = segment.Priority2,
                        Frequency = segment.Frequency2,
                        FrequencyPriority = segment.FrequencyPriority2,
                        TimeVisit =
                            segment.TimeVisit2 > 0 ? segment.TimeVisit2 * 60 : DefaultVisitTime,
                    };
                }
            }

            return outlets;
        }

        static List<CsvOutput> WriteToCsv(
            List<DailyRoute> schedule,
            Dictionary<long, OutletInfo> outlets
        )
        {
            var records = new List<CsvOutput>();

            foreach (var route in schedule)
            {
                for (int i = 0; i < route.Outlets.Count; i++)
                {
                    var outletId = route.Outlets[i].OutletId;
                    var outletInfo = outlets[outletId];

                    records.Add(
                        new CsvOutput
                        {
                            DayNumber = route.DayNumber,
                            RoutePointNumber = i + 1,
                            OutletId = outletId,
                            Priority = outletInfo.Priority,
                            Frequency = outletInfo.Frequency,
                            FrequencyPriority = outletInfo.FrequencyPriority,
                            VisitTime = route.Outlets[i].VisitTime,
                            TravelTime = route.Outlets[i].TravelTime,
                            TotalRouteTime = route.TotalTime,
                            TotalRouteCost = route.TotalCost,
                        }
                    );
                }
            }
            return records;
        }
    }
}
