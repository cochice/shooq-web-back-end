using Microsoft.EntityFrameworkCore;
using Marvin.Tmtmfh91.Web.BackEnd.Data;
using Marvin.Tmtmfh91.Web.BackEnd.Models;
using Dapper;
using Npgsql;

namespace Marvin.Tmtmfh91.Web.Backend.Services;

public class ShooqService
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ShooqService> _logger;

    /// <summary>
    /// 조정된 스코어 하드코딩 문자열
    /// </summary>
    private static readonly string GetScaledScore2 = @"
        COALESCE(s.views, 0)
        --+ (CASE s.site WHEN 'HumorUniv' THEN COALESCE(s.likes, 0) ELSE COALESCE(s.likes, 0) * 10 END)
        --+ COALESCE(s.reply_num, 0) * 3
        * CASE s.site
            WHEN 'Ruliweb' THEN 4
            WHEN 'Ppomppu' THEN 3
            WHEN 'BobaeDream' THEN 9
            WHEN 'TheQoo' THEN 3
            WHEN 'HumorUniv' THEN 1
            WHEN 'SlrClub' THEN 20
            WHEN 'Clien' THEN 30
            WHEN 'Inven' THEN 30
            WHEN 'Damoang' THEN 32
            WHEN '82Cook' THEN 30
            WHEN 'TodayHumor' THEN 50
            WHEN 'MlbPark' THEN 5
            WHEN 'FMKorea' THEN 1
            ELSE 1
        END
        ";

    public ShooqService(ApplicationDbContext context, IConfiguration configuration, ILogger<ShooqService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// 게시물 목록을 조회합니다.
    /// </summary>
    public async Task<(List<SiteBbsInfo> posts, int totalCount)> GetPostsAsync(
        int page = 1,
        int pageSize = 10,
        string? site = null,
        string? keyword = null,
        string? author = null,
        long? maxNo = null,
        string? sortBy = "hot",
        string? topPeriod = "today",
        bool onlyWithMedia = false)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ?? _configuration.GetConnectionString("DefaultConnection");
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var pageIndex = page - 1;

        // sortBy 정규화
        var normalizedSortBy = sortBy?.ToLower() ?? "hot";

        // 추천순일 때만 기간 필터링 적용
        var timeInterval = normalizedSortBy == "top" ? (topPeriod?.ToLower() switch
        {
            "today" => "INTERVAL '24 hours'",
            "week" => "INTERVAL '7 days'",
            "month" => "INTERVAL '30 days'",
            "all" => "INTERVAL '365 days'", // 전체는 1년으로 제한
            _ => "INTERVAL '24 hours'"
        }) : "INTERVAL '48 hours'";

        // 급상승 일때 적용
        if (normalizedSortBy == "rising")
        {
            timeInterval = "INTERVAL '3 hours'";
        }

        // 최신순 일때 적용
        if (normalizedSortBy == "new")
        {
            timeInterval = "INTERVAL '730 days'";
        }

        // ORDER BY 절 결정
        var orderByClause = normalizedSortBy switch
        {
            "new" => "c.posted_dt DESC",
            "top" => "c.score DESC",
            "rising" => "c.rising_score DESC, c.posted_dt DESC",
            _ => "c.time_bucket_no ASC, c.score DESC" // hot (default)
        };

        var sql = $@"
            WITH current_seoul_time AS (
                SELECT timezone('Asia/Seoul', CURRENT_TIMESTAMP) as now_time
            ),
            filtered AS (
                SELECT
                    s.""no"", s.""number"", s.title, s.author, s.""date"", s.""views"",
                    s.likes, s.url, s.site, s.reg_date, s.reply_num, s.""content"", s.posted_dt, s.img2,
                    (
                        {GetScaledScore2}
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
                WHERE s.posted_dt >= cst.now_time - {timeInterval}
                AND (@p_site IS NULL OR @p_site = '' OR s.site = @p_site)
                AND s.site NOT IN ('NaverNews', 'GoogleNews')
                AND (@p_max_no IS NULL OR s.""no"" <= @p_max_no)
                AND (@p_only_with_media = false OR s.img2 > 0)
                AND (
                        @p_keyword IS NULL OR @p_keyword = ''
                        OR s.title ILIKE '%' || @p_keyword || '%'
                        --OR s.""content"" ILIKE '%' || @p_keyword || '%'
                )
            ),
            counted AS (
                SELECT *,
                    COUNT(*) OVER() as total_count,
                    CASE WHEN time_bucket_no <= 3 THEN score ELSE 0 END AS rising_score
                FROM filtered
            )
            SELECT DISTINCT
                c.score, c.time_bucket_no, c.posted_dt, c.site, c.reg_date, c.reply_num,
                c.""no"", c.""number"", c.title, c.author, c.""date"", c.""views"", c.likes,
                c.url, c.""content"", c.total_count, c.cloudinary_url, c.rising_score, c.img2
            FROM counted c
            ORDER BY {orderByClause}
            OFFSET (@p_page_index * @p_page_count)
            LIMIT @p_page_count
        ";

        var parameters = new
        {
            p_site = site,
            p_keyword = string.IsNullOrWhiteSpace(keyword) ? null : keyword,
            p_page_index = pageIndex,
            p_page_count = pageSize,
            p_max_no = maxNo,
            p_only_with_media = onlyWithMedia
        };

        _logger.LogInformation("ShooqService.GetPostsAsync - Parameters: Site={site}, Keyword={Keyword}, PageIndex={PageIndex}, PageSize={PageSize}, MaxNo={MaxNo}, SortBy={SortBy}, TopPeriod={TopPeriod}, OnlyWithMedia={OnlyWithMedia}, TimeInterval={TimeInterval}, OrderBy={OrderBy}",
            site,
            keyword ?? "NULL",
            pageIndex,
            pageSize,
            maxNo?.ToString() ?? "NULL",
            normalizedSortBy,
            topPeriod ?? "today",
            onlyWithMedia,
            timeInterval,
            orderByClause);

        var posts = await connection.QueryAsync<dynamic>(sql, parameters);
        var postsList = posts.ToList();
        var totalCount = postsList.FirstOrDefault()?.total_count != null ? Convert.ToInt32(postsList.FirstOrDefault()?.total_count) : 0;

        // Fetch all optimized images in parallel
        var imagesTasks = postsList.Select(p => GetOptimizedImagesAsync((int)p.no)).ToList();
        var imagesResults = await Task.WhenAll(imagesTasks);

        // Convert dynamic objects to SiteBbsInfo objects
        var siteBbsInfos = postsList.Select((p, index) => new SiteBbsInfo
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
            cloudinary_url = p.cloudinary_url,
            OptimizedImagesList = imagesResults[index],
            img2 = p.img2 != null ? (int?)Convert.ToInt32(p.img2) : null
        }).ToList();

        return (siteBbsInfos, totalCount);
    }


    public async Task<List<OptimizedImages>> GetOptimizedImagesAsync(int no)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ?? _configuration.GetConnectionString("DefaultConnection");
        using var db = new NpgsqlConnection(connectionString);

        var sql = @"
            SELECT
                id,
                cloudinary_url,
                NO,
                CASE
                    WHEN cloudinary_url IS NOT NULL THEN
                        CASE
                            WHEN cloudinary_url ~* '\.(jpg|jpeg|png|gif|webp|svg|bmp)$' THEN 'image'
                            WHEN cloudinary_url ~* '\.(mp4|avi|mov|wmv|flv|webm|mkv)$' THEN 'video'
                            WHEN cloudinary_url ~* '(youtube\.com|youtu\.be)' THEN 'video'
                            ELSE NULL
                        END
                    ELSE NULL
                END AS media_type
            FROM tmtmfhgi.optimized_images oi
            WHERE NO = @no";

        var list = (await db.QueryAsync<OptimizedImages>(sql, new { no })).AsList();

        return list;
    }

    /// <summary>
    /// 트렌딩 커뮤니티를 조회합니다 (레딧 스타일)
    /// 최근 24시간 동안 이미지가 있고 좋아요가 20개 이상인 게시물을 기준으로
    /// 각 사이트의 베스트 포스트를 조회합니다.
    /// </summary>
    public async Task<List<TrendingCommunity>> GetTrendingCommunitiesAsync(int limit = 6)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ?? _configuration.GetConnectionString("DefaultConnection");
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
            WITH filtered AS (
                SELECT *
                FROM tmtmfhgi.site_bbs_info
                WHERE posted_dt >= NOW() - INTERVAL '24 hours'
                  AND likes >= 20
                  AND (img2 > 0 OR site = 'YouTube')
            ),
            site_stats AS (
                SELECT
                    site,
                    SUM(likes)        AS total_likes,
                    SUM(reply_num)    AS total_replies
                FROM filtered
                GROUP BY site
            ),
            site_best_post AS (
                SELECT DISTINCT ON (site)
                    site,
                    ""no"",
                    title,
                    likes,
                    reply_num,
                    posted_dt,
                    url,
                    author,
                    ""date"",
                    ""views"",
                    ""content""
                FROM filtered
                ORDER BY site, likes DESC, reply_num DESC, posted_dt DESC
            )
            SELECT
                sbp.site,
                sbp.""no"" AS best_post_no,
                sbp.title AS best_post_title,
                sbp.likes AS best_post_likes,
                sbp.reply_num AS best_post_replies,
                sbp.posted_dt AS best_post_date,
                sbp.url AS best_post_url,
                sbp.author AS best_post_author,
                sbp.""date"" AS best_post_original_date,
                sbp.""views"" AS best_post_views,
                sbp.""content"" AS best_post_content,
                ss.total_likes,
                ss.total_replies
            FROM site_stats ss
            JOIN site_best_post sbp USING (site)
            ORDER BY
                ss.total_likes DESC,
                ss.total_replies DESC
            LIMIT @p_limit
        ";

        var parameters = new { p_limit = limit };

        _logger.LogInformation("ShooqService.GetTrendingCommunitiesAsync - Parameters: Limit={Limit}", limit);

        var communities = await connection.QueryAsync<dynamic>(sql, parameters);
        var communitiesList = communities.ToList();

        // Fetch optimized images for each best post
        var imagesTasks = communitiesList.Select(c => GetOptimizedImagesAsync((int)c.best_post_no)).ToList();
        var imagesResults = await Task.WhenAll(imagesTasks);

        // Convert dynamic objects to TrendingCommunity objects
        var result = communitiesList.Select((c, index) => new TrendingCommunity
        {
            site = c.site,
            best_post_no = (long)c.best_post_no,
            best_post_title = c.best_post_title,
            best_post_likes = c.best_post_likes != null ? (int?)Convert.ToInt32(c.best_post_likes) : null,
            best_post_replies = c.best_post_replies != null ? (int?)Convert.ToInt32(c.best_post_replies) : null,
            best_post_date = c.best_post_date != null ? (DateTime?)c.best_post_date : null,
            best_post_url = c.best_post_url,
            best_post_author = c.best_post_author,
            best_post_original_date = c.best_post_original_date,
            best_post_views = c.best_post_views != null ? (int?)Convert.ToInt32(c.best_post_views) : null,
            best_post_content = c.best_post_content,
            total_likes = c.total_likes != null ? (long?)c.total_likes : null,
            total_replies = c.total_replies != null ? (long?)c.total_replies : null,
            optimizedImagesList = imagesResults[index]
        }).ToList();

        return result;
    }

    /// <summary>
    /// 게시물 및 관련 이미지를 삭제합니다 (트랜잭션 처리)
    /// </summary>
    public async Task<bool> DeletePostAsync(long no)
    {
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL") ?? _configuration.GetConnectionString("DefaultConnection");
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        using var transaction = await connection.BeginTransactionAsync();
        try
        {
            // 1. optimized_images 테이블에서 삭제
            var deleteImagesSql = @"DELETE FROM tmtmfhgi.optimized_images WHERE NO = @p_no";
            await connection.ExecuteAsync(deleteImagesSql, new { p_no = no }, transaction);

            // 2. site_bbs_info 테이블에서 삭제
            var deletePostSql = @"DELETE FROM tmtmfhgi.site_bbs_info WHERE ""no"" = @p_no";
            var affectedRows = await connection.ExecuteAsync(deletePostSql, new { p_no = no }, transaction);

            // 커밋
            await transaction.CommitAsync();

            _logger.LogInformation("ShooqService.DeletePostAsync - Successfully deleted post with no={No}, AffectedRows={AffectedRows}", no, affectedRows);

            return affectedRows > 0;
        }
        catch (Exception ex)
        {
            // 롤백
            await transaction.RollbackAsync();
            _logger.LogError(ex, "ShooqService.DeletePostAsync - Error deleting post with no={No}", no);
            throw;
        }
    }
}

/// <summary>
/// 트렌딩 커뮤니티 정보
/// </summary>
public class TrendingCommunity
{
    public string site { get; set; } = string.Empty;
    public long best_post_no { get; set; }
    public string? best_post_title { get; set; }
    public int? best_post_likes { get; set; }
    public int? best_post_replies { get; set; }
    public DateTime? best_post_date { get; set; }
    public string? best_post_url { get; set; }
    public string? best_post_author { get; set; }
    public string? best_post_original_date { get; set; }
    public int? best_post_views { get; set; }
    public string? best_post_content { get; set; }
    public long? total_likes { get; set; }
    public long? total_replies { get; set; }
    public List<OptimizedImages>? optimizedImagesList { get; set; }
}