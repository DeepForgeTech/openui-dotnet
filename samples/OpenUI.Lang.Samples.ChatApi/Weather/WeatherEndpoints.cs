namespace OpenUI.Lang.Samples.ChatApi.Weather;

/// <summary>
/// The weather endpoints. Each is also a tool that an OpenUI Lang <c>Query</c> or <c>Mutation</c>
/// calls, under the name in its <c>WithName</c>: <c>listLocations</c>, <c>getForecast</c>,
/// <c>listSprayJobs</c> and <c>scheduleSprayJob</c>. The tool's arguments are the endpoint's query
/// string or body, and its result is the response body.
/// </summary>
public static class WeatherEndpoints
{
    private const int DefaultDays = 7;

    /// <summary>Maps the weather endpoints under <c>/api</c>.</summary>
    public static IEndpointRouteBuilder MapWeatherApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/locations", (WeatherService weather) => new LocationList(weather.Locations))
            .WithName("listLocations");

        endpoints.MapGet("/api/forecast", GetForecast).WithName("getForecast");
        endpoints.MapGet("/api/spray-jobs", GetJobs).WithName("listSprayJobs");
        endpoints.MapPost("/api/spray-jobs", Schedule).WithName("scheduleSprayJob");
        return endpoints;
    }

    // GET /api/forecast?location=north-field&days=7
    private static IResult GetForecast(WeatherService weather, string? location, string? days)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return Results.Json(new ApiError("location is required.", new Dictionary<string, string> { ["location"] = "Required." }), statusCode: StatusCodes.Status400BadRequest);
        }

        var count = DefaultDays;
        if (days is not null && !(int.TryParse(days, out count) && count is >= 1 and <= WeatherService.Horizon))
        {
            return Results.Json(
                new ApiError($"days must be a whole number from 1 to {WeatherService.Horizon}.", new Dictionary<string, string> { ["days"] = "Out of range." }),
                statusCode: StatusCodes.Status400BadRequest);
        }

        return weather.GetForecast(location, count) is { } forecast
            ? Results.Json(forecast)
            : Results.Json(new ApiError($"Unknown field '{location}'."), statusCode: StatusCodes.Status404NotFound);
    }

    // GET /api/spray-jobs?status=scheduled
    private static IResult GetJobs(WeatherService weather, string? status) =>
        status is not null && !WeatherService.Statuses.Contains(status)
            ? Results.Json(
                new ApiError($"Unknown status '{status}'. Use one of: {string.Join(", ", WeatherService.Statuses)}.", new Dictionary<string, string> { ["status"] = "Unknown." }),
                statusCode: StatusCodes.Status400BadRequest)
            : Results.Json(new SprayJobList(weather.GetJobs(status)));

    // POST /api/spray-jobs  { "location": "north-field", "product": "Fungicide", "date": "2026-11-03" }
    private static IResult Schedule(WeatherService weather, ScheduleSprayJobRequest? request)
    {
        var result = weather.Schedule(request);
        return result.Job is { } job
            ? Results.Json(job, statusCode: result.StatusCode)
            : Results.Json(result.Error, statusCode: result.StatusCode);
    }
}
