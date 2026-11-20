using patentdesign.Enums;

namespace patentdesign.Dtos.Response;

public class UserAccountTypeDto
{
    public string UserId { get; set; } = "";
    public AccountType? AccountType { get; set; }
    public string? AccountTypeName => AccountType?.ToString();
}

public class AccountTypeUpdateResultDto : UserAccountTypeDto
{
    public bool Changed { get; set; }
}
