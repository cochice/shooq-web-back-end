using Microsoft.EntityFrameworkCore;
using Marvin.Tmtmfh91.Web.BackEnd.Data;
using Marvin.Tmtmfh91.Web.Backend.Models;
using System.Net;

namespace Marvin.Tmtmfh91.Web.Backend.Services;

public class AccessLogService
{
    private readonly ApplicationDbContext _context;

    public AccessLogService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task LogAccessAsync(IPAddress ipAddress)
    {
        var existingLog = await _context.WebsiteAccessLogs
            .FirstOrDefaultAsync(log => log.IpAddress.Equals(ipAddress));

        if (existingLog != null)
        {
            // 기존 IP 주소인 경우 접속 횟수 증가
            existingLog.AccessCount++;
            existingLog.LastAccessTime = DateTime.UtcNow;
            existingLog.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // 새로운 IP 주소인 경우 새 레코드 생성
            var newLog = new WebsiteAccessLog
            {
                IpAddress = ipAddress,
                AccessCount = 1,
                FirstAccessTime = DateTime.UtcNow,
                LastAccessTime = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
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
        var startDate = DateTime.UtcNow.Date.AddDays(-days);
        
        var stats = await _context.WebsiteAccessLogs
            .Where(log => log.LastAccessTime >= startDate)
            .GroupBy(log => log.LastAccessTime.Date)
            .Select(group => new 
            { 
                Date = group.Key, 
                Count = group.Sum(x => x.AccessCount) 
            })
            .OrderBy(x => x.Date)
            .ToListAsync();

        return stats.ToDictionary(
            s => s.Date.ToString("yyyy-MM-dd"), 
            s => s.Count
        );
    }
}