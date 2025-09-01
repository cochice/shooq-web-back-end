using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Marvin.Tmtmfh91.Web.BackEnd.Models;

[Table("site_bbs_info", Schema = "tmtmfhgi")]
public class SiteBbsInfo
{
    [Key]
    [Column("no")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public long No { get; set; }

    [Column("number")]
    public long? Number { get; set; }

    [Column("title")]
    public string? Title { get; set; }

    [Column("author")]
    public string? Author { get; set; }

    [Column("date")]
    public string? Date { get; set; }

    [Column("views")]
    public int? Views { get; set; }

    [Column("likes")]
    public int? Likes { get; set; }

    [Column("url")]
    public string? Url { get; set; }

    [Column("site")]
    public string? Site { get; set; }

    [Column("reg_date")]
    public DateTime? RegDate { get; set; }

    [Column("reply_num")]
    public int? ReplyNum { get; set; }

    [Column("content")]
    public string? Content { get; set; }
}