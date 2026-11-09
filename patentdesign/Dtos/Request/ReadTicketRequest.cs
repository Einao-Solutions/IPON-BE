using System.ComponentModel.DataAnnotations;

namespace patentdesign.Dtos.Request;

public record ReadTicketRequest
{
    [Required]
    [Range(typeof(long), "0", "9223372036854775807")]
    public long? ThroughMessageSequence { get; set; }
}
