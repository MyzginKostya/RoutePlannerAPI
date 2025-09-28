namespace RoutePlannerAPI.Models
{
    // хранение информации о точках, по которым строим маршрут
    public class OutletInfo
    {
        public long Id { get; set; } // уникальный идентификатор ТТ
        public int Priority { get; set; } // приоритет ТТ
        public int Frequency { get; set; } // частота посещения ТТ
        public int FrequencyPriority { get; set; } // приоритет на увеличение частоты посещений
        public double TimeVisit { get; set; } // время на визит в ТТ
    }
}
