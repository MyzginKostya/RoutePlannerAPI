namespace RoutePlannerAPI.Models
{
    /// <summary>
    /// представляет ориентированный сегмент маршрута между двумя точками — это ребро в графе, содержащее все метрики и атрибуты для перемещения от точки A к точке B
    /// </summary>
    public class RouteSegment
    {

        public long IdOutlet1 { get; set; }
        public long IdOutlet2 { get; set; }
        public double Time { get; set; }
        public double Length { get; set; }
        public double Cost { get; set; }
        public int Priority1 { get; set; }
        public int Priority2 { get; set; }
        public double TimeVisit1 { get; set; }
        public double TimeVisit2 { get; set; }
        public int Frequency1 { get; set; }
        public int Frequency2 { get; set; }
        public int FrequencyPriority1 { get; set; }
        public int FrequencyPriority2 { get; set; }
        //public char TypeMovements  {get; set; }

    }
}
