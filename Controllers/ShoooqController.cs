using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Marvin.Tmtmfh91.Web.BackEnd.Data;
using Marvin.Tmtmfh91.Web.BackEnd.Models;
using Marvin.Tmtmfh91.Web.Backend.Services;
using Dapper;
using Npgsql;

namespace Marvin.Tmtmfh91.Web.BackEnd.Controllers;

[ApiController]
[Route("api")]
public class ShoooqController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ShoooqController> _logger;
    private readonly AccessLogService _accessLogService;
    private readonly IConfiguration _configuration;

    public ShoooqController(ApplicationDbContext context, ILogger<ShoooqController> logger, AccessLogService accessLogService, IConfiguration configuration)
    {
        _context = context;
        _logger = logger;
        _accessLogService = accessLogService;
        _configuration = configuration;
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
        string? author = null,
        string? isNewsYn = "n")
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        var pageIndex = (page - 1) * pageSize;
        var dataSql = $@"
            SELECT
                score, time_bucket, time_bucket_no,
                posted_dt, site, reg_date,
                reply_num, ""no"", ""number"",
                title, author, ""date"",
                ""views"", likes, url,
                content, total_count
            FROM tmtmfhgi.fetch_site_bbs_info_with_count(@sites, @isNewsYn, @keyword, @pageIndex, @pageSize, NULL)";

        var parameters = new
        {
            sites = sites,
            isNewsYn = isNewsYn,
            keyword = keyword,
            pageIndex = pageIndex,
            pageSize = pageSize,
            //TimeBucket = timeBucket
        };

        Console.WriteLine("--------------------------- Query Debug --------------------------");
        // 파라미터 로깅
        _logger.LogInformation("Parameters: Sites={Sites}, IsNewsYn={IsNewsYn}, Keyword={Keyword}, PageIndex={PageIndex}, pageSize={pageSize}",
            sites == null ? "NULL" : string.Join(",", sites),
            isNewsYn ?? "NULL",
            keyword ?? "NULL",
            pageIndex,
            pageSize);
        Console.WriteLine(dataSql);
        Console.WriteLine("---------------------------------------------------------------");

        var posts = await connection.QueryAsync<SiteBbsInfo>(dataSql, parameters); //total_count
        var totalCount = posts.FirstOrDefault()?.total_count ?? 0;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
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
            .FirstOrDefaultAsync(x => x.no == no);

        if (post == null)
        {
            return NotFound();
        }

        return Ok(post);
    }

    [HttpGet("sites")]
    public async Task<ActionResult<IEnumerable<string>>> GetSites()
    {
        using var connection = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();

        var sql = @"
            SELECT DISTINCT site
            FROM tmtmfhgi.site_bbs_info
            WHERE site IS NOT NULL AND site != ''
            ORDER BY site";

        var sites = await connection.QueryAsync<string>(sql);

        return Ok(sites);
    }


    [HttpGet("popular")]
    public async Task<ActionResult<IEnumerable<SiteBbsInfo>>> GetPopularPosts(int count = 10)
    {
        if (count < 1 || count > 50) count = 10;

        var popularPosts = await _context.SiteBbsInfos
            .Where(x => x.views.HasValue || x.likes.HasValue)
            .OrderByDescending(x => (x.views ?? 0) + (x.likes ?? 0) * 10)
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
                .Where(x => x.site != "NaverNews" && x.site != "GoogleNews")
                .CountAsync();

            // 활성 사이트 수 (NaverNews, GoogleNews 제외)
            var activeSites = await _context.SiteBbsInfos
                .Where(x => x.site != "NaverNews" && x.site != "GoogleNews")
                .Select(x => x.site)
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
                .Where(x => !string.IsNullOrEmpty(x.site) &&
                           x.site != "NaverNews" && x.site != "GoogleNews")
                .GroupBy(x => x.site)
                .Select(g => new
                {
                    site = g.Key,
                    postCount = g.Count(),
                    todayCount = g.Count(x => x.reg_date.HasValue &&
                                             x.reg_date.Value >= todayUtc &&
                                             x.reg_date.Value < tomorrowUtc),
                    lastPostDate = g.Max(x => x.reg_date)
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
                .Where(x => x.site != "NaverNews" && x.site != "GoogleNews")
                .OrderByDescending(x => x.reg_date)
                .Take(count)
                .Select(x => new
                {
                    no = x.no,
                    title = x.title,
                    date = x.date,
                    regDate = x.reg_date,
                    site = x.site
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
                .Where(x => !string.IsNullOrEmpty(x.date) &&
                           x.site != "NaverNews" && x.site != "GoogleNews")
                .ToListAsync();

            // Date 필드를 DateTime으로 파싱하여 정렬
            var sortedPosts = recentPosts
                .Where(x => DateTime.TryParse(x.date, out _))
                .OrderByDescending(x => DateTime.Parse(x.date!))
                .Take(count)
                .Select(x => new
                {
                    no = x.no,
                    title = x.title,
                    date = x.date,
                    regDate = x.reg_date,
                    site = x.site
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
                    .Where(x => x.reg_date.HasValue &&
                               x.reg_date.Value >= targetDateUtc &&
                               x.reg_date.Value < nextDateUtc &&
                               x.site != "NaverNews" && x.site != "GoogleNews")
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
                .Where(x => x.reg_date.HasValue &&
                           x.reg_date.Value >= todayUtc &&
                           x.reg_date.Value < tomorrowUtc &&
                           !string.IsNullOrEmpty(x.site) &&
                           x.site != "NaverNews" && x.site != "GoogleNews")
                .GroupBy(x => x.site)
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