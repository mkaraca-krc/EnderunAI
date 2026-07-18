using EnderunAI.Api.Contracts;
using EnderunAI.Api.Data;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    AppDbContext db,
    PasswordService passwordService,
    TokenService tokenService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .Include(x => x.UserRoles)
            .ThenInclude(x => x.Role)
            .SingleOrDefaultAsync(x => x.Username == request.Username.Trim(), cancellationToken);

        if (user is null || !user.IsActive ||
            !passwordService.Verify(request.Password, user.PasswordHash, user.PasswordSalt))
        {
            return Unauthorized(new { message = "Kullanıcı adı veya şifre hatalı." });
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        var roles = user.UserRoles.Select(x => x.Role.Name).ToArray();
        return Ok(new
        {
            token = tokenService.Create(user, roles),
            expiresInSeconds = 43200,
            user = new { user.Id, user.Username, user.FullName, user.Email, roles }
        });
    }

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me() => Ok(new { username = User.Identity?.Name });
}
