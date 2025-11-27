using Microsoft.EntityFrameworkCore;
using Marvin.Tmtmfh91.Web.BackEnd.Data;
using Marvin.Tmtmfh91.Web.Backend.Models;
using System.Net;
using Dapper;
using Npgsql;

namespace Marvin.Tmtmfh91.Web.Backend.Services;

public class AccessLogService
{
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _context;
    private static readonly TimeZoneInfo KstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");
    private readonly Lazy<string> _connectionString;

    public AccessLogService(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
        _connectionString = new Lazy<string>(() =>
            Environment.GetEnvironmentVariable("DATABASE_URL") ?? _configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Database connection string not found"));
    }

    private DateTime GetKoreanTime()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KstTimeZone);
    }

    public async Task LogAccessAsync(IPAddress ipAddress)
    {
        using var connection = new NpgsqlConnection(_connectionString.Value);

        var sql = @"
            MERGE INTO tmtmfhgi.website_access_log AS target
            USING (SELECT @IpAddress::inet AS ip_address) AS source
            ON target.ip_address = source.ip_address
            WHEN MATCHED THEN
                UPDATE SET
                    access_count = target.access_count + 1,
                    last_access_time = timezone('Asia/Seoul', CURRENT_TIMESTAMP),
                    updated_at = timezone('Asia/Seoul', CURRENT_TIMESTAMP)
            WHEN NOT MATCHED THEN
                INSERT (ip_address, access_count, first_access_time, last_access_time, created_at, updated_at)
                VALUES (
                    source.ip_address,
                    1,
                    timezone('Asia/Seoul', CURRENT_TIMESTAMP),
                    timezone('Asia/Seoul', CURRENT_TIMESTAMP),
                    timezone('Asia/Seoul', CURRENT_TIMESTAMP),
                    timezone('Asia/Seoul', CURRENT_TIMESTAMP)
                );";

        await connection.ExecuteAsync(sql, new { IpAddress = ipAddress.ToString() });
    }

    public async Task<int> GetTotalVisitorsAsync()
    {
        return await _context.WebsiteAccessLogs.CountAsync();
    }

    public async Task<int> GetTotalAccessCountAsync()
    {
        return await _context.WebsiteAccessLogs.SumAsync(log => log.AccessCount);
    }

    public async Task<int> GetTodayVisitorsAsync()
    {
        using var connection = new NpgsqlConnection(_connectionString.Value);
        string YYYYMMDD = $"{DateTime.Now:yyyyMMdd}";
        var sql = "SELECT COUNT(*) CNT FROM tmtmfhgi.website_access_log wal WHERE to_char(last_access_time, 'YYYYMMDD') = @YYYYMMDD";
        return await connection.QuerySingleAsync<int>(sql, new { YYYYMMDD = YYYYMMDD });
    }

    public async Task<List<WebsiteAccessLog>> GetRecentAccessLogsAsync(int count = 10)
    {
        return await _context.WebsiteAccessLogs
            .OrderByDescending(log => log.LastAccessTime)
            .Take(count)
            .ToListAsync();
    }

    public async Task<List<WebsiteAccessLog>> GetTopVisitorsAsync(int count = 10)
    {
        return await _context.WebsiteAccessLogs
            .OrderByDescending(log => log.AccessCount)
            .Take(count)
            .ToListAsync();
    }

    public async Task<Dictionary<string, int>> GetDailyVisitStatsAsync(int days = 7)
    {
        var koreanNow = GetKoreanTime();
        var startDate = koreanNow.Date.AddDays(-days);
        var startDateUtc = TimeZoneInfo.ConvertTimeToUtc(startDate, KstTimeZone);

        var stats = await _context.WebsiteAccessLogs
            .Where(log => log.LastAccessTime >= startDateUtc)
            .ToListAsync(); // 먼저 데이터를 가져온 후 클라이언트에서 그룹핑

        // UTC 시간을 한국 시간으로 변환하여 날짜별로 그룹핑
        var groupedStats = stats
            .GroupBy(log => TimeZoneInfo.ConvertTimeFromUtc(log.LastAccessTime, KstTimeZone).Date)
            .Select(group => new
            {
                Date = group.Key,
                Count = group.Sum(x => x.AccessCount)
            })
            .OrderBy(x => x.Date)
            .ToList();

        return groupedStats.ToDictionary(
            s => s.Date.ToString("yyyy-MM-dd"),
            s => s.Count
        );
    }
}