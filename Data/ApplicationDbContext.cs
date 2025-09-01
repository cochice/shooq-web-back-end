using Microsoft.EntityFrameworkCore;
using Marvin.Tmtmfh91.Web.BackEnd.Models;

namespace Marvin.Tmtmfh91.Web.BackEnd.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<SiteBbsInfo> SiteBbsInfos { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SiteBbsInfo>(entity =>
        {
            entity.HasKey(e => e.No);
            entity.Property(e => e.No).ValueGeneratedOnAdd();
            entity.Property(e => e.RegDate)
                .HasDefaultValueSql("(now() AT TIME ZONE 'Asia/Seoul')");
            
            // LIKE 검색 성능 향상을 위한 인덱스
            entity.HasIndex(e => e.Title)
                .HasDatabaseName("ix_site_bbs_info_title");
            
            entity.HasIndex(e => e.Content)
                .HasDatabaseName("ix_site_bbs_info_content");
            
            // 복합 검색을 위한 인덱스 (title, content 동시 검색)
            entity.HasIndex(e => new { e.Title, e.Content })
                .HasDatabaseName("ix_site_bbs_info_title_content");
            
            // 사이트별 검색을 위한 복합 인덱스
            entity.HasIndex(e => new { e.Site, e.Title })
                .HasDatabaseName("ix_site_bbs_info_site_title");
            
            entity.HasIndex(e => new { e.Site, e.Content })
                .HasDatabaseName("ix_site_bbs_info_site_content");
        });

        base.OnModelCreating(modelBuilder);
    }
}