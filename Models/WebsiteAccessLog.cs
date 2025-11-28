using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Net;

namespace Marvin.Tmtmfh91.Web.Backend.Models;

[Table("website_access_log", Schema = "tmtmfhgi")]
public class WebsiteAccessLog
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("ip_address")]
    [Required]
    public IPAddress IpAddress { get; set; } = null!;

    [Column("access_count")]
    public int AccessCount { get; set; } = 1;

    [Column("first_access_time")]
    public DateTime FirstAccessTime { get; set; } = DateTime.UtcNow;

    [Column("last_access_time")]
    public DateTime LastAccessTime { get; set; } = DateTime.UtcNow;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}