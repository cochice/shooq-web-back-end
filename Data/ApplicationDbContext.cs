using Microsoft.EntityFrameworkCore;
using Marvin.Tmtmfh91.Web.BackEnd.Models;
using Marvin.Tmtmfh91.Web.Backend.Models;

namespace Marvin.Tmtmfh91.Web.BackEnd.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<SiteBbsInfo> SiteBbsInfos { get; set; }
    public DbSet<WebsiteAccessLog> WebsiteAccessLogs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SiteBbsInfo>(entity =>
        {
            entity.HasKey(e => e.no);
            entity.Property(e => e.no).ValueGeneratedOnAdd();
            entity.Property(e => e.reg_date)
                .HasDefaultValueSql("(now() AT TIME ZONE 'Asia/Seoul')");

            // 기본적인 검색 인덱스
            entity.HasIndex(e => e.site);
            entity.HasIndex(e => e.reg_date);
        });

        modelBuilder.Entity<WebsiteAccessLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedOnAdd();
            entity.Property(e => e.IpAddress).IsRequired();
            entity.Property(e => e.AccessCount).HasDefaultValue(1);
            entity.Property(e => e.FirstAccessTime)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.LastAccessTime)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");

            // 필수 인덱스만 유지
            entity.HasIndex(e => e.IpAddress);
        });

        base.OnModelCreating(modelBuilder);
    }
}