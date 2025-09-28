namespace RoutePlannerAPI.Models
{
    // класс хранит результаты расчета маршрутов по дням
    // для изменений внутри работы алгоритма
    public class DailyRoute
    {

        public int DayNumber { get; set; } // номер дня в плане посещений
        public List<RoutePoint> Outlets { get; set; } = new List<RoutePoint>(); // последовательность точек которые нужно посетить в день
        public double TotalTime { get; set; } // общее суммарное время на маршруте
        public double TotalCost { get; set; } // суммарный "вес" маршрута

    }
}
