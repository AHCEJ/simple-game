var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<Games.Game.World>();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets();

app.Map("/ws", async (HttpContext ctx) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var world = ctx.RequestServices.GetRequiredService<Games.Game.World>();
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();

    var name = ctx.Request.Query["name"].ToString();
    var player = world.AddPlayer(name);
    var session = new Games.Game.Session(world, socket, player);
    await session.RunAsync();
});

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

var world = app.Services.GetRequiredService<Games.Game.World>();
world.Start();
app.Lifetime.ApplicationStopping.Register(() => world.StopAsync().GetAwaiter().GetResult());

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
