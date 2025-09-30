using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using RoutePlannerAPI.Models;
using System.Formats.Asn1;
using System.Globalization;
using System.Text.Json.Serialization;

namespace RoutePlannerAPI.Services
{
    public class RoutePlannerService
    {

        public const double DefaultVisitTime = 1800; // 30 минут в секундах
        public const int PriorityWeightPenalty = 300; // штраф за приоритет
        public const double TimeTolerance = 600; // 10 минут
        public const int FrequencyPriorityWeight = 200;
        public const double MaxDistanceLimit = 100; // Максимальное расстояние между точками в км (кроме первой пары)

        private static long GetSuitableStartOutlet(
             Dictionary<long, OutletInfo> outlets,
             List<RouteSegment> segments,
             VisitTracker visitTracker,
             int currentDay,
             HashSet<long> excludedOutlets = null)
        {
            excludedOutlets ??= new HashSet<long>();

            // Выбираем точки, которые можно посетить в этот день и не исключены
            var availableOutlets = outlets
                .Where(kvp => visitTracker.CanVisit(kvp.Key, currentDay) &&
                             !excludedOutlets.Contains(kvp.Key))
                .ToList();

            if (!availableOutlets.Any())
            {
                // Если нет доступных точек, возвращаем первую из всех
                return outlets.Keys.FirstOrDefault();
            }

            // Рассчитываем score для каждой доступной точки
            var scoredOutlets = availableOutlets
                .Select(kvp =>
                {
                    var outletId = kvp.Key;
                    var outletInfo = kvp.Value;

                    // Количество исходящих связей (чем больше, тем лучше)
                    int connectionCount = segments.Count(s => s.IdOutlet1 == outletId);

                    // Среднее расстояние до соседей (чем меньше, тем лучше)
                    var connectedSegments = segments.Where(s => s.IdOutlet1 == outletId).ToList();
                    double avgDistance = connectedSegments.Any() ?
                        connectedSegments.Average(s => s.Length) : double.MaxValue;

                    // Приоритет точки (чем ВЫШЕ приоритет, тем ХУЖЕ - инвертируем)
                    double priorityScore = (100 - outletInfo.Priority) * 100; // Приоритет 1 → score 500, приоритет 5 → score 100

                    // Срочность посещения (чем чаще нужно посещать, тем лучше для старта)
                    int requiredVisits = visitTracker.GetRequiredVisits(outletId, currentDay);
                    int actualVisits = visitTracker.GetVisitCount(outletId);
                    int visitUrgency = Math.Max(0, requiredVisits - actualVisits) * 200;

                    // Частота посещений (чем выше частота, тем важнее точка)
                    double frequencyScore = outletInfo.Frequency * 50;

                    // Приоритет частоты (чем выше, тем важнее)
                    double frequencyPriorityScore = outletInfo.FrequencyPriority * 300;

                    // Наличие связей
                    double connectionScore = connectionCount * 1000;

                    // Итоговый score (чем выше, тем лучше)
                    double totalScore = connectionScore / Math.Max(1, avgDistance) +
                                      priorityScore +
                                      visitUrgency +
                                      frequencyScore +
                                      frequencyPriorityScore;

                    return new
                    {
                        OutletId = outletId,
                        Score = totalScore,
                        ConnectionCount = connectionCount,
                        AvgDistance = avgDistance,
                        Priority = outletInfo.Priority,
                        VisitUrgency = visitUrgency,
                        HasConnections = connectionCount > 0
                    };
                })
                .OrderByDescending(x => x.HasConnections) // Сначала точки с связями
                .ThenByDescending(x => x.Score)           // Затем по score
                .ThenBy(x => x.AvgDistance)               // При равном score предпочитаем точки с меньшим средним расстоянием
                .ToList();

            // Логируем топ-3 варианта для отладки
            if (scoredOutlets.Any())
            {
                Console.WriteLine($"Топ-3 кандидатов для дня {currentDay}:");
                foreach (var candidate in scoredOutlets.Take(3))
                {
                    Console.WriteLine($"  Точка {candidate.OutletId}: Score={candidate.Score:F0}, " +
                                    $"Connections={candidate.ConnectionCount}, " +
                                    $"AvgDist={candidate.AvgDistance:F1}km, " +
                                    $"Priority={candidate.Priority}, " +
                                    $"Urgency={candidate.VisitUrgency}");
                }
            }

            return scoredOutlets.FirstOrDefault()?.OutletId ?? outlets.Keys.First();
        }

