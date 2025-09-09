using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Marvin.Tmtmfh91.Web.BackEnd.Data;
using Marvin.Tmtmfh91.Web.BackEnd.Models;
using Marvin.Tmtmfh91.Web.Backend.Services;

namespace Marvin.Tmtmfh91.Web.BackEnd.Controllers;

[ApiController]
[Route("api")]
public class ShoooqController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ShoooqController> _logger;
    private readonly AccessLogService _accessLogService;

    public ShoooqController(ApplicationDbContext context, ILogger<ShoooqController> logger, AccessLogService accessLogService)
    {
        _context = context;
        _logger = logger;
        _accessLogService = accessLogService;
    }

    [HttpGet("test")]
    public ActionResult<string> Test()
    {
        return Ok("Shoooq Controller is working!");
    }

    /// <summary>
    /// 게시물 목록을 조회합니다.
    /// </summary>
    /// <param name="page">페이지 번호 (기본값: 1)</param>
    /// <param name="pageSize">페이지 크기 (기본값: 10, 최대: 100)</param>
    /// <param name="site">단일 사이트 필터</param>
    /// <param name="sites">다중 사이트 필터</param>
    /// <param name="sortBy">정렬 방식: "latest" (최신순), "views" (조회순), "popular" (인기순), "comments" (댓글순)</param>
    /// <param name="keyword">검색 키워드</param>
    /// <param name="author">작성자 필터</param>
    /// <returns>페이징된 게시물 목록</returns>
    [HttpGet("posts")]
    public async Task<ActionResult<PagedResult<SiteBbsInfo>>> GetPosts(
        int page = 1, 
        int pageSize = 10, 
        string? site = null,
        [FromQuery] string[]? sites = null,
        string? sortBy = "latest",
        string? keyword = null,
        string? author = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var query = _context.SiteBbsInfos.AsQueryable();

        // 키워드 검색
        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(x =>
                (x.Title != null && x.Title.Contains(keyword)) ||
                (x.Content != null && x.Content.Contains(keyword)));
        }

        // 작성자 검색
        if (!string.IsNullOrEmpty(author))
        {
            query = query.Where(x => x.Author == author);
        }

        // 단일 사이트 필터 (기존 호환성)
        if (!string.IsNullOrEmpty(site))
        {
            query = query.Where(x => x.Site == site);
        }
        // 다중 사이트 필터 (새로운 기능)
        else if (sites != null && sites.Length > 0)
        {
            var validSites = sites.Where(s => !string.IsNullOrEmpty(s)).ToList();
            if (validSites.Count > 0)
            {
                query = query.Where(x => x.Site != null && validSites.Contains(x.Site));
            }
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        // 정렬 방식에 따른 쿼리 실행
        var posts = sortBy?.ToLower() switch
        {
            "views" => await query
                .OrderByDescending(x => x.Views ?? 0)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(),
            "popular" => await query
                .OrderByDescending(x => x.Likes ?? 0)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(),
            "comments" => await query
                .OrderByDescending(x => x.ReplyNum ?? 0)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(),
            "latest" or _ => (await query.ToListAsync())
                .OrderByDescending(x => DateTime.TryParse(x.Date, out var date) ? date : DateTime.MinValue)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList()
        };

        var result = new PagedResult<SiteBbsInfo>
        {
            Data = posts,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            TotalPages = totalPages,
            HasNextPage = page < totalPages,
            HasPreviousPage = page > 1
        };

        return Ok(result);
    }

    [HttpGet("{no:long}")]
    public async Task<ActionResult<SiteBbsInfo>> GetPost(long no)
    {
        var post = await _context.SiteBbsInfos
            .FirstOrDefaultAsync(x => x.No == no);

        if (post == null)
        {
            return NotFound();
        }

        return Ok(post);
    }

    [HttpGet("sites")]
    public async Task<ActionResult<IEnumerable<string>>> GetSites()
    {
        var sites = await _context.SiteBbsInfos
            .Where(x => !string.IsNullOrEmpty(x.Site))
            .Select(x => x.Site!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync();

        return Ok(sites);
    }


    [HttpGet("popular")]
    public async Task<ActionResult<IEnumerable<SiteBbsInfo>>> GetPopularPosts(int count = 10)
    {
        if (count < 1 || count > 50) count = 10;

        var popularPosts = await _context.SiteBbsInfos
            .Where(x => x.Views.HasValue || x.Likes.HasValue)
            .OrderByDescending(x => (x.Views ?? 0) + (x.Likes ?? 0) * 10)
            .Take(count)
            .ToListAsync();

        return Ok(popularPosts);
    }

    [HttpGet("admin/stats")]
    public async Task<ActionResult> GetAdminStats()
    {
        try
        {
            // 총 게시물 수 (NaverNews, GoogleNews 제외)
            var totalPosts = await _context.SiteBbsInfos
                .Where(x => x.Site != "NaverNews" && x.Site != "GoogleNews")
                .CountAsync();
            
            // 활성 사이트 수 (NaverNews, GoogleNews 제외)
            var activeSites = await _context.SiteBbsInfos
                .Where(x => x.Site != "NaverNews" && x.Site != "GoogleNews")
                .Select(x => x.Site)
                .Distinct()
                .CountAsync();
            
            // 총 방문자 수
            var totalVisitors = await _accessLogService.GetTotalVisitorsAsync();
            
            // 총 접속 수 (일일 조회수로 사용)
            var totalAccess = await _accessLogService.GetTotalAccessCountAsync();

            var stats = new
            {
                totalPosts,
                activeSites,
                totalVisitors,
                dailyViews = totalAccess, // 총 접속수를 일일 조회수로 사용
                systemStatus = "정상"
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting admin stats");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("admin/site-stats")]
    public async Task<ActionResult> GetSiteStats()
    {
        try
        {
            // 오늘 날짜 (UTC)
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);
            var todayUtc = DateTime.SpecifyKind(today, DateTimeKind.Utc);
            var tomorrowUtc = DateTime.SpecifyKind(tomorrow, DateTimeKind.Utc);

            var siteStats = await _context.SiteBbsInfos
                .Where(x => !string.IsNullOrEmpty(x.Site) && 
                           x.Site != "NaverNews" && x.Site != "GoogleNews")
                .GroupBy(x => x.Site)
                .Select(g => new
                {
                    site = g.Key,
                    postCount = g.Count(),
                    todayCount = g.Count(x => x.RegDate.HasValue && 
                                             x.RegDate.Value >= todayUtc && 
                                             x.RegDate.Value < tomorrowUtc),
                    lastPostDate = g.Max(x => x.RegDate)
                })
                .OrderByDescending(x => x.postCount)
                .ToListAsync();

            return Ok(siteStats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting site stats");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("admin/recent-posts-by-crawl")]
    public async Task<ActionResult> GetRecentPostsByCrawlTime(int count = 5)
    {
        try
        {
            if (count < 1 || count > 50) count = 5;

            var recentPosts = await _context.SiteBbsInfos
                .Where(x => x.Site != "NaverNews" && x.Site != "GoogleNews")
                .OrderByDescending(x => x.RegDate)
                .Take(count)
                .Select(x => new
                {
                    no = x.No,
                    title = x.Title,
                    date = x.Date,
                    regDate = x.RegDate,
                    site = x.Site
                })
                .ToListAsync();

            return Ok(recentPosts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent posts by crawl time");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("admin/recent-posts-by-content")]
    public async Task<ActionResult> GetRecentPostsByContentTime(int count = 5)
    {
        try
        {
            if (count < 1 || count > 50) count = 5;

            var recentPosts = await _context.SiteBbsInfos
                .Where(x => !string.IsNullOrEmpty(x.Date) && 
                           x.Site != "NaverNews" && x.Site != "GoogleNews")
                .ToListAsync();

            // Date 필드를 DateTime으로 파싱하여 정렬
            var sortedPosts = recentPosts
                .Where(x => DateTime.TryParse(x.Date, out _))
                .OrderByDescending(x => DateTime.Parse(x.Date!))
                .Take(count)
                .Select(x => new
                {
                    no = x.No,
                    title = x.Title,
                    date = x.Date,
                    regDate = x.RegDate,
                    site = x.Site
                })
                .ToList();

            return Ok(sortedPosts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting recent posts by content time");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("admin/weekly-crawl-stats")]
    public async Task<ActionResult> GetWeeklyCrawlStats()
    {
        try
        {
            // 현재 시간 기준 최근 7일간의 날짜 생성 (UTC로 변환)
            var today = DateTime.UtcNow.Date;
            var weeklyStats = new List<object>();

            for (int i = 6; i >= 0; i--)
            {
                var targetDate = today.AddDays(-i);
                var nextDate = targetDate.AddDays(1);

                // UTC 시간으로 변환하여 비교
                var targetDateUtc = DateTime.SpecifyKind(targetDate, DateTimeKind.Utc);
                var nextDateUtc = DateTime.SpecifyKind(nextDate, DateTimeKind.Utc);

                // 해당 날짜에 크롤링된 게시물 개수 계산 (RegDate 기준, NaverNews, GoogleNews 제외)
                var count = await _context.SiteBbsInfos
                    .Where(x => x.RegDate.HasValue && 
                               x.RegDate.Value >= targetDateUtc && 
                               x.RegDate.Value < nextDateUtc &&
                               x.Site != "NaverNews" && x.Site != "GoogleNews")
                    .CountAsync();

                weeklyStats.Add(new
                {
                    date = targetDate.ToString("yyyy-MM-dd"),
                    count = count
                });
            }

            return Ok(weeklyStats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting weekly crawl stats");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }

    [HttpGet("admin/daily-site-stats")]
    public async Task<ActionResult> GetDailySiteStats()
    {
        try
        {
            // 오늘 날짜 (UTC)
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            // UTC 시간으로 변환
            var todayUtc = DateTime.SpecifyKind(today, DateTimeKind.Utc);
            var tomorrowUtc = DateTime.SpecifyKind(tomorrow, DateTimeKind.Utc);

            // 오늘 하루 사이트별 크롤링 개수 (NaverNews, GoogleNews 제외)
            var siteStats = await _context.SiteBbsInfos
                .Where(x => x.RegDate.HasValue && 
                           x.RegDate.Value >= todayUtc && 
                           x.RegDate.Value < tomorrowUtc &&
                           !string.IsNullOrEmpty(x.Site) &&
                           x.Site != "NaverNews" && x.Site != "GoogleNews")
                .GroupBy(x => x.Site)
                .Select(g => new
                {
                    site = g.Key,
                    count = g.Count()
                })
                .OrderByDescending(x => x.count)
                .ToListAsync();

            return Ok(siteStats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting daily site stats");
            return StatusCode(500, new { message = "Internal server error" });
        }
    }
}

public class PagedResult<T>
{
    public IEnumerable<T> Data { get; set; } = [];
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public int TotalPages { get; set; }
    public bool HasNextPage { get; set; }
    public bool HasPreviousPage { get; set; }
}