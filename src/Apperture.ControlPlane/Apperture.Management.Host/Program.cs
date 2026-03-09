using Apperture.Bootstrap;
using Apperture.Persistence.SqlServer;
using Apperture.RuntimePlane.DTFx;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAppertureBootstrap(builder.Configuration);
builder.Services.AddAppertureDTFx();
builder.Services.AddApperturePersistence(builder.Configuration);
builder.Services.AddAppertureSqlSecrets();
builder.Services.AddScoped<Apperture.Core.Security.Trust.IRuntimeEnrollmentService, Apperture.ControlPlane.Security.SqlRuntimeEnrollmentService>();
builder.Services.AddScoped<Apperture.Core.Security.Trust.IRuntimeIdentityService, Apperture.ControlPlane.Security.SqlRuntimeIdentityService>();

var app = builder.Build();
app.MapControllers();
app.Run();
