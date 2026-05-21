namespace patentdesign.Dtos.Request;

public class OppositionRequestDto
{
    public string? UserId { get; set; }
    public string? FileNumber { get; set; }
    public string? FileId { get; set; }
    public string? FileTitle { get; set; }
    public string? Name { get; set; }
    public string? Phone {get; set;}
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Nationality { get; set; }
    public string? Reason { get; set; }
    public List<IFormFile>? SupportingDocs { get; set; }
    public DateTime? OppositionDate { get; set; }
    public string? PaymentId { get; set; }
    public bool? StaffOpposition { get; set; } = false;
    public string? StaffId { get; set; }
}

public class OppositionSearchDto
{
    public string? FileNumber { get; set; }
    public string? FileId { get; set; }
    public string? FileTitle { get; set; }
    public string? Cost { get; set; }
    public string? PaymentId { get; set; }
    public string? ServiceFee { get; set; }
    public string? RepresentationUrl { get; set; }
    public string? ApplicantName { get; set; }
    public int? Class { get; set; }
}

public class OppositionStatsDto
{
    public long? AwaitingCounter { get; set; }
    public long? NewOpposition { get; set; }
    public long? AwaitingOfficeProcess { get; set; }
    public long? Abandoned { get; set; }
}

public class CounterStatementRequestDto
{
    public string? FileNumber { get; set; }
    public string? FileId { get; set; }
    public string? FileTitle { get; set; }
    public string? OppositionId { get; set; }
    public string? CounterStatement { get; set; }
    public List<IFormFile>? SupportingDocs { get; set; }
    public string? UserId { get; set; }
}

public class OppositionWithdrawalRequestDto
{
    public string? FileNumber { get; set; }
    public string? FileId { get; set; }
    public string? OppositionId { get; set; }
    public string? FileTitle { get; set; }
    public string? Reason { get; set; }
    public string? UserId { get; set; }
    public List<IFormFile>? SupportingDocs { get; set; }
}

public class StatutoryDeclarationRequestDto
{
    public string? FileNumber { get; set; }
    public string? FileId { get; set; }
    public string? OppositionId { get; set; }
    public string? FileTitle { get; set; }
    public string? Comment { get; set; }
    public string? UserId { get; set; }
    public string? Role { get; set; } // "opposer" or "applicant"
    public List<IFormFile>? SupportingDocs { get; set; }
}

public class CsSearchDto
{
    public bool Success { get; set; }
    public string? FileNumber { get; set; }
    public string? FileName { get; set; }
    public string? FileOwner { get; set; }
    public int? TrademarkClass { get; set; }
    public string? RepresentationUrl { get; set; }
    public string? OppositionId { get; set; }
    public string? PaymentId { get; set; }
    public string? Cost { get; set; }
    public string? ServiceFee { get; set; }
    public string? Message { get; set; }
}

public class PaymentUpdateDto
{
    public string? Status { get; set; }
    public string? TransactionRef { get; set; }
    public decimal? Amount { get; set; }
}

public class ResolveOppositionDto
{
    public string? ApplicationId { get; set; }
    public string? Statement { get; set; }
    public string? Decision { get; set; }
    public int? NewStatus { get; set; }
    public int? CurrentStatus { get; set; }
    public string? Reason { get; set; }
    public string? UserName { get; set; }
    public string? UserId { get; set; }
}

public class GenerateOppositionPaymentDto
{
    public string? Description { get; set; }
    public string? Type { get; set; } // "opposition", "response", "resolution", "statutorydeclaration"
    public string? OppositionID { get; set; }
    public string? Name { get; set; }
    public string? Email { get; set; }
    public string? Number { get; set; }
}

public class AmendmentCost
{
    public string? Amount { get; set; }
    public string? PaymentId { get; set; }
    public string? UserId { get; set; }
    public string? FileId { get; set; }
    public string? FileTitle { get; set; }
     public string? ApplicantName { get; set; }
}
public class OppositionAmendmentDto 
{     
    public string? FileNumber { get; set; }
    public string? PaymentId { get; set; }
    public string? NewAdditionalDescription { get; set; }
    public string? NewDisclaimer { get; set; }
    public IFormFile? NewRepresentation { get; set; }
    public string? AmendmentDetails { get; set; }
    public List<IFormFile>? SupportingDocs { get; set; }
    public string? UserId { get; set; }
}

