namespace PayFlow.Domain.Constants;

public static class RoleConstants
{
    public const string Customer = "Customer";
    public const string ComplianceOfficer = "ComplianceOfficer";
    public const string SystemAdmin = "SystemAdmin";
    public const string Auditor = "ComplianceOfficer";
    public const string Admin = "SystemAdmin";

    public const string RequireAdminRole = "RequireAdminRole";
    public const string RequireAuditorRole = "RequireAuditorRole";
    public const string RequireElevatedRole = "RequireElevatedRole";
}
