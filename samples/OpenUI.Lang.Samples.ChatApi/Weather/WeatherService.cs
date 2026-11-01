namespace OpenUI.Lang.Samples.ChatApi.Weather;

/// <summary>
/// The weather domain, in memory: fields, a made-up but repeatable forecast for each, and the spray
/// jobs people schedule against it. Jobs live as long as the process.
/// </summary>
public sealed class WeatherService(TimeProvider time)
{
    /// <summary>How many days ahead there is a forecast.</summary>
    public const int Horizon = 14;

    /// <summary>The most wind, in mph, a job can be scheduled in.</summary>
    public const int MaxSprayWindMph = 15;

    /// <summary>The highest chance of rain, in percent, a job can be scheduled in. Rain at this chance or above is too much.</summary>
    public const int MaxSprayRainChance = 60;

    /// <summary>The products a job can name.</summary>
    public static readonly IReadOnlyList<string> Products = ["Fungicide", "Herbicide", "Insecticide"];

    /// <summary>The statuses a job can have.</summary>
    public static readonly IReadOnlyList<string> Statuses = ["scheduled"];

    private static readonly FieldLocation[] Fields =
    [
        new("north-field", "North field", "Wheat"),
        new("river-bend", "River bend", "Corn"),
        new("east-ridge", "East ridge", "Soybeans"),
    ];

    private readonly Lock _gate = new();
    private readonly List<SprayJob> _jobs = Seed(time);

    /// <summary>The fields there is a forecast for.</summary>
    public IReadOnlyList<FieldLocation> Locations => Fields;

    /// <summary>The forecast for a field, today first, or null when there is no such field.</summary>
    /// <param name="location">A field id.</param>
    /// <param name="days">How many days, 1 to <see cref="Horizon"/>.</param>
    public Forecast? GetForecast(string location, int days)
    {
        if (Fields.All(field => field.Id != location))
        {
            return null;
        }

        var today = Today();
        return new Forecast(location, [.. Enumerable.Range(0, days).Select(offset => Day(location, today.AddDays(offset)))]);
    }

    /// <summary>The jobs, soonest first.</summary>
    /// <param name="status">Only jobs with this status, or null for all.</param>
    public IReadOnlyList<SprayJob> GetJobs(string? status)
    {
        lock (_gate)
        {
            return [.. _jobs.Where(job => status is null || job.Status == status).OrderBy(job => job.Date).ThenBy(job => job.Id, StringComparer.Ordinal)];
        }
    }

    /// <summary>Checks a request and, when it is sound, puts the job on the calendar.</summary>
    public ScheduleResult Schedule(ScheduleSprayJobRequest? request)
    {
        var fields = new Dictionary<string, string>();
        var location = Required(request?.Location, nameof(request.Location), fields);
        var product = Required(request?.Product, nameof(request.Product), fields);
        var dateText = Required(request?.Date, nameof(request.Date), fields);

        if (location is not null && Fields.All(field => field.Id != location))
        {
            fields["location"] = $"Unknown field '{location}'.";
        }

        if (product is not null && !Products.Contains(product))
        {
            fields["product"] = $"Unknown product '{product}'. Use one of: {string.Join(", ", Products)}.";
        }

        DateOnly date = default;
        if (dateText is not null && !DateOnly.TryParseExact(dateText, "yyyy-MM-dd", out date))
        {
            fields["date"] = $"'{dateText}' is not a date. Use yyyy-MM-dd.";
        }

        if (fields.Count > 0)
        {
            return ScheduleResult.Invalid(new ApiError($"The job could not be scheduled. {string.Join(" ", fields.Select(field => $"{field.Key}: {field.Value}"))}", fields));
        }

        var today = Today();
        if (date < today || date > today.AddDays(Horizon - 1))
        {
            return ScheduleResult.Invalid(new ApiError(
                $"There is no forecast for {date:yyyy-MM-dd}. Pick a day from {today:yyyy-MM-dd} to {today.AddDays(Horizon - 1):yyyy-MM-dd}.",
                new Dictionary<string, string> { ["date"] = "Outside the forecast." }));
        }

        var day = Day(location!, date);
        if (!day.Sprayable)
        {
            return ScheduleResult.Rejected(new ApiError(
                $"{date:yyyy-MM-dd} is not a spraying day for {location}: wind {day.WindMph} mph (limit {MaxSprayWindMph}), rain chance {day.RainChance}% (limit under {MaxSprayRainChance}%)."));
        }

        lock (_gate)
        {
            if (_jobs.Any(job => job.Location == location && job.Date == date))
            {
                return ScheduleResult.Conflict(new ApiError($"{location} already has a job on {date:yyyy-MM-dd}."));
            }

            var job = new SprayJob($"job-{_jobs.Count + 1}", location!, product!, date, "scheduled", time.GetUtcNow());
            _jobs.Add(job);
            return ScheduleResult.Created(job);
        }
    }

    private DateOnly Today() => DateOnly.FromDateTime(time.GetLocalNow().DateTime);

    // The same field and day always give the same weather, so a fetch after a refresh shows the same numbers.
    private static DayForecast Day(string location, DateOnly date)
    {
        var seed = Fnv1a($"{location}/{date:yyyy-MM-dd}");
        var high = 52 + (int)(seed % 30);
        var low = high - 12 - (int)(seed >> 8 & 0xff) % 8;
        var rain = (int)(seed >> 12 & 0xfff) % 100;
        var wind = 3 + (int)(seed >> 22) % 22;
        return new DayForecast(date, high, low, rain, wind, wind <= MaxSprayWindMph && rain < MaxSprayRainChance);
    }

    private static List<SprayJob> Seed(TimeProvider time)
    {
        var today = DateOnly.FromDateTime(time.GetLocalNow().DateTime);
        var now = time.GetUtcNow();
        return
        [
            new("job-1", "north-field", "Herbicide", NextSprayableDay("north-field", today.AddDays(1)), "scheduled", now),
            new("job-2", "east-ridge", "Fungicide", NextSprayableDay("east-ridge", today.AddDays(2)), "scheduled", now),
        ];
    }

    // The seed jobs sit on days that pass the same check a new job does.
    private static DateOnly NextSprayableDay(string location, DateOnly from)
    {
        var date = from;
        while (!Day(location, date).Sprayable)
        {
            date = date.AddDays(1);
        }

        return date;
    }

    private static string? Required(string? value, string name, Dictionary<string, string> fields)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        fields[char.ToLowerInvariant(name[0]) + name[1..]] = "Required.";
        return null;
    }

    // string.GetHashCode is randomized per process; this is not.
    private static uint Fnv1a(string text)
    {
        var hash = 2166136261u;
        foreach (var c in text)
        {
            hash ^= c;
            hash *= 16777619u;
        }

        // FNV alone leaves consecutive dates with near-identical high bits; the finalizer spreads them.
        hash ^= hash >> 16;
        hash *= 0x85ebca6bu;
        hash ^= hash >> 13;
        hash *= 0xc2b2ae35u;
        hash ^= hash >> 16;
        return hash;
    }
}

/// <summary>How a request to schedule a job ended.</summary>
public sealed record ScheduleResult(SprayJob? Job, ApiError? Error, int StatusCode)
{
    /// <summary>The job was added.</summary>
    public static ScheduleResult Created(SprayJob job) => new(job, null, StatusCodes.Status201Created);

    /// <summary>The request is malformed or names something that does not exist.</summary>
    public static ScheduleResult Invalid(ApiError error) => new(null, error, StatusCodes.Status400BadRequest);

    /// <summary>The request is sound but the weather rules it out.</summary>
    public static ScheduleResult Rejected(ApiError error) => new(null, error, StatusCodes.Status422UnprocessableEntity);

    /// <summary>The field already has a job that day.</summary>
    public static ScheduleResult Conflict(ApiError error) => new(null, error, StatusCodes.Status409Conflict);
}