        private static bool HasConnections(long outletId, List<RouteSegment> segments)
        {
            return segments.Any(s => s.IdOutlet1 == outletId);
        }

        private static long GetAlternativeStartOutlet(
            Dictionary<long, OutletInfo> outlets,
            List<RouteSegment> segments,
            HashSet<long> excludedOutlets,
            VisitTracker visitTracker,
            int currentDay)
        {
            // Просто используем основной метод с исключениями
            return GetSuitableStartOutlet(outlets, segments, visitTracker, currentDay, excludedOutlets);
        }


        // метод GenerateScheduleAsync подготоваливает массив данных для прохода по графу, а после получения результата от GenerateScheduleInternal отпимизирует его, в рамках одного дня
        // возвращает результат в виде списка объектов CsvOutput
        public async Task<List<CsvOutput>> GenerateScheduleAsync(RoutePlanRequest request)
        {


            // несколько проверок на корректность входных данных
            if (request == null)
                throw new ArgumentNullException("Request body is null.");

            if (request.Segments == null || !request.Segments.Any())
                throw new ArgumentException("Request.Segments is null or empty.");

            if (request.UseFixedStartPoint && request.FixedStartPointId <= 0)
                throw new ArgumentException("FixedStartPointId must be a positive id when UseFixedStartPoint=true.");

            // копируем сегменты из входящего датапула
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

            // готовим только уникальные точки из всего набора данных с их атрибутами
            var outlets = CollectAllOutlets(segments);

            if (outlets.Count == 0)
                throw new InvalidOperationException("No outlets could be collected from the provided segments.");

            if (request.UseFixedStartPoint && !outlets.ContainsKey(request.FixedStartPointId))
                throw new InvalidOperationException($"FixedStartPointId {request.FixedStartPointId} is not present among outlets.");

            // создаем расписание
            var schedule = GenerateScheduleInternal(
                    segments,
                    request.TotalDays,
                    request.WorkingHoursPerDay,
                    request.MaxCountVisists,
                    request.UseFixedStartPoint,
                    request.FixedStartPointId,
                    outlets
             );

            /*
            // оптимизация маршрута в рамках одного дня
            var optimizedSchedule = RouteOptimizer.OptimizeDailyRoutes(
                schedule,
                segments,
                request.UseFixedStartPoint
            );
            */

            var response = MapToResponse(schedule, outlets);
            var result = WriteToCsv(schedule, outlets);
            return result;

        }

