using System.ComponentModel.DataAnnotations;

namespace EnderunAI.Api.Contracts;

public sealed class SubmitAccessRequestRequest
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}
