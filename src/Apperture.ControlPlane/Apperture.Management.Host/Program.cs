using Apperture.Runtime.DTFx;
using Apperture.Runtime.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAppertureDTFx();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;";
builder.Services.AddDbContext<AppertureDbContext>(o => o.UseSqlServer(connectionString));

var app = builder.Build();
app.MapControllers();
app.Run();
