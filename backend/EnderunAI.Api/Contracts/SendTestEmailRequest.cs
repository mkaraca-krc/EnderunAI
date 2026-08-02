using System.ComponentModel.DataAnnotations;

namespace EnderunAI.Api.Contracts;

public sealed class SendTestEmailRequest
{
    [Required]
    public string ToEmail { get; set; } = string.Empty;
}
