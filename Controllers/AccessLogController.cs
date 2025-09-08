using Microsoft.AspNetCore.Mvc;
using Marvin.Tmtmfh91.Web.Backend.Services;
using System.Net;

namespace Marvin.Tmtmfh91.Web.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AccessLogController : ControllerBase
{
    private readonly AccessLogService _accessLogService;
    private readonly ILogger<AccessLogController> _logger;

    public AccessLogController(AccessLogService accessLogService, ILogger<AccessLogController> logger)
    {
        _accessLogService = accessLogService;
        _logger = logger;
    }

    [HttpPost("log")]
    public async Task<IActionResult> LogAccess()
    {
        try
        {
            var clientIp = GetClientIpAddress();
            if (clientIp != null)
            {
                await _accessLogService.LogAccessAsync(clientIp);
                return Ok(new { message = "Access logged successfully" });
            }
            
            return BadRequest(new { message = "Could not determine client IP address" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging access");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("stats/total-visitors")]
    public async Task<IActionResult> GetTotalVisitors()
    {
        try
        {
            var totalVisitors = await _accessLogService.GetTotalVisitorsAsync();
            return Ok(new { totalVisitors });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total visitors");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("stats/total-access")]
    public async Task<IActionResult> GetTotalAccessCount()
    {
        try
        {
            var totalAccess = await _accessLogService.GetTotalAccessCountAsync();
            return Ok(new { totalAccess });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting total access count");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("stats/daily/{days:int?}")]
    public async Task<IActionResult> GetDailyStats(int days = 7)
    {
        try
        {
            var stats = await _accessLogService.GetDailyVisitStatsAsync(days);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting daily stats");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("recent/{count:int?}")]
    public async Task<IActionResult> GetRecentAccessLogs(int count = 10)
    {
        try
        {
            var logs = await _accessLogService.GetRecentAccessLogsAsync(count);
            return Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent access logs");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("top-visitors/{count:int?}")]
    public async Task<IActionResult> GetTopVisitors(int count = 10)
    {
        try
        {
            var topVisitors = await _accessLogService.GetTopVisitorsAsync(count);
            return Ok(topVisitors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting top visitors");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    private IPAddress? GetClientIpAddress()
    {
        // X-Forwarded-For 헤더 확인 (프록시나 로드 밸런서 뒤에 있는 경우)
        var xForwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xForwardedFor))
        {
            var ips = xForwardedFor.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (ips.Length > 0 && IPAddress.TryParse(ips[0].Trim(), out var forwardedIp))
            {
                return forwardedIp;
            }
        }

        // X-Real-IP 헤더 확인
        var xRealIp = Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(xRealIp) && IPAddress.TryParse(xRealIp, out var realIp))
        {
            return realIp;
        }

        // RemoteIpAddress 사용
        return Request.HttpContext.Connection.RemoteIpAddress;
    }
}