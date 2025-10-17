using patentdesign.Models;

namespace patentdesign.Dtos.Response;

public class FileApplicationsDto
{
    public string FileTitle { get; set; } = string.Empty;

    public List<ApplicationInfo> Applications { get; set; } = [];
}