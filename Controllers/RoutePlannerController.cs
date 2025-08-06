using Microsoft.AspNetCore.Mvc;
using RoutePlannerAPI.Models;
using RoutePlannerAPI.Services;

namespace RoutePlannerAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class RoutePlannerController(RoutePlannerService planner) : Controller
    {
        [HttpPost]
        public async Task<ActionResult<List<CsvOutput>>> Run(RoutePlanRequest request)
        {
            try
            {
                return Ok(await planner.GenerateScheduleAsync(request));
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }
    }
}