        private List<DailyRoute> GenerateScheduleInternal(
            List<RouteSegment> segments, // все возможные отрезки путей между точками (ориентированный граф)
            int totalDays, // общее количество дней на которое планируем маршрут
            double workingHoursPerDay, // количество рабочих часов в день
            int MaxCountVisits, // максимальное количество визитов в день
            bool useFixedStartPoint, // нужно ли начинать всегда из одной ТТ?
            long fixedStartPointId, // если нужно начинать из конкретной точки каждый день, то здесь будет id этой точки, иначе 0
            Dictionary<long, OutletInfo> outlets // справочная информация по всем точкам
        )
        {
            var schedule = new List<DailyRoute>(); // список с результатом сформированного дневного маршрута
            var visitTracker = new VisitTracker(outlets, totalDays); // счетчик количества посещений каждой точки
            var routeHistory = new HashSet<string>(); // хочу всегда уникальные маршруты

            double maxDailyTime = workingHoursPerDay * 3600; // переводим максимальное количество времени на маршрут из часов в секунды

            // Начальная стартовая точка
            long currentStartOutlet = useFixedStartPoint
                ? fixedStartPointId // если нужна фиксированная точка, то берем ее ID
                : GetSuitableStartOutlet(outlets, segments, visitTracker, 1); // иначе выбор оптимальной стартовой точки для дня 1

            for (int day = 1; day <= totalDays; day++) // начинаем цикл по всем дням от первого до номера в totalDays
            {
                var dailyRoute = new DailyRoute { DayNumber = day }; // объект для маршрута на один день
                double dailyTimeUsed = 0; // переменная для подсчета времени на маршруте за день
                var visitedToday = new HashSet<long>(); // записываем точки, посещенные в этот день - гарант того, что мы не посетим точку более одного раза за день
                int attempt = 0; // сколько раз попытались построить уникальный маршрут
                const int maxAttempts = 50; // не более 100 раз пытаемся

                // Обновляем стартовую точку для этого дня (если не фиксированная)
                if (!useFixedStartPoint)
                {
                    currentStartOutlet = GetSuitableStartOutlet(outlets, segments, visitTracker, day, visitedToday);
                }

                // циклом строим уникальные маршруты
                // Если построенный маршрут совпадает с каким-то из ранее созданных (routeHistory), он делает новую попытку
                while (attempt++ < maxAttempts)
                {
                    dailyRoute.Outlets.Clear();
                    dailyTimeUsed = 0; // обнуляем счетчик времени
                    visitedToday.Clear(); // очищаем список посещенных ТТ в этот день

                    long currentOutlet = currentStartOutlet; // начинаем день с выбранной стартовой точки

                    // Проверяем что стартовая точка имеет связи с другими точками
                    if (!HasConnections(currentOutlet, segments))
                    {
                        // Если нет связей, выбираем альтернативную точку
                        currentOutlet = GetAlternativeStartOutlet(outlets, segments, visitedToday, visitTracker, day);
                        if (currentOutlet == 0)
                        {
                            Console.WriteLine($"Не удалось найти подходящую стартовую точку для дня {day}");
                            break;
                        }
                    }

                    if (visitTracker.CanVisit(currentOutlet, day) && !visitedToday.Contains(currentOutlet)) // проверяем можем ли мы посетить эту ТТ, согласно частоте посещений
                    {
                        var visitTime = outlets[currentOutlet].TimeVisit;
                        dailyRoute.Outlets.Add(
                            new RoutePoint
                            {
                                OutletId = currentOutlet,
                                VisitTime = visitTime,
                                TravelTime = 0, // до первой точки путь всегда 0
                            }
                        );
                        dailyTimeUsed += visitTime;
                        visitedToday.Add(currentOutlet); // помечаю точку как посещенную
                    }
                    else
                    {
                        // Если нельзя посетить стартовую точку, выбираем другую
                        currentOutlet = GetAlternativeStartOutlet(outlets, segments, visitedToday, visitTracker, day);
                        if (currentOutlet == 0) break;

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

                    while (dailyTimeUsed < maxDailyTime) // проверяю, что в рабочем дне еще есть время для посещений
                    {
                        if (dailyRoute.Outlets.Count >= MaxCountVisits) // проверка на превышение максимального количества посещений в день
                        {
                            Console.WriteLine($"Достигнуто максимальное количество точек ({MaxCountVisits}) для дня {day}");
                            break;
                        }

                        var availableSegments = segments
                            .Where(s => s.IdOutlet1 == currentOutlet) // рассматриваем только те ТТ, с которыми есть пересечение, то есть до которых можно дойти из текущей ТТ
                            .Where(s =>
                                visitTracker.CanVisit(s.IdOutlet2, day)
                                && !visitedToday.Contains(s.IdOutlet2) // и только те ТТ, которые можно посетить в этот день и которые не посещали сегодня
                            )
                            // Применяем ограничение по расстоянию (кроме первого перехода)
                            .Where(s =>
                                dailyRoute.Outlets.Count <= 1 || s.Length <= MaxDistanceLimit
                            )
                            .Where(s => s != null) // проверка на отсутсвие null значений
                            .ToList(); // представляем все это списком

                        if (!availableSegments.Any())
                        {
                            // Проверить, есть ли вообще доступные точки, которые еще не посещались сегодня
                            var allAvailableOutlets = segments
                                .Where(s => s.IdOutlet1 == currentOutlet)
                                .Select(s => s.IdOutlet2)
                                .Where(id => !visitedToday.Contains(id))
                                .ToList();

                            if (!allAvailableOutlets.Any())
                            {
                                Console.WriteLine($"Нет доступных непосещенных точек для дня {day} из точки {currentOutlet}");
                                break;
                            }
                            else
                            {
                                Console.WriteLine($"Есть доступные точки, но они недоступны по частоте посещений: {string.Join(", ", allAvailableOutlets)}");
                                break;
                            }
                        }

                        // выбираю лучший вариант
                        var bestSegment = availableSegments
                            .OrderBy(s => visitTracker.GetVisitCount(s.IdOutlet2)) // точки, которые реже всего требуют посещений (чем меньше раз посетили, тем лучше)
                            .ThenBy(s => CalculateSegmentWeight(s, visitTracker, outlets, day)) // точка с наименьшим весом (чем меньше вес тем лучше)
                            .First(); // беру первый элемент из полученного сортированного списка

                        var nextOutlet = bestSegment.IdOutlet2; // находим выбранную точку с ее атрибутами
                        var visitTime = outlets[nextOutlet].TimeVisit; // получаем время визита для выбранной точки
                        var totalTime = bestSegment.Time + visitTime; // общее время складывается из времени в пути до точки + время на визит в ТТ

                        // ФИНАЛЬНАЯ ПРОВЕРКА ПЕРЕД ДОБАВЛЕНИЕМ
                        if (visitedToday.Contains(nextOutlet))
                        {
                            Console.WriteLine($"КРИТИЧЕСКАЯ ОШИБКА: точка {nextOutlet} уже в visitedToday");
                            break;
                        }

                        if (dailyTimeUsed + totalTime > maxDailyTime + TimeTolerance) // проверка на то, что на маршруте сотрудник потратит не больше чем это задано + 10 минут (допуск)
                            break; // выходим из цикла если день заполнен

                        dailyRoute.Outlets.Add( // добавление выбранной точки в дневной маршрут
                            new RoutePoint
                            {
                                OutletId = nextOutlet,
                                VisitTime = visitTime,
                                TravelTime = bestSegment.Time, // время на дорогу до точки
                            }
                        );

                        visitedToday.Add(nextOutlet); // помечаю точку, как посещенную в этот день
                        dailyTimeUsed += totalTime; // прибалвяю затраченное на этот визит время к общему времени на маршруте за этот день
                        currentOutlet = nextOutlet; // теперь точка старта - это та точка в которую я только что приехал
                    }

                    // храню полученный уникальный маршрут в виде списка id точек
                    var routeSignature = string.Join(
                        ",",
                        dailyRoute.Outlets.Select(o => o.OutletId)
                    );
                    if (!routeHistory.Contains(routeSignature) && dailyRoute.Outlets.Count >= 2) // если такой маршрут ранее не был сгенерирован и в маршруте более 2 точек
                    {
                        routeHistory.Add(routeSignature);
                        break; // выход из цикла, если найден уникальный маршрут
                    }
                }

                // если стартовая точка была задана, то не учитываем ее как визит, иначе учитываются все ТТ
                foreach (var point in dailyRoute.Outlets.Skip(useFixedStartPoint ? 1 : 0))
                {
                    visitTracker.RecordVisit(point.OutletId, day);
                }

                if (dailyRoute.Outlets.Count >= 2) // добавляем маршрут только если в нем есть хотя бы 2 точки в день
                {
                    dailyRoute.TotalTime = dailyTimeUsed; // общее время на маршруте
                    schedule.Add(dailyRoute); // добавление в раписание
                }

                if (!useFixedStartPoint) // если стартовая точка не была фиксированной, то начинаем следующий день с новой оптимальной точки
                {
                    // Для следующего дня будет выбрана новая оптимальная точка в начале цикла
                }
            }

            //AddMissingOutlets(schedule, segments, outlets, visitTracker, maxDailyTime, MaxCountVisits); // если маршруты созданы на все дни, но остались точки которые нужно еще посетить, то пытаемся добавить их в уже существующие маршруты
            return schedule; // возвращаю готовое расписание
        }



        private RoutePlanResponse MapToResponse(
            List<DailyRoute> schedule, // итоговое расписание маршрутов
            Dictionary<long, OutletInfo> outlets // свойства точек
        )
        {
            var response = new RoutePlanResponse { Schedule = new List<DailyRouteResponse>() }; // готолю объект, в котором передам ответ от API

            // перебор всех дней из получившегося расписания
            foreach (var route in schedule)
            {
                var dailyRoute = new DailyRouteResponse
                {
                    DayNumber = route.DayNumber,
                    TotalTime = route.TotalTime,
                    TotalCost = route.TotalCost,
                    Outlets = new List<RoutePointResponse>(), // список точек на этот день
                };

                // перебор всех точек маршрута в порядке посещения
                for (int i = 0; i < route.Outlets.Count; i++)
                {
                    var outletId = route.Outlets[i].OutletId; // ID точки из маршрута
                    var outletInfo = outlets[outletId]; // свойства этой точки

                    dailyRoute.Outlets.Add(
                        new RoutePointResponse
                        {
                            RoutePointNumber = i + 1, // Порядковый номер точки
                            OutletId = outletId, // ID точки
                            Priority = outletInfo.Priority, // Приоритет из справочника
                            Frequency = outletInfo.Frequency, // Частота посещений из справочника
                            FrequencyPriority = outletInfo.FrequencyPriority, // Приоритет частоты
                            VisitTime = route.Outlets[i].VisitTime, // Время посещения из маршрута
                            TravelTime = route.Outlets[i].TravelTime, // Время пути из маршрута
                        }
                    );
                }
                // получаю объект для ответа от API
                response.Schedule.Add(dailyRoute);
            }

            // возврщаю этот ответ
            return response;
        }

        static void AddMissingOutlets(
            List<DailyRoute> schedule,
            List<RouteSegment> segments,
            Dictionary<long, OutletInfo> outlets,
            VisitTracker visitTracker,
            double maxDailyTime,
            int maxCountVisitPerDay // максимальное количество посещений в день
        )
        {
            // берем все id точек из списка
            var missingOutlets = outlets
                .Keys.Where(id => visitTracker.GetVisitCount(id) < outlets[id].Frequency) // находим из всего списка точек те, у которых количество посещений не было достигнуто
                .ToList(); // формируем список точек, которые нужно еще посетить

            foreach (var outletId in missingOutlets) // начинаем перебирать все точки
            {
                var outlet = outlets[outletId];
                int visitsNeeded = outlet.Frequency - visitTracker.GetVisitCount(outletId); // считаем какое количество раз, которое нужно еще допосесить точки

                for (int visit = 0; visit < visitsNeeded; visit++)
                {
                    foreach (var dayRoute in schedule.OrderBy(r => r.TotalTime))
                    {
                        // Не превышает ли день лимит по количеству точек
                        if (dayRoute.Outlets.Count >= maxCountVisitPerDay)
                        {
                            continue; // Пропускаем этот день, он уже заполнен
                        }

                        if (visitTracker.GetVisitCount(outletId) >= outlet.Frequency)
                            break;

                        for (int i = 0; i < dayRoute.Outlets.Count - 1; i++)
                        {

                            // Не превысит ли вставка максимальное количество точек
                            if (dayRoute.Outlets.Count >= maxCountVisitPerDay)
                            {
                                break; // Выходим из цикла, день уже заполнен
                            }

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

                                var totalAddedTime = segmentToMissing.Time + outlet.TimeVisit + segmentFromMissing.Time;

                                var originalSegment = segments.FirstOrDefault(s => s.IdOutlet1 == current.OutletId && s.IdOutlet2 == next.OutletId);

                                if (originalSegment == null)
                                    continue; // нет прямого ребра между current и next — пропускаем эту позицию

                                var originalTime = originalSegment.Time;


                                /* версия, которая порождает 500 ошибку при добавлении точек, так как если после оптимизации порядка точек (или в процессе вставки недостающих) в маршруте окажутся соседние точки без прямого ребра в исходном списке
                                 * var originalTime = segments
                                    .First(s =>
                                        s.IdOutlet1 == current.OutletId
                                        && s.IdOutlet2 == next.OutletId
                                    )
                                    .Time;
                                */

                                // Учитываем оба ограничения - время и количество точек
                                if (
                                    dayRoute.TotalTime + totalAddedTime - originalTime <= maxDailyTime + TimeTolerance
                                    && dayRoute.Outlets.Count < maxCountVisitPerDay
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

                                    // После добавления проверяем, не заполнили ли день
                                    if (dayRoute.Outlets.Count >= maxCountVisitPerDay)
                                    {
                                        break; // День заполнен, выходим из цикла
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }


        // метод CalculateSegmentWeight преобразует различные бизнес-правила в единый числовой показатель
        // который позволяет алгоритму принимать обоснованные решения на каждом шаге построения маршрута
        static double CalculateSegmentWeight(
            RouteSegment segment, // сегменты (точки) маршрута
            VisitTracker tracker,
            Dictionary<long, OutletInfo> outlets, // информация об атрибутах точек
            int currentDay // текущий день планирования
        )
        {
            var outlet = outlets[segment.IdOutlet2]; // находим точку, в которую можно пройти из IdOutlet1 и получаем ее приоритет, частоту посещений и т.д.
            double weight = segment.Cost; // получаем "стоимость" проезда до этой точки

            if (outlet.Priority >= 1) // Проверяем, что приоритет задан (>=1)
                weight += PriorityWeightPenalty * outlet.Priority; // умножаем штраф на приоритет, чем меньше приоритет, тем меньше штраф (1 -приоритет самые важные точки)

            var visitsNeeded = tracker.GetRequiredVisits(segment.IdOutlet2, currentDay); // сколько раз нужно посетить точку к текущему дню (на основе частоты посещений и пройденных дней)
            var visitsActual = tracker.GetVisitCount(segment.IdOutlet2); // сколько раз уже посетили точку фактически

            // если точку посещали меньше, чем требуется, уменьшаю вес, делая ее привлекательнее)))
            if (visitsActual < visitsNeeded)
                weight -= (visitsNeeded - visitsActual) * 500;

            // делает точку менее привлекательной после достижения целевого количества посещений
            if (visitsActual >= outlet.Frequency)
                weight -= outlet.FrequencyPriority * FrequencyPriorityWeight;

            // получаем итоговое значение веса для этого перемещения (сегмента)
            return weight;
        }

        // метод CollectAllOutlets агрериует данные о точках в удобном виде, чтобы потом по ним строить маршруты
        static Dictionary<long, OutletInfo> CollectAllOutlets(List<RouteSegment> segments)
        {
            var outlets = new Dictionary<long, OutletInfo>(); // создаем пустой словарь, который будет заполняться информацией о точках

            // прохожу по каждому сегменту маршрута из списка
            foreach (var segment in segments)
            {

                // обработка для первой точки сегмента
                if (!outlets.ContainsKey(segment.IdOutlet1)) // проверяю есть ли уже такая точка в словаре, если нет, то добавляю в словарь
                {
                    outlets[segment.IdOutlet1] = new OutletInfo // создание и добавление записи в словарь
                    {
                        Id = segment.IdOutlet1,
                        Priority = segment.Priority1,
                        Frequency = segment.Frequency1,
                        FrequencyPriority = segment.FrequencyPriority1,
                        TimeVisit = segment.TimeVisit1 > 0 ? segment.TimeVisit1 * 60 : DefaultVisitTime, // переводим время в секунды, если указан 0, то берется дефолтное
                    };
                }

                // обработка для второй точки сегмента
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
            // возвращаю уникальный словарь со всеми парами точек и их атрибутами
            return outlets;
        }

        // преобразование полученных результатов, подготвка данных для отправки результатов
        // преобразует сложную структуру расписания в список записей
        // используется для тестов, когда результат нужно вернуть CSV-файлом
        static List<CsvOutput> WriteToCsv(
            List<DailyRoute> schedule, // расписание маршрутов, которое получилось
            Dictionary<long, OutletInfo> outlets // свойства точек
        )
        {
            var records = new List<CsvOutput>(); // создал список, в который будет выполнятся наполнение результатами

            // перебор всех дней из полученного расписания
            foreach (var route in schedule)
            {
                // для каждого дня перебираю все точки в порядке посещения
                for (int i = 0; i < route.Outlets.Count; i++)
                {
                    var outletId = route.Outlets[i].OutletId; // беру ID точки из маршрута
                    var outletInfo = outlets[outletId]; // ее свойства

                    records.Add(
                        new CsvOutput
                        {
                            DayNumber = route.DayNumber, // Номер дня (из DailyRoute)
                            RoutePointNumber = i + 1, // Порядковый номер точки в маршруте дня
                            OutletId = outletId, // ID точки
                            Priority = outletInfo.Priority, // Приоритет точки (из справочника)
                            Frequency = outletInfo.Frequency, // Частота посещений (из справочника)
                            FrequencyPriority = outletInfo.FrequencyPriority, // Приоритет частоты
                            VisitTime = route.Outlets[i].VisitTime, // Время посещения (из маршрута)
                            TravelTime = route.Outlets[i].TravelTime, // Время пути до точки (из маршрута)
                            TotalRouteTime = route.TotalTime, // Общее время всего маршрута дня
                            TotalRouteCost = route.TotalCost, // Общая стоимость маршрута дня
                        }
                    );
                }
            }
            return records;
        }
    }
}
