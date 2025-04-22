var builder = WebApplication.CreateBuilder(args);

// Add Health Checks
builder.Services.AddHealthChecks()
    .AddAsyncCheck("Database", async () =>
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

        try
        {
            using var connection = new SqlConnection(connectionString);
            // Add timeout to prevent hanging
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await connection.OpenAsync(cts.Token);
            return HealthCheckResult.Healthy("Database is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connection failed", ex);
        }
    },
    tags: new[] { "database", "sql" },
    timeout: TimeSpan.FromSeconds(5))
    .AddAsyncCheck("External API", async () =>
    {
        using var httpClient = new HttpClient();
        var response = await httpClient.GetAsync("https://api.github.com");

        return response.IsSuccessStatusCode
            ? HealthCheckResult.Healthy("API is reachable")
            : HealthCheckResult.Unhealthy("API is down");
    })
    .AddCheck("Memory Usage", () =>
    {
        var usedMemory = GC.GetTotalMemory(false) / (1024 * 1024);
        return usedMemory < 500
            ? HealthCheckResult.Healthy($"Memory usage is {usedMemory}MB")
            : HealthCheckResult.Unhealthy($"High memory usage: {usedMemory}MB");
    })
    .AddCheck("CPU Usage", () =>
    {
        var cpuUsage = System.Diagnostics.Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds;
        return cpuUsage < 80
            ? HealthCheckResult.Healthy($"CPU usage is {cpuUsage}%")
            : HealthCheckResult.Unhealthy($"High CPU usage: {cpuUsage}%");
    })
    .AddCheck("Disk Space", () =>
    {
        var drive = new DriveInfo("C");
        var availableSpace = drive.AvailableFreeSpace / (1024 * 1024 * 1024);

        return availableSpace > 10
            ? HealthCheckResult.Healthy($"Disk space available: {availableSpace}GB")
            : HealthCheckResult.Unhealthy($"Low disk space: {availableSpace}GB");
    });

var app = builder.Build();

app.UseHttpsRedirection();

// Map Health Check endpoint
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = System.Text.Json.JsonSerializer.Serialize(
            new
            {
                status = report.Status.ToString(),
                totalDuration = report.TotalDuration,
                errors = report.Entries.Select(e => new
                {
                    key = e.Key,
                    status = e.Value.Status.ToString(),
                    description = e.Value.Description,
                    duration = e.Value.Duration
                })
            });

        await context.Response.WriteAsync(result);
    }
});

app.Run();