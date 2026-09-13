var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter("sql-password", secret: true);

// Stable host port (default 14333) so local tools / Platform Settings can target Aspire SQL
// without chasing a random mapping each start. Keep 1433 free for script/docker-compose SQL.
const int defaultSqlHostPort = 14333;
var sqlHostPortRaw = builder.Configuration["Aspire:SqlServer:HostPort"];
var sqlHostPort = string.IsNullOrWhiteSpace(sqlHostPortRaw)
    ? defaultSqlHostPort
    : int.TryParse(sqlHostPortRaw, out var parsedPort) ? parsedPort : -1;
if (sqlHostPort is <= 0 or > 65535)
    throw new InvalidOperationException(
        $"Aspire:SqlServer:HostPort must be an integer between 1 and 65535 (configured: '{sqlHostPortRaw}').");

var sql = builder.AddSqlServer("sql", sqlPassword, port: sqlHostPort)
    .WithImageTag("2022-latest")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("apperture-aspire-sql");

var db = sql.AddDatabase("Apperture");

var managementApi = builder.AddProject<Projects.Apperture_Management_Host>("management-api")
    .WithHttpEndpoint(port: 9000, name: "http")
    .WithHttpHealthCheck("/health")
    .WithReference(db, connectionName: "DefaultConnection")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WaitFor(db);

builder.AddProject<Projects.Apperture_Management_Ui_Server>("management-ui")
    .WithHttpEndpoint(port: 9001, name: "http")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WaitFor(managementApi);

builder.AddProject<Projects.Apperture_RuntimeHost_Windows_WorkerHost>("worker")
    .WithReference(db, connectionName: "DefaultConnection")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    // Host/claim identity (ScheduleRuns.ClaimedByHostId). Independent of schedule plane (SWM-1).
    .WithEnvironment("APERTURE_RUNTIME_ID", "worker-local")
    // Must match SchedulesController / IntegrationSchedulesController assignment default ("local").
    .WithEnvironment("ScheduleDispatch__RuntimePlaneId", "local")
    .WaitFor(managementApi);

builder.Build().Run();
