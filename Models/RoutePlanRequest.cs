namespace RoutePlannerAPI.Models
{
    /// <summary>
    /// модель данных для расчета маршрута
    /// </summary>
    public class RoutePlanRequest
    {
        public int TotalDays { get; set; } // общее количество дней на которое строим маршрут
        public double WorkingHoursPerDay { get; set; } // сколько часов в день может работать сотрудник
        public bool UseFixedStartPoint { get; set; } // задана ли стартовая точка принудительно или нет
        public long FixedStartPointId { get; set; } // идентификатор заданной стартовой точки
        public int MaxCountVisists { get; set; } // максимальное количество визитов в день
        public List<RouteSegmentInput> Segments { get; set; } // список точек
    }
}
