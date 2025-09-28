namespace RoutePlannerAPI.Models
{
    /// <summary>
    /// следит за тем, чтобы каждая точка была посещена нужное количество раз за период, распределяя визиты равномерно по дням
    /// </summary>
    public class VisitTracker
    {

        private readonly Dictionary<long, int> _visitCounts = new(); // Счетчик посещений каждой точки
        private readonly Dictionary<long, OutletInfo> _outlets; // Справочник точек
        private readonly int _totalDays; // Общее количество дней периода

        public VisitTracker(Dictionary<long, OutletInfo> outlets, int totalDays)
        {
            _outlets = outlets;
            _totalDays = totalDays;
            foreach (var id in outlets.Keys)
                _visitCounts[id] = 0; // Инициализация счетчиков нулями
        }

        // проверяю можно ли посетить точку
        // текущее количество посещений относительно планового
        public bool CanVisit(long outletId, int currentDay)
        {
            var outlet = _outlets[outletId];
            double plannedVisits = (double)outlet.Frequency * currentDay / _totalDays;
            return _visitCounts[outletId] < Math.Ceiling(plannedVisits) || outlet.FrequencyPriority > 0; // можно посещать, если текущее количество посещений меньше планового или если точка имеет высокий приоритет частоты
        }

        public void RecordVisit(long outletId, int day) => _visitCounts[outletId]++; // увеличиваю счетчик посещений

        // получаю количество посещений, которое должно быть
        public int GetVisitCount(long outletId) => _visitCounts.TryGetValue(outletId, out var count) ? count : 0;

        // поиск точки, которая была посещена меньше всего
        public long GetLeastVisitedOutlet(IEnumerable<long> outletIds)
        {
            return outletIds
                .OrderBy(id => GetVisitCount(id)) // сортировка точек по количеству уже совершенных посещений
                .ThenBy(_ => Guid.NewGuid()) // случайный выбор при равенстве условий нескольких точек
                .FirstOrDefault(); // выбор первого элемента из отсортированной коллекции, если коллеция пуста, то возвращается 0
        }

        // рассчитывает, сколько раз точка должна быть посещена к текущему дню
        public int GetRequiredVisits(long outletId, int currentDay)
        {
            var outlet = _outlets[outletId]; // нахожу свойства точки по ее ID
            return (int)Math.Ceiling((double)outlet.Frequency * currentDay / _totalDays);
            // требуется посетить точку (кол-во раз) = (сколько раз нужно посетить за весь период * текущий день / общее кол-во дней)
        }

    }
}
