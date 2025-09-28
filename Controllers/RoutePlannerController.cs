using Microsoft.AspNetCore.Mvc;
using RoutePlannerAPI.Models; // подключаю свою классы
using RoutePlannerAPI.Services; // подключаю свою классы

namespace RoutePlannerAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]  // задаем адрес по которому можно обращаться к API - .../RoutePlanner
    public class RoutePlannerController(RoutePlannerService planner) : Controller
    {
        [HttpPost] // для пост запроса
        public async Task<ActionResult<List<CsvOutput>>> Run(RoutePlanRequest request) // метод асинхронный;
                                                                                       // Task - возвращает результат;
                                                                                       // ActionResult — это тип, который позволяет возвращать различные HTTP-ответы (например, Ok(), NotFound(), BadRequest()).
                                                                                       // Внутри него ожидается список объектов типа CsvOutput
                                                                                       // (RoutePlanRequest request) — Параметр метода. Атрибут [ApiController] говорит фреймворку, что нужно автоматически десериализовать тело JSON из входящего POST-запроса в объект типа RoutePlanRequest
        {
            try
            {
                return Ok(await planner.GenerateScheduleAsync(request)); //  Асинхронный вызов метода GenerateScheduleAsync из вашего сервиса RoutePlannerService, передавая ему введенные пользователем данные (request).
                                                                         //  await приостанавливает выполнение метода, пока не будет получен результат от сервиса.
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
