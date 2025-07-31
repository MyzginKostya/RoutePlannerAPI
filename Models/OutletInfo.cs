namespace RoutePlannerAPI.Models
{
    public class OutletInfo
    {
        public long Id { get; set; }
        public int Priority { get; set; }
        public int Frequency { get; set; }
        public int FrequencyPriority { get; set; }
        public double TimeVisit { get; set; }
    }
}
