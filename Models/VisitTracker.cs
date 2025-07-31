namespace RoutePlannerAPI.Models
{
    public class VisitTracker
    {

        private readonly Dictionary<long, int> _visitCounts = new();
        private readonly Dictionary<long, OutletInfo> _outlets;
        private readonly int _totalDays;

        public VisitTracker(Dictionary<long, OutletInfo> outlets, int totalDays)
        {
            _outlets = outlets;
            _totalDays = totalDays;
            foreach (var id in outlets.Keys)
                _visitCounts[id] = 0;
        }

        public bool CanVisit(long outletId, int currentDay)
        {
            var outlet = _outlets[outletId];
            double plannedVisits = (double)outlet.Frequency * currentDay / _totalDays;
            return _visitCounts[outletId] < Math.Ceiling(plannedVisits) || outlet.FrequencyPriority > 0;
        }

        public void RecordVisit(long outletId, int day) => _visitCounts[outletId]++;

        public int GetVisitCount(long outletId) => _visitCounts.TryGetValue(outletId, out var count) ? count : 0;

        public long GetLeastVisitedOutlet(IEnumerable<long> outletIds)
        {
            return outletIds
                .OrderBy(id => GetVisitCount(id))
                .ThenBy(_ => Guid.NewGuid())
                .FirstOrDefault();
        }

        public int GetRequiredVisits(long outletId, int currentDay)
        {
            var outlet = _outlets[outletId];
            return (int)Math.Ceiling((double)outlet.Frequency * currentDay / _totalDays);
        }

    }
}
