using System.Reflection;
using Microsoft.OpenApi.Models;
using RoutePlannerAPI.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();

ConfigureSwagger(builder);

builder.Services.AddScoped<RoutePlannerService>();

var app = builder.Build();

app.UseRouting();

app.UseHttpsRedirection();

_ = app.UseSwagger();
_ = app.UseSwaggerUI();

_ = app.UseEndpoints(endpoints =>
{
    _ = endpoints.MapControllers();
    _ = endpoints.MapControllerRoute(name: "default", pattern: "{controller}/{action}");
    _ = endpoints.MapSwagger();
});

app.Run();

void ConfigureSwagger(WebApplicationBuilder builder)
{
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "Swagger", Version = "v1" });
    });
}
