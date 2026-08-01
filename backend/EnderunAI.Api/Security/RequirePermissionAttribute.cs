namespace EnderunAI.Api.Security;

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = true,
    Inherited = true)]
public sealed class RequirePermissionAttribute : Attribute
{
    public RequirePermissionAttribute(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            throw new ArgumentException(
                "Yetki anahtarı boş olamaz.",
                nameof(permission));
        }

        Permission = permission;
    }

    public string Permission { get; }
}
