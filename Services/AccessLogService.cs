using Microsoft.EntityFrameworkCore;
using Marvin.Tmtmfh91.Web.BackEnd.Data;
using Marvin.Tmtmfh91.Web.Backend.Models;
using System.Net;

namespace Marvin.Tmtmfh91.Web.Backend.Services;

public class AccessLogService
{
    private readonly ApplicationDbContext _context;
    private static readonly TimeZoneInfo KstTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Seoul");

    public AccessLogService(ApplicationDbContext context)
    {
        _context = context;
    }

    private DateTime GetKoreanTime()
    {
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, KstTimeZone);
    }

    public async Task LogAccessAsync(IPAddress ipAddress)
    {
        var koreanNow = GetKoreanTime();
        var koreanNowUtc = TimeZoneInfo.ConvertTimeToUtc(koreanNow, KstTimeZone);

        var existingLog = await _context.WebsiteAccessLogs
            .FirstOrDefaultAsync(log => log.IpAddress.Equals(ipAddress));

        if (existingLog != null)
        {
            // 기존 IP 주소인 경우 접속 횟수 증가
            existingLog.AccessCount++;
            existingLog.LastAccessTime = koreanNowUtc;
            existingLog.UpdatedAt = koreanNowUtc;
        }
        else
        {
            // 새로운 IP 주소인 경우 새 레코드 생성
            var newLog = new WebsiteAccessLog
            {
                IpAddress = ipAddress,
                AccessCount = 1,
                FirstAccessTime = koreanNowUtc,
                LastAccessTime = koreanNowUtc,
                CreatedAt = koreanNowUtc,
                UpdatedAt = koreanNowUtc
            };

            _context.WebsiteAccessLogs.Add(newLog);
        }

        await _context.SaveChangesAsync();
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
        var koreanNow = GetKoreanTime();
        var today = koreanNow.Date;
        var tomorrow = today.AddDays(1);

        // 한국 시간을 UTC로 변환하여 데이터베이스 쿼리에서 비교
        var todayUtc = TimeZoneInfo.ConvertTimeToUtc(today, KstTimeZone);
        var tomorrowUtc = TimeZoneInfo.ConvertTimeToUtc(tomorrow, KstTimeZone);

        return await _context.WebsiteAccessLogs
            .Where(log => log.LastAccessTime >= todayUtc && log.LastAccessTime < tomorrowUtc)
            .CountAsync();
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