using Apperture.Runtime.DTFx;
using Apperture.Runtime.Persistence.SqlServer;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddAppertureDTFx();

var sql = builder.Configuration.GetSection("SqlServer");
var connectionString = GetSqlConnectionString(builder.Configuration, sql);

builder.Services.AddDbContext<AppertureDbContext>(o => o.UseSqlServer(connectionString));

static string GetSqlConnectionString(IConfiguration config, IConfigurationSection sqlSection)
{
    var server = sqlSection["Server"];
    var database = sqlSection["Database"];
    var user = sqlSection["User"];
    var password = sqlSection["Password"];

    if (!string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(user) && !string.IsNullOrEmpty(password))
    {
        var sb = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder
        {
            DataSource = server,
            InitialCatalog = database ?? "Apperture",
            UserID = user,
            Password = password,
            TrustServerCertificate = true
        };
        return sb.ConnectionString;
    }

    return config.GetConnectionString("DefaultConnection")
        ?? "Server=(localdb)\\mssqllocaldb;Database=Apperture;Trusted_Connection=True;";
}

var app = builder.Build();
app.MapControllers();
app.Run();
