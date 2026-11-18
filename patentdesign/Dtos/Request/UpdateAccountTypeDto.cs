using System.ComponentModel.DataAnnotations;
using patentdesign.Enums;

namespace patentdesign.Dtos.Request;

public class UpdateAccountTypeDto
{
    [Required]
    [EnumDataType(typeof(AccountType))]
    public AccountType? AccountType { get; set; }
}
