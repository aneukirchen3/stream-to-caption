using System;

namespace PodtextCaption.Web.Models;

public class QueueStatus
{
    public int Id { get; set; } = 1;
    public string DsStatus { get; set; } = "Inativo";
    public DateTime DtLastUpdateStatus { get; set; } = DateTime.UtcNow;
}
