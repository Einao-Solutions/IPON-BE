using patentdesign.Models;

namespace patentdesign.Dtos.Response
{
    public class PublicationDto
    {
        public string? FileNumber { get; set; }
        public string? Comment { get; set; }
        public string? StaffId { get; set; }
        public string? StaffName { get; set; }
        public List<Opposition>? Opposition { get; set; }
        public DateTime? PublicationDate { get; set; }
        public bool? IsManualPublication { get; set; } = false;
        public string? BatchVolume { get; set; }
    }
    public class PublicationInfoDto
    {
        public string? FileNumber { get; set; }
        public string? FileId { get; set; }
        public string? Title { get; set; }
        public int? Class { get; set; }
        public string? Representation { get; set; }
        public string? Applicant { get; set; }
        public DateTime PublicationDate { get; set; }
        public DateTime FilingDate { get; set; }
    }
    public class PaginatedPublicationResponse
    {
        public List<PublicationInfoDto> Result { get; set; } = [];
        public long Count { get; set; }
    }
    public class TreatBatchDto
    {
        public string FileNumber { get; set; }
        public string? Comment { get; set; }
        public string StaffId { get; set; }
        public string ApplicationId { get; set; }
        public bool IsApproved { get; set; } = false;
    }

    public class StaffBatchRequest
    {
        public string UserId { get; set; }
        public int Volume { get; set; }
        public int Number { get; set; }
        public DateTime ReleaseDate {get; set;}
    }
}
