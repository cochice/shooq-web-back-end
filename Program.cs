using Microsoft.EntityFrameworkCore;
using Marvin.Tmtmfh91.Web.BackEnd.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(
        Environment.GetEnvironmentVariable("DATABASE_URL") ??
        builder.Configuration.GetConnectionString("DefaultConnection")
        ));

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(builder =>
    {
        builder
            .SetIsOriginAllowed(origin => true) // 모든 오리진 허용
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();
    });
});

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// 미들웨어 순서 중요!
app.UseCors(); // 가장 먼저!

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    // Use CORS in development
    app.UseCors("AllowFrontend");
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

// 포트는 환경변수 또는 기본값 사용
var port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
app.Run($"http://0.0.0.0:{port}");

app.Run();
