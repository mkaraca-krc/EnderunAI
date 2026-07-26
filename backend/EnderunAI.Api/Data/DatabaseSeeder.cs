using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        PasswordService passwordService,
        IConfiguration configuration)
    {
        foreach (var preset in PermissionCatalog.RolePresets)
        {
            var roleExists = await db.Roles.AnyAsync(role => role.Name == preset.Name);
            if (!roleExists)
            {
                db.Roles.Add(new AppRole
                {
                    Name = preset.Name,
                    Description = preset.Description
                });
            }
        }

        await db.SaveChangesAsync();
        var adminRole = await db.Roles.SingleAsync(role => role.Name == "Admin");

        var username = Environment.GetEnvironmentVariable("SEED_ADMIN_USERNAME");
        var password = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");
        var fullName =
            Environment.GetEnvironmentVariable("SEED_ADMIN_FULLNAME") ??
            "Mehmet Karacabey";

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        username = username.Trim().ToLowerInvariant();
        var user = await db.Users
            .Include(item => item.UserRoles)
            .SingleOrDefaultAsync(item => item.Username == username);

        if (user is null)
        {
            var result = passwordService.Hash(password);
            user = new AppUser
            {
                Username = username,
                FullName = fullName,
                PasswordHash = result.Hash,
                PasswordSalt = result.Salt,
                IsActive = true
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        if (!user.UserRoles.Any(userRole => userRole.RoleId == adminRole.Id))
        {
            db.UserRoles.Add(new UserRole
            {
                UserId = user.Id,
                RoleId = adminRole.Id
            });
            await db.SaveChangesAsync();
        }
    }
}
