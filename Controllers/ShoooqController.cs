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
    [HttpGet("posts-old")]
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
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 10;

            // sortBy 파라미터를 timeBucket으로 변환
            string? timeBucket = sortBy switch
            {
                "1h" => "1h",
                "6h" => "6h",
                "12h" => "12h",
                "1d" => "1d",
                "latest" => null,
                "views" => null,
                "popular" => null,
                "comments" => null,
                _ => null
            };

            var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ??
                                  _configuration.GetConnectionString("DefaultConnection");
            using var connection = new NpgsqlConnection(connectionString);
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
                FROM tmtmfhgi.fetch_site_bbs_info_with_count(@sites, @isNewsYn, @keyword, @pageIndex, @pageSize, @timeBucket)";

            var parameters = new
            {
                sites = sites,
                isNewsYn = isNewsYn,
                keyword = keyword,
                pageIndex = pageIndex,
                pageSize = pageSize,
                timeBucket = timeBucket
            };

            _logger.LogInformation("API Call: /api/posts - Parameters: Sites={Sites}, IsNewsYn={IsNewsYn}, Keyword={Keyword}, PageIndex={PageIndex}, PageSize={PageSize}, TimeBucket={TimeBucket}",
                sites == null ? "NULL" : string.Join(",", sites),
                isNewsYn ?? "NULL",
                keyword ?? "NULL",
                pageIndex,
                pageSize,
                timeBucket ?? "NULL");

            var posts = await connection.QueryAsync<SiteBbsInfo>(dataSql, parameters);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetPosts API: {Message}", ex.Message);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// 게시물 목록을 조회합니다. (Prepared Statement 방식)
    /// </summary>
    /// <param name="page">페이지 번호 (기본값: 1)</param>
    /// <param name="pageSize">페이지 크기 (기본값: 10, 최대: 100)</param>
    /// <param name="site">단일 사이트 필터</param>
    /// <param name="keyword">검색 키워드</param>
    /// <param name="author">작성자 필터</param>
    /// <returns>페이징된 게시물 목록</returns>
    [HttpGet("posts")]
    public async Task<ActionResult<PagedResult<SiteBbsInfo>>> GetPostsPs(
        int page = 1,
        int pageSize = 10,
        string? site = null,
        string? keyword = null,
        string? author = null)
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 10;

            var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ??
                                  _configuration.GetConnectionString("DefaultConnection");
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var pageIndex = page - 1;
            var sql = @"
                WITH current_seoul_time AS (
                    SELECT timezone('Asia/Seoul', CURRENT_TIMESTAMP) as now_time
                ),
                filtered AS (
                    SELECT
                        s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                        s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                        (
                            COALESCE(s.likes, 0) * 10
                            + COALESCE(s.reply_num, 0) * 3
                            + (CASE WHEN s.site = '82Cook'
                                THEN (COALESCE(s.""views"", 0) / 1000)
                                ELSE COALESCE(s.""views"", 0) END
                            ) * CASE s.site
                                    -- 조회수 높은 커뮤니티 (가중치 낮춤)
                                    WHEN 'TheQoo' THEN 0.5
                                    WHEN 'HumorUniv' THEN 0.5
                                    WHEN '82Cook' THEN 1.0  -- 이미 /1000 되어있음
                                    WHEN 'Dogdrip' THEN 2.0
                                    
                                    -- 중간 커뮤니티
                                    WHEN 'BobeDream' THEN 3.0
                                    WHEN 'StrClub' THEN 4.0
                                    WHEN 'Inven' THEN 5.0
                                    WHEN 'Ruliweb' THEN 5.0
                                    WHEN 'Clien' THEN 5.0
                                    WHEN 'TodayHumor' THEN 6.0
                                    
                                    -- 조회수 낮은 커뮤니티 (가중치 높임)
                                    WHEN 'Damoang' THEN 50.0
                                    WHEN 'MlbPark' THEN 200.0
                                    
                                    ELSE 3.0
                                END
                        ) AS score,
                        CASE
                            WHEN s.posted_dt >= cst.now_time - INTERVAL '1 hour' THEN 1
                            WHEN s.posted_dt >= cst.now_time - INTERVAL '3 hours' THEN 3
                            WHEN s.posted_dt >= cst.now_time - INTERVAL '9 hours' THEN 9
                            WHEN s.posted_dt >= cst.now_time - INTERVAL '24 hours' THEN 24
                            --WHEN s.posted_dt >= cst.now_time - INTERVAL '7 days' THEN 700
                            ELSE 999
                        END AS time_bucket_no, cloudinary_url
                    FROM tmtmfhgi.site_bbs_info s
                    LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id
                    CROSS JOIN current_seoul_time cst
                    WHERE s.posted_dt >= cst.now_time - INTERVAL '24 hours'
                    AND (@p_site IS NULL OR @p_site = '' OR s.site = @p_site)
                    AND s.site NOT IN ('NaverNews', 'GoogleNews')
                    AND (
                            @p_keyword IS NULL OR @p_keyword = ''
                            OR s.title ILIKE '%' || @p_keyword || '%'
                            OR s.""content"" ILIKE '%' || @p_keyword || '%'
                    )
                ),
                counted AS (
                    SELECT *, COUNT(*) OVER() as total_count
                    FROM filtered
                )
                SELECT
                    c.score, c.time_bucket_no, c.posted_dt, c.site, c.reg_date, c.reply_num,
                    c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                    c.url, c.""content"", c.total_count, c.cloudinary_url
                FROM counted c
                ORDER BY c.time_bucket_no ASC, c.score DESC
                OFFSET (@p_page_index * @p_page_count)
                LIMIT @p_page_count
            ";

            var parameters = new
            {
                p_site = site,
                p_keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
                p_page_index = pageIndex,
                p_page_count = pageSize
            };

            _logger.LogInformation("API Call: /api/posts - Parameters: Site={site}, Keyword={Keyword}, PageIndex={PageIndex}, PageSize={PageSize}",
                site,
                keyword ?? "NULL",
                pageIndex,
                pageSize);

            var posts = await connection.QueryAsync<dynamic>(sql, parameters);
            var postsList = posts.ToList();
            var totalCount = postsList.FirstOrDefault()?.total_count != null ? Convert.ToInt32(postsList.FirstOrDefault()?.total_count) : 0;
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            // Convert dynamic objects to SiteBbsInfo objects
            var siteBbsInfos = postsList.Select(p => new SiteBbsInfo
            {
                no = (long)p.no,
                number = p.number != null ? (long?)p.number : null,
                title = p.title,
                author = p.author,
                date = p.date,
                views = p.views != null ? (int?)Convert.ToInt32(p.views) : null,
                likes = p.likes != null ? (int?)Convert.ToInt32(p.likes) : null,
                url = p.url,
                site = p.site,
                reg_date = p.reg_date,
                reply_num = p.reply_num != null ? (int?)Convert.ToInt32(p.reply_num) : null,
                content = p.content,
                posted_dt = p.posted_dt?.ToString(),
                total_count = p.total_count != null ? (int?)Convert.ToInt32(p.total_count) : null,
                score = p.score != null ? (long?)p.score : null,
                time_bucket = p.time_bucket,
                time_bucket_no = p.time_bucket_no != null ? (int?)Convert.ToInt32(p.time_bucket_no) : null,
                cloudinary_url = p.cloudinary_url
            }).ToList();

            var result = new PagedResult<SiteBbsInfo>
            {
                Data = siteBbsInfos,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasNextPage = page < totalPages,
                HasPreviousPage = page > 1
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetPostsPs API: {Message}", ex.Message);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// 게시물 목록을 조회합니다. (Prepared Statement 방식)
    /// </summary>
    /// <param name="page">페이지 번호 (기본값: 1)</param>
    /// <param name="pageSize">페이지 크기 (기본값: 10, 최대: 100)</param>
    /// <param name="site">단일 사이트 필터</param>
    /// <param name="sites">다중 사이트 필터</param>
    /// <param name="sortBy">정렬 방식: "latest" (최신순), "views" (조회순), "popular" (인기순), "comments" (댓글순)</param>
    /// <param name="keyword">검색 키워드</param>
    /// <param name="author">작성자 필터</param>
    /// <returns>페이징된 게시물 목록</returns>
    [HttpGet("posts-main")]
    public async Task<ActionResult<MainPagedResult<SiteBbsInfo>>> GetPostsMain(
        string? keyword = null,
        string? author = null,
        string? isNewsYn = "n")
    {
        try
        {
            var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ?? _configuration.GetConnectionString("DefaultConnection");
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            #region [ Query ]

            var sql = @"
                (
                SELECT
                    s.""no"",
                    s.""number"",
                    s.title,
                    s.author,
                    s.""date"",
                    s.""views"",
                    s.likes,
                    s.url,
                    s.site,
                    s.reg_date,
                    s.reply_num,
                    s.""content"",
                    s.posted_dt,
                    '1h' AS time_bucket,
                    (
                        COALESCE(s.likes, 0) * 10
                        + COALESCE(s.reply_num, 0) * 3
                        + (CASE WHEN s.site = '82Cook' THEN (COALESCE(s.""views"", 0) / 1000) ELSE COALESCE(s.""views"", 0) END)
                        * (CASE s.site
                            -- 조회수 높은 커뮤니티 (가중치 낮춤)
                            WHEN 'TheQoo' THEN 0.5
                            WHEN 'HumorUniv' THEN 0.5
                            WHEN '82Cook' THEN 1.0  -- 이미 /1000 되어있음
                            WHEN 'Dogdrip' THEN 2.0
                            
                            -- 중간 커뮤니티
                            WHEN 'BobeDream' THEN 3.0
                            WHEN 'StrClub' THEN 4.0
                            WHEN 'Inven' THEN 5.0
                            WHEN 'Ruliweb' THEN 5.0
                            WHEN 'Clien' THEN 5.0
                            WHEN 'TodayHumor' THEN 6.0
                            
                            -- 조회수 낮은 커뮤니티 (가중치 높임)
                            WHEN 'Damoang' THEN 50.0
                            WHEN 'MlbPark' THEN 200.0
                            
                            ELSE 3.0
                        END)
                    ) AS score, cloudinary_url
                FROM tmtmfhgi.site_bbs_info s
                LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id
                WHERE s.posted_dt >= timezone('Asia/Seoul', CURRENT_TIMESTAMP) - INTERVAL '3 hour'
                AND (
                    @p_keyword IS NULL OR @p_keyword = ''
                    OR s.title ILIKE '%' || @p_keyword || '%'
                    OR s.""content"" ILIKE '%' || @p_keyword || '%'
                )
                AND (@p_is_news_yn != 'n' OR s.site NOT IN ('NaverNews', 'GoogleNews'))
                ORDER BY (
                    COALESCE(s.likes, 0) * 10
                    + COALESCE(s.reply_num, 0) * 3
                    + (CASE WHEN s.site = '82Cook' THEN (COALESCE(s.""views"", 0) / 1000) ELSE COALESCE(s.""views"", 0) END)
                    * (CASE s.site
                            -- 조회수 높은 커뮤니티 (가중치 낮춤)
                            WHEN 'TheQoo' THEN 0.5
                            WHEN 'HumorUniv' THEN 0.5
                            WHEN '82Cook' THEN 1.0  -- 이미 /1000 되어있음
                            WHEN 'Dogdrip' THEN 2.0
                            
                            -- 중간 커뮤니티
                            WHEN 'BobeDream' THEN 3.0
                            WHEN 'StrClub' THEN 4.0
                            WHEN 'Inven' THEN 5.0
                            WHEN 'Ruliweb' THEN 5.0
                            WHEN 'Clien' THEN 5.0
                            WHEN 'TodayHumor' THEN 6.0
                            
                            -- 조회수 낮은 커뮤니티 (가중치 높임)
                            WHEN 'Damoang' THEN 50.0
                            WHEN 'MlbPark' THEN 200.0
                            
                            ELSE 3.0
                        END)
                ) DESC
                LIMIT 20
                )
                UNION ALL
                (
                SELECT
                    s.""no"",
                    s.""number"",
                    s.title,
                    s.author,
                    s.""date"",
                    s.""views"",
                    s.likes,
                    s.url,
                    s.site,
                    s.reg_date,
                    s.reply_num,
                    s.""content"",
                    s.posted_dt,
                    '3h' AS time_bucket,
                    (
                        COALESCE(s.likes, 0) * 10
                        + COALESCE(s.reply_num, 0) * 3
                        + (CASE WHEN s.site = '82Cook' THEN (COALESCE(s.""views"", 0) / 1000) ELSE COALESCE(s.""views"", 0) END)
                        * (CASE s.site
                            -- 조회수 높은 커뮤니티 (가중치 낮춤)
                            WHEN 'TheQoo' THEN 0.5
                            WHEN 'HumorUniv' THEN 0.5
                            WHEN '82Cook' THEN 1.0  -- 이미 /1000 되어있음
                            WHEN 'Dogdrip' THEN 2.0
                            
                            -- 중간 커뮤니티
                            WHEN 'BobeDream' THEN 3.0
                            WHEN 'StrClub' THEN 4.0
                            WHEN 'Inven' THEN 5.0
                            WHEN 'Ruliweb' THEN 5.0
                            WHEN 'Clien' THEN 5.0
                            WHEN 'TodayHumor' THEN 6.0
                            
                            -- 조회수 낮은 커뮤니티 (가중치 높임)
                            WHEN 'Damoang' THEN 50.0
                            WHEN 'MlbPark' THEN 200.0
                            
                            ELSE 3.0
                        END)
                    ) AS score, cloudinary_url
                FROM tmtmfhgi.site_bbs_info s
                LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id
                WHERE s.posted_dt < timezone('Asia/Seoul', CURRENT_TIMESTAMP) - INTERVAL '3 hour' AND s.posted_dt >= timezone('Asia/Seoul', CURRENT_TIMESTAMP) - INTERVAL '6 hour'
                AND (
                    @p_keyword IS NULL OR @p_keyword = ''
                    OR s.title ILIKE '%' || @p_keyword || '%'
                    OR s.""content"" ILIKE '%' || @p_keyword || '%'
                )
                AND (@p_is_news_yn != 'n' OR s.site NOT IN ('NaverNews', 'GoogleNews'))
                ORDER BY (
                    COALESCE(s.likes, 0) * 10
                    + COALESCE(s.reply_num, 0) * 3
                    + (CASE WHEN s.site = '82Cook' THEN (COALESCE(s.""views"", 0) / 1000) ELSE COALESCE(s.""views"", 0) END)
                    * (CASE s.site
                        -- 조회수 높은 커뮤니티 (가중치 낮춤)
                        WHEN 'TheQoo' THEN 0.5
                        WHEN 'HumorUniv' THEN 0.5
                        WHEN '82Cook' THEN 1.0  -- 이미 /1000 되어있음
                        WHEN 'Dogdrip' THEN 2.0
                        
                        -- 중간 커뮤니티
                        WHEN 'BobeDream' THEN 3.0
                        WHEN 'StrClub' THEN 4.0
                        WHEN 'Inven' THEN 5.0
                        WHEN 'Ruliweb' THEN 5.0
                        WHEN 'Clien' THEN 5.0
                        WHEN 'TodayHumor' THEN 6.0
                        
                        -- 조회수 낮은 커뮤니티 (가중치 높임)
                        WHEN 'Damoang' THEN 50.0
                        WHEN 'MlbPark' THEN 200.0
                        
                        ELSE 3.0
                    END)
                ) DESC
                LIMIT 10
                )
                UNION ALL
                (
                SELECT
                    s.""no"",
                    s.""number"",
                    s.title,
                    s.author,
                    s.""date"",
                    s.""views"",
                    s.likes,
                    s.url,
                    s.site,
                    s.reg_date,
                    s.reply_num,
                    s.""content"",
                    s.posted_dt,
                    '9h' AS time_bucket,
                    (
                        COALESCE(s.likes, 0) * 10
                        + COALESCE(s.reply_num, 0) * 3
                        + (CASE WHEN s.site = '82Cook' THEN (COALESCE(s.""views"", 0) / 1000) ELSE COALESCE(s.""views"", 0) END)
                        * (CASE s.site
                            -- 조회수 높은 커뮤니티 (가중치 낮춤)
                            WHEN 'TheQoo' THEN 0.5
                            WHEN 'HumorUniv' THEN 0.5
                            WHEN '82Cook' THEN 1.0  -- 이미 /1000 되어있음
                            WHEN 'Dogdrip' THEN 2.0
                            
                            -- 중간 커뮤니티
                            WHEN 'BobeDream' THEN 3.0
                            WHEN 'StrClub' THEN 4.0
                            WHEN 'Inven' THEN 5.0
                            WHEN 'Ruliweb' THEN 5.0
                            WHEN 'Clien' THEN 5.0
                            WHEN 'TodayHumor' THEN 6.0
                            
                            -- 조회수 낮은 커뮤니티 (가중치 높임)
                            WHEN 'Damoang' THEN 50.0
                            WHEN 'MlbPark' THEN 200.0
                            
                            ELSE 3.0
                        END)
                    ) AS score, cloudinary_url
                FROM tmtmfhgi.site_bbs_info s
                LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id
                WHERE s.posted_dt < timezone('Asia/Seoul', CURRENT_TIMESTAMP) - INTERVAL '6 hour' AND s.posted_dt >= timezone('Asia/Seoul', CURRENT_TIMESTAMP) - INTERVAL '9 hour'
                AND (
                    @p_keyword IS NULL OR @p_keyword = ''
                    OR s.title ILIKE '%' || @p_keyword || '%'
                    OR s.""content"" ILIKE '%' || @p_keyword || '%'
                )
                AND (@p_is_news_yn != 'n' OR s.site NOT IN ('NaverNews', 'GoogleNews'))
                ORDER BY (
                    COALESCE(s.likes, 0) * 10
                    + COALESCE(s.reply_num, 0) * 3
                    + (CASE WHEN s.site = '82Cook' THEN (COALESCE(s.""views"", 0) / 1000) ELSE COALESCE(s.""views"", 0) END)
                    * (CASE s.site
                        -- 조회수 높은 커뮤니티 (가중치 낮춤)
                        WHEN 'TheQoo' THEN 0.5
                        WHEN 'HumorUniv' THEN 0.5
                        WHEN '82Cook' THEN 1.0  -- 이미 /1000 되어있음
                        WHEN 'Dogdrip' THEN 2.0
                        
                        -- 중간 커뮤니티
                        WHEN 'BobeDream' THEN 3.0
                        WHEN 'StrClub' THEN 4.0
                        WHEN 'Inven' THEN 5.0
                        WHEN 'Ruliweb' THEN 5.0
                        WHEN 'Clien' THEN 5.0
                        WHEN 'TodayHumor' THEN 6.0
                        
                        -- 조회수 낮은 커뮤니티 (가중치 높임)
                        WHEN 'Damoang' THEN 50.0
                        WHEN 'MlbPark' THEN 200.0
                        
                        ELSE 3.0
                    END)
                ) DESC
                LIMIT 10
                )
                UNION ALL
                (
                SELECT
                    s.""no"",
                    s.""number"",
                    s.title,
                    s.author,
                    s.""date"",
                    s.""views"",
                    s.likes,
                    s.url,
                    s.site,
                    s.reg_date,
                    s.reply_num,
                    s.""content"",
                    s.posted_dt,
                    '24h' AS time_bucket,
                    (
                        COALESCE(s.likes, 0) * 10
                        + COALESCE(s.reply_num, 0) * 3
                        + (CASE WHEN s.site = '82Cook' THEN (COALESCE(s.""views"", 0) / 1000) ELSE COALESCE(s.""views"", 0) END)
                        * (CASE s.site
                            -- 조회수 높은 커뮤니티 (가중치 낮춤)
                            WHEN 'TheQoo' THEN 0.5
                            WHEN 'HumorUniv' THEN 0.5
                            WHEN '82Cook' THEN 1.0  -- 이미 /1000 되어있음
                            WHEN 'Dogdrip' THEN 2.0
                            
                            -- 중간 커뮤니티
                            WHEN 'BobeDream' THEN 3.0
                            WHEN 'StrClub' THEN 4.0
                            WHEN 'Inven' THEN 5.0
                            WHEN 'Ruliweb' THEN 5.0
                            WHEN 'Clien' THEN 5.0
                            WHEN 'TodayHumor' THEN 6.0
                            
                            -- 조회수 낮은 커뮤니티 (가중치 높임)
                            WHEN 'Damoang' THEN 50.0
                            WHEN 'MlbPark' THEN 200.0
                            
                            ELSE 3.0
                        END)
                    ) AS score, cloudinary_url
                FROM tmtmfhgi.site_bbs_info s
                LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id
                WHERE s.posted_dt < timezone('Asia/Seoul', CURRENT_TIMESTAMP) - INTERVAL '9 hour' AND s.posted_dt >= timezone('Asia/Seoul', CURRENT_TIMESTAMP) - INTERVAL '24 hour'
                AND (
                    @p_keyword IS NULL OR @p_keyword = ''
                    OR s.title ILIKE '%' || @p_keyword || '%'
                    OR s.""content"" ILIKE '%' || @p_keyword || '%'
                )
                AND (@p_is_news_yn != 'n' OR s.site NOT IN ('NaverNews', 'GoogleNews'))
                ORDER BY (
                    COALESCE(s.likes, 0) * 10
                    + COALESCE(s.reply_num, 0) * 3
                    + (CASE WHEN s.site = '82Cook' THEN (COALESCE(s.""views"", 0) / 1000) ELSE COALESCE(s.""views"", 0) END)
                    * (CASE s.site
                        -- 조회수 높은 커뮤니티 (가중치 낮춤)
                        WHEN 'TheQoo' THEN 0.5
                        WHEN 'HumorUniv' THEN 0.5
                        WHEN '82Cook' THEN 1.0  -- 이미 /1000 되어있음
                        WHEN 'Dogdrip' THEN 2.0
                        
                        -- 중간 커뮤니티
                        WHEN 'BobeDream' THEN 3.0
                        WHEN 'StrClub' THEN 4.0
                        WHEN 'Inven' THEN 5.0
                        WHEN 'Ruliweb' THEN 5.0
                        WHEN 'Clien' THEN 5.0
                        WHEN 'TodayHumor' THEN 6.0
                        
                        -- 조회수 낮은 커뮤니티 (가중치 높임)
                        WHEN 'Damoang' THEN 50.0
                        WHEN 'MlbPark' THEN 200.0
                        
                        ELSE 3.0
                    END)
                ) DESC
                LIMIT 10
                )
                ;";

            #endregion

            var parameters = new
            {
                p_keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
                p_is_news_yn = isNewsYn ?? "n"
            };

            _logger.LogInformation("API Call: /api/posts-ps - Parameters: IsNewsYn={IsNewsYn}, Keyword={Keyword}",
                isNewsYn ?? "NULL",
                keyword ?? "NULL");

            var posts = await connection.QueryAsync<dynamic>(sql, parameters);
            var postsList = posts.ToList();

            // Convert dynamic objects to SiteBbsInfo objects
            var siteBbsInfos = postsList.Select(p => new SiteBbsInfo
            {
                no = (long)p.no,
                number = p.number != null ? (long?)p.number : null,
                title = p.title,
                author = p.author,
                date = p.date,
                views = p.views != null ? (int?)Convert.ToInt32(p.views) : null,
                likes = p.likes != null ? (int?)Convert.ToInt32(p.likes) : null,
                url = p.url,
                site = p.site,
                reg_date = p.reg_date,
                reply_num = p.reply_num != null ? (int?)Convert.ToInt32(p.reply_num) : null,
                content = p.content,
                posted_dt = p.posted_dt?.ToString(),
                total_count = p.total_count != null ? (int?)Convert.ToInt32(p.total_count) : null,
                score = p.score != null ? (long?)p.score : null,
                time_bucket = p.time_bucket,
                time_bucket_no = p.time_bucket_no != null ? (int?)Convert.ToInt32(p.time_bucket_no) : null,
                cloudinary_url = p.cloudinary_url
            }).ToList();

            var result = new MainPagedResult<SiteBbsInfo>
            {
                Data = siteBbsInfos,
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetPostsPs API: {Message}", ex.Message);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    [HttpGet("news")]
    public async Task<ActionResult<PagedResult<SiteBbsInfo>>> GetNews(
        int page = 1,
        int pageSize = 10,
        string? site = null,
        [FromQuery] string[]? sites = null,
        string? sortBy = "latest",
        string? keyword = null,
        string? author = null,
        string? isNewsYn = "n")
    {
        try
        {
            if (page < 1) page = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 10;

            var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ??
                                  _configuration.GetConnectionString("DefaultConnection");
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            var pageIndex = page - 1;

            var sql = @"
                WITH filtered AS (
                    SELECT
                        s.""no"",
                        s.""number"",
                        s.title,
                        s.author,
                        s.""date"",
                        s.""views"",
                        s.likes,
                        s.url,
                        s.site,
                        s.reg_date,
                        s.reply_num,
                        s.""content"",
                        s.posted_dt
                    FROM tmtmfhgi.site_bbs_info s
                    WHERE site IN ('NaverNews', 'GoogleNews')
                    AND (
                        @p_keyword IS NULL OR @p_keyword = ''
                        OR s.title ILIKE '%' || @p_keyword || '%'
                        OR s.""content"" ILIKE '%' || @p_keyword || '%'
                    )
                ),
                counted AS (
                    SELECT *, COUNT(*) OVER() as total_count FROM filtered
                )
                SELECT
                    c.posted_dt,
                    c.site,
                    c.reg_date,
                    c.reply_num,
                    c.""no"",
                    c.""number"",
                    c.title,
                    c.author,
                    c.""date"",
                    c.""views"",
                    c.likes,
                    c.url,
                    c.""content"",
                    c.posted_dt,
                    c.total_count
                FROM counted c
                ORDER BY c.posted_dt DESC
                OFFSET (@p_page_index * @p_page_count)
                LIMIT @p_page_count";

            var parameters = new
            {
                p_keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
                p_page_index = pageIndex,
                p_page_count = pageSize
            };

            _logger.LogInformation("API Call: /api/posts-ps - Parameters: Keyword={Keyword}, PageIndex={PageIndex}, PageSize={PageSize}",
                keyword ?? "NULL",
                pageIndex,
                pageSize);

            var posts = await connection.QueryAsync<dynamic>(sql, parameters);
            var postsList = posts.ToList();
            var totalCount = postsList.FirstOrDefault()?.total_count != null ? Convert.ToInt32(postsList.FirstOrDefault()?.total_count) : 0;
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            // Convert dynamic objects to SiteBbsInfo objects
            var siteBbsInfos = postsList.Select(p => new SiteBbsInfo
            {
                no = (long)p.no,
                number = p.number != null ? (long?)p.number : null,
                title = p.title,
                author = p.author,
                date = p.date,
                views = p.views != null ? (int?)Convert.ToInt32(p.views) : null,
                likes = p.likes != null ? (int?)Convert.ToInt32(p.likes) : null,
                url = p.url,
                site = p.site,
                reg_date = p.reg_date,
                reply_num = p.reply_num != null ? (int?)Convert.ToInt32(p.reply_num) : null,
                content = p.content,
                posted_dt = p.posted_dt?.ToString(),
                total_count = p.total_count != null ? (int?)Convert.ToInt32(p.total_count) : null,
                score = p.score != null ? (long?)p.score : null,
                time_bucket = p.time_bucket,
                time_bucket_no = p.time_bucket_no != null ? (int?)Convert.ToInt32(p.time_bucket_no) : null
            }).ToList();

            var result = new PagedResult<SiteBbsInfo>
            {
                Data = siteBbsInfos,
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = totalPages,
                HasNextPage = page < totalPages,
                HasPreviousPage = page > 1
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetPostsPs API: {Message}", ex.Message);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// 게시물 목록을 조회합니다. (Prepared Statement 방식)
    /// </summary>
    /// <param name="page">페이지 번호 (기본값: 1)</param>
    /// <param name="pageSize">페이지 크기 (기본값: 10, 최대: 100)</param>
    /// <param name="site">단일 사이트 필터</param>
    /// <param name="sites">다중 사이트 필터</param>
    /// <param name="sortBy">정렬 방식: "latest" (최신순), "views" (조회순), "popular" (인기순), "comments" (댓글순)</param>
    /// <param name="keyword">검색 키워드</param>
    /// <param name="author">작성자 필터</param>
    /// <returns>페이징된 게시물 목록</returns>
    [HttpGet("week")]
    public async Task<ActionResult<MainPagedResult<SiteBbsInfo>>> GetWeek(
        string? yyyy = null,
        string? mm = null,
        string? w = null)
    {
        try
        {
            int.TryParse(yyyy, out int year);
            int.TryParse(mm, out int month);
            int.TryParse(w, out int week);
            var (startDate, endDate) = year.CalculateWeekRange(month, week);

            var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ?? _configuration.GetConnectionString("DefaultConnection");
            using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            #region [ Query ]

            var sql = @"
            (
                    -- gubun 01@ 전체 사이트 통합 랭킹 (상위 20개)
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (
                                COALESCE(s.likes, 0) * 10
                                + (CASE WHEN s.site = 'SlrClub' THEN (COALESCE(s.reply_num, 0) / 1000) ELSE COALESCE(s.reply_num, 0) END) * 3
                                + (CASE WHEN s.site = '82Cook' THEN (COALESCE(s.""views"", 0) / 1000) ELSE COALESCE(s.""views"", 0) END)
                            ) AS score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site NOT IN ('NaverNews', 'GoogleNews')
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '01' gubun, cloudinary_url
                    FROM list c
                    ORDER BY c.score DESC
                    LIMIT 20
                    )
                    UNION
                    (
                    -- gubun 02@ 각 사이트별 상위 10개씩 (FMKorea)
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, 
                            cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'FMKorea'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ Humoruniv
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'Humoruniv'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ TheQoo
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'TheQoo'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ Ppomppu
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'Ppomppu'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ Clien
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'Clien'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ TodayHumor
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'TodayHumor'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ SlrClub
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'SlrClub'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ Ruliweb
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'Ruliweb'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ 82Cook
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = '82Cook'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ MlbPark
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'MlbPark'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ BobaeDream
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'BobaeDream'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ Inven
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'Inven'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                    )
                    UNION
                    (
                    -- gubun 02@ Damoang
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (COALESCE(s.likes, 0) * 10) + (COALESCE(s.reply_num, 0) * 3) + COALESCE(s.""views"", 0) score, cloudinary_url
                        FROM tmtmfhgi.site_bbs_info s
                        LEFT JOIN tmtmfhgi.optimized_images oi ON s.img1 = oi.id 
                        WHERE s.site = 'Damoang'
                            AND s.posted_dt >= @p_startDate
                            AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '02' gubun, cloudinary_url
                    FROM list c
                    ORDER BY (COALESCE(c.likes, 0) * 10) + (COALESCE(c.reply_num, 0) * 3) + COALESCE(c.""views"", 0) DESC
                    LIMIT 10
                )
                ORDER BY gubun";

            var sql1 = @"
                    WITH list AS (
                        SELECT
                            s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                            s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt,
                            (
                                COALESCE(s.likes, 0) * 10
                                + (CASE WHEN s.site = 'SlrClub' THEN (COALESCE(s.reply_num, 0) / 1000) ELSE COALESCE(s.reply_num, 0) END) * 3
                                + (CASE WHEN s.site = '82Cook' THEN (COALESCE(s.""views"", 0) / 1000) ELSE COALESCE(s.""views"", 0) END)
                            ) AS score
                        FROM tmtmfhgi.site_bbs_info s
                        WHERE s.site NOT IN ('NaverNews', 'GoogleNews')
                        AND s.posted_dt >= @p_startDate
                        AND s.posted_dt < @p_endDate
                    )
                    SELECT
                        c.score, c.posted_dt, c.site, c.reg_date, c.reply_num,
                        c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                        c.url, c.""content"", '01' gubun
                    FROM list c
                    ORDER BY c.score DESC
                    LIMIT 20
            ";

            #endregion

            var parameters = new
            {
                // p_startDate = $"{startDate:yyyy-MM-dd} 00:00:00",
                // p_endDate = $"{endDate:yyyy-MM-dd} 00:00:00"

                p_startDate = startDate,
                p_endDate = endDate
            };

            _logger.LogInformation("API Call: /api/week - Parameters: yyyy={yyyy}, mm={mm}, w={w}, StartDate={StartDate}, EndDate={EndDate}",
                yyyy,
                mm,
                w,
                startDate,
                endDate);

            var posts = await connection.QueryAsync<dynamic>(sql, parameters);
            var postsList = posts.ToList();

            // Convert dynamic objects to SiteBbsInfo objects
            var siteBbsInfos = postsList.Select(p => new SiteBbsInfo
            {
                no = (long)p.no,
                number = p.number != null ? (long?)p.number : null,
                title = p.title,
                author = p.author,
                date = p.date,
                views = p.views != null ? (int?)Convert.ToInt32(p.views) : null,
                likes = p.likes != null ? (int?)Convert.ToInt32(p.likes) : null,
                url = p.url,
                site = p.site,
                reg_date = p.reg_date,
                reply_num = p.reply_num != null ? (int?)Convert.ToInt32(p.reply_num) : null,
                content = p.content,
                posted_dt = p.posted_dt?.ToString(),
                total_count = p.total_count != null ? (int?)Convert.ToInt32(p.total_count) : null,
                score = p.score != null ? (long?)p.score : null,
                time_bucket = null,
                time_bucket_no = null,
                gubun = p.gubun,
                cloudinary_url = p.cloudinary_url,
            }).ToList();

            var result = new MainPagedResult<SiteBbsInfo>
            {
                Data = siteBbsInfos,
            };

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetPostsPs API: {Message}", ex.Message);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
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
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ??
                              _configuration.GetConnectionString("DefaultConnection");
        using var connection = new NpgsqlConnection(connectionString);
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
            // 커뮤니티 게시물 수 (NaverNews, GoogleNews 제외)
            var communityPosts = await _context.SiteBbsInfos
                .Where(x => x.site != "NaverNews" && x.site != "GoogleNews")
                .CountAsync();

            // 뉴스 게시물 수 (NaverNews, GoogleNews 포함)
            var newsPosts = await _context.SiteBbsInfos
                .Where(x => x.site == "NaverNews" || x.site == "GoogleNews")
                .CountAsync();

            // 총 게시물 수 (뉴스 포함)
            var totalPosts = communityPosts + newsPosts;

            // 커뮤니티 활성 사이트 수 (NaverNews, GoogleNews 제외)
            var communitySites = await _context.SiteBbsInfos
                .Where(x => x.site != "NaverNews" && x.site != "GoogleNews")
                .Select(x => x.site)
                .Distinct()
                .CountAsync();

            // 뉴스 활성 사이트 수 (NaverNews, GoogleNews 포함)
            var newsSites = await _context.SiteBbsInfos
                .Where(x => x.site == "NaverNews" || x.site == "GoogleNews")
                .Select(x => x.site)
                .Distinct()
                .CountAsync();

            // 총 활성 사이트 수 (뉴스 포함)
            var activeSites = communitySites + newsSites;

            // 총 방문자 수
            var totalVisitors = await _accessLogService.GetTotalVisitorsAsync();

            // 오늘 방문자 수
            var todayVisitors = await _accessLogService.GetTodayVisitorsAsync();

            // 총 접속 수 (일일 조회수로 사용)
            var totalAccess = await _accessLogService.GetTotalAccessCountAsync();

            var stats = new
            {
                totalPosts,
                communityPosts,
                newsPosts,
                activeSites,
                communitySites,
                newsSites,
                totalVisitors,
                todayVisitors,
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
                .Where(x => !string.IsNullOrEmpty(x.date) && x.site != "NaverNews" && x.site != "GoogleNews")
                .Select(x => new { x.no, x.title, x.date, x.reg_date, x.site })
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

    [HttpGet("admin/latest-crawl-time")]
    public async Task<ActionResult> GetLatestCrawlTime()
    {
        try
        {
            // 오늘 날짜 (UTC)
            var today = DateTime.UtcNow.Date;
            var tomorrow = today.AddDays(1);

            // UTC 시간으로 변환
            var todayUtc = DateTime.SpecifyKind(today, DateTimeKind.Utc);
            var tomorrowUtc = DateTime.SpecifyKind(tomorrow, DateTimeKind.Utc);

            // 오늘 크롤링된 글 중 가장 최근 reg_date 조회 (NaverNews, GoogleNews 제외)
            var latestCrawlTime = await _context.SiteBbsInfos
                .Where(x => x.reg_date.HasValue &&
                           x.reg_date.Value >= todayUtc &&
                           x.reg_date.Value < tomorrowUtc &&
                           !string.IsNullOrEmpty(x.site) &&
                           x.site != "NaverNews" && x.site != "GoogleNews")
                .OrderByDescending(x => x.reg_date)
                .Select(x => x.reg_date)
                .FirstOrDefaultAsync();

            return Ok(new { latestCrawlTime });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting latest crawl time");
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

public class MainPagedResult<T>
{
    public IEnumerable<T> Data { get; set; } = [];
}

public static class Util
{
    public static (DateTime start, DateTime end) CalculateWeekRange(this int year, int month, int week)
    {
        // 간단한 방식: 1일~7일=1주차, 8일~14일=2주차...
        var firstDay = new DateTime(year, month, 1);
        var weekStart = firstDay.AddDays((week - 1) * 7);
        var weekEnd = weekStart.AddDays(7);

        return (weekStart, weekEnd);
    }

    public static DateTime GetFirstMondayOfMonth(DateTime date)
    {
        var firstOfMonth = new DateTime(date.Year, date.Month, 1);
        var dayOfWeek = (int)firstOfMonth.DayOfWeek;

        // 일요일이 0이므로, 월요일을 찾기 위해 계산
        var daysToAdd = dayOfWeek == 0 ? 1 : (8 - dayOfWeek);

        return firstOfMonth.AddDays(daysToAdd);
    }

    public static int GetFirstMondayDay(int year, int month)
    {
        // 해당 달의 1일
        DateTime firstDay = new DateTime(year, month, 1);

        // 첫 월요일까지 며칠 더해야 하는지 계산
        int offset = ((int)DayOfWeek.Monday - (int)firstDay.DayOfWeek + 7) % 7;

        // 날짜 반환 (일(day)만)
        return firstDay.AddDays(offset).Day;
    }

    public static DateTime GetFirstMonday(int year, int month)
    {
        // 해당 달의 1일
        DateTime firstDay = new DateTime(year, month, 1);

        // 요일 차이 계산 (월요일이 될 때까지 며칠을 더해야 하는지)
        int offset = ((int)DayOfWeek.Monday - (int)firstDay.DayOfWeek + 7) % 7;

        return firstDay.AddDays(offset);
    }
}