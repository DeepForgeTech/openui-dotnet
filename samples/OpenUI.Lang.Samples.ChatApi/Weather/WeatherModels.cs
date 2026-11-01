namespace OpenUI.Lang.Samples.ChatApi.Weather;

/// <summary>A field the farm sprays, with the crop in it.</summary>
public sealed record FieldLocation(string Id, string Name, string Crop);

/// <summary>One day of a field's forecast.</summary>
/// <param name="Date">The day, as <c>yyyy-MM-dd</c>.</param>
/// <param name="HighF">The high, in degrees Fahrenheit.</param>
/// <param name="LowF">The low, in degrees Fahrenheit.</param>
/// <param name="RainChance">The chance of rain, 0 to 100.</param>
/// <param name="WindMph">The wind, in miles per hour.</param>
/// <param name="Sprayable">True when wind and rain are both low enough to spray.</param>
public sealed record DayForecast(DateOnly Date, int HighF, int LowF, int RainChance, int WindMph, bool Sprayable);

/// <summary>A field's forecast, today first.</summary>
public sealed record Forecast(string Location, IReadOnlyList<DayForecast> Days);

/// <summary>The jobs that match a query.</summary>
public sealed record SprayJobList(IReadOnlyList<SprayJob> Jobs);

/// <summary>The fields there is a forecast for.</summary>
public sealed record LocationList(IReadOnlyList<FieldLocation> Locations);

/// <summary>A spray job on the calendar.</summary>
public sealed record SprayJob(string Id, string Location, string Product, DateOnly Date, string Status, DateTimeOffset CreatedAt);

/// <summary>The body of <c>POST /api/spray-jobs</c>. Every member is optional here so a missing one is reported as a validation error.</summary>
/// <param name="Location">A field id from the locations list.</param>
/// <param name="Product">One of <see cref="WeatherService.Products"/>.</param>
/// <param name="Date">The day to spray, as <c>yyyy-MM-dd</c>, within the forecast.</param>
public sealed record ScheduleSprayJobRequest(string? Location, string? Product, string? Date);

/// <summary>The body of every error response.</summary>
/// <param name="Error">One sentence a person can read, which is also the message a tool call fails with.</param>
/// <param name="Fields">The request members that are wrong, with why, or null when no one member is to blame.</param>
public sealed record ApiError(string Error, IReadOnlyDictionary<string, string>? Fields = null);
