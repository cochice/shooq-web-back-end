using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Marvin.Tmtmfh91.Web.BackEnd.Models;

[Table("site_bbs_info", Schema = "tmtmfhgi")]
public class SiteBbsInfo
{
    [Key]
    [Column("no")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long no { get; set; }

    [Column("number")]
    public long? number { get; set; }

    [Column("title")]
    public string? title { get; set; }

    [Column("author")]
    public string? author { get; set; }

    [Column("date")]
    public string? date { get; set; }

    [Column("views")]
    public int? views { get; set; }

    [Column("likes")]
    public int? likes { get; set; }

    [Column("url")]
    public string? url { get; set; }

    [Column("site")]
    public string? site { get; set; }

    [Column("reg_date")]
    public DateTime? reg_date { get; set; }

    [Column("reply_num")]
    public int? reply_num { get; set; }

    [Column("content")]
    public string? content { get; set; }

    [Column("posted_dt")]
    public string? posted_dt { get; set; }

    [Column("total_count")]
    public int? total_count { get; set; }

    [Column("score")]
    public long? score { get; set; }

    [Column("time_bucket")]
    public string? time_bucket { get; set; }

    [Column("time_bucket_no")]
    public int? time_bucket_no { get; set; }

    [Column("gubun")]
    public string? gubun { get; set; }

    [Column("cloudinary_url")]
    public string? cloudinary_url { get; set; }

    [Column("img_srcs")]
    public List<OptimizedImages>? OptimizedImagesList { get; set; }
}

[Table("optimized_images", Schema = "tmtmfhgi")]
public class OptimizedImages
{
    [Column("id")]
    public int id { get; set; }

    [Column("cloudinary_url")]
    public string? cloudinary_url { get; set; }

    [Column("no")]
    public long? no { get; set; }

    [Column("media_type")]
    public string? media_type { get; set; }  // 'image' or 'video'
}