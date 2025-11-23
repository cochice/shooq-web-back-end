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

    public ShooqService(ApplicationDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
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
                            ELSE NULL
                        END
                    ELSE NULL
                END AS media_type
            FROM tmtmfhgi.optimized_images oi
            WHERE NO = @no";

        var list = (await db.QueryAsync<OptimizedImages>(sql, new { no })).AsList();

        return list;
    }
}