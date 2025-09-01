using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Marvin.Tmtmfh91.Web.BackEnd.Data;
using Marvin.Tmtmfh91.Web.BackEnd.Models;

namespace Marvin.Tmtmfh91.Web.BackEnd.Controllers;

[ApiController]
[Route("api")]
public class ShoooqController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<ShoooqController> _logger;

    public ShoooqController(ApplicationDbContext context, ILogger<ShoooqController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpGet("test")]
    public ActionResult<string> Test()
    {
        return Ok("Shoooq Controller is working!");
    }

    [HttpGet("posts")]
    public async Task<ActionResult<PagedResult<SiteBbsInfo>>> GetPosts(
        int page = 1, 
        int pageSize = 10, 
        string? site = null,
        [FromQuery] string[]? sites = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var query = _context.SiteBbsInfos.AsQueryable();

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

        var posts = await query
            .OrderByDescending(x => x.Date)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

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

    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<SiteBbsInfo>>> SearchPosts(
        string? keyword = null,
        string? site = null,
        string? author = null,
        int page = 1,
        int pageSize = 10,
        [FromQuery] string[]? sites = null)
    {
        if (page < 1) page = 1;
        if (pageSize < 1 || pageSize > 100) pageSize = 10;

        var query = _context.SiteBbsInfos.AsQueryable();

        if (!string.IsNullOrEmpty(keyword))
        {
            query = query.Where(x =>
                (x.Title != null && x.Title.Contains(keyword)) ||
                (x.Content != null && x.Content.Contains(keyword)));
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

        if (!string.IsNullOrEmpty(author))
        {
            query = query.Where(x => x.Author == author);
        }

        var totalCount = await query.CountAsync();
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var posts = await query
            .OrderByDescending(x => x.RegDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

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