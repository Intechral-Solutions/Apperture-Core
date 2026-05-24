var builder = DistributedApplication.CreateBuilder(args);

var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", sqlPassword)
    .WithImageTag("2022-latest")
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume("apperture-aspire-sql");

var db = sql.AddDatabase("Apperture");

var managementApi = builder.AddProject<Projects.Apperture_Management_Host>("management-api")
    .WithHttpEndpoint(port: 9000, name: "http")
    .WithReference(db, connectionName: "DefaultConnection")
    .WaitFor(db);

builder.AddProject<Projects.Apperture_Management_Ui_Server>("management-ui")
    .WithHttpEndpoint(port: 9001, name: "http")
    .WaitFor(managementApi);

builder.AddProject<Projects.Apperture_RuntimeHost_Windows_WorkerHost>("worker")
    .WithReference(db, connectionName: "DefaultConnection")
    .WithEnvironment("DOTNET_ENVIRONMENT", "Development")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithEnvironment("APERTURE_RUNTIME_ID", "worker-local")
    .WaitFor(db);

builder.Build().Run();
