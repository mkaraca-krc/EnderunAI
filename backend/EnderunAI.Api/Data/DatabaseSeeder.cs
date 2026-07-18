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
        var adminRole = await db.Roles.SingleOrDefaultAsync(x => x.Name == "Admin");
        if (adminRole is null)
        {
            adminRole = new AppRole { Name = "Admin", Description = "Tam sistem yetkisi" };
            db.Roles.Add(adminRole);
            await db.SaveChangesAsync();
        }

        var username = Environment.GetEnvironmentVariable("SEED_ADMIN_USERNAME");
        var password = Environment.GetEnvironmentVariable("SEED_ADMIN_PASSWORD");
        var fullName = Environment.GetEnvironmentVariable("SEED_ADMIN_FULLNAME") ?? "Mehmet Karacabey";

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return;

        var user = await db.Users.Include(x => x.UserRoles)
            .SingleOrDefaultAsync(x => x.Username == username);

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

        if (!user.UserRoles.Any(x => x.RoleId == adminRole.Id))
        {
            db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = adminRole.Id });
            await db.SaveChangesAsync();
        }
    }
}
