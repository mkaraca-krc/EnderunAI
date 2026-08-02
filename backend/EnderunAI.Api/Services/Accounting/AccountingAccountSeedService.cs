using System.Text.Json;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Accounting;

public sealed class AccountingAccountSeedService(
    AppDbContext dbContext,
    IWebHostEnvironment environment,
    ILogger<AccountingAccountSeedService> logger)
    : IAccountingAccountSeedService
{
    private sealed record SeedAccount(
        string Code,
        string Name,
        string? ParentCode,
        string Nature,
        int Level,
        bool IsPostingAllowed,
        bool RequiresProject,
        bool RequiresCostCenter,
        string? CurrencyCode,
        bool IsActive);

    public async Task<AccountingAccountSeedResult> SeedAsync(
        Guid companyId,
        CancellationToken cancellationToken)
    {
        var companyExists = await dbContext.Companies
            .AnyAsync(x => x.Id == companyId, cancellationToken);

        if (!companyExists)
            throw new KeyNotFoundException("Şirket bulunamadı.");

        var accounts = await LoadAccountsAsync(cancellationToken);

        if (accounts.Count == 0)
            throw new InvalidOperationException(
                "Enderun hesap planı verisi boş.");

        var duplicateCodes = accounts
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() > 1)
            .Select(x => x.Key)
            .ToList();

        if (duplicateCodes.Count > 0)
            throw new InvalidOperationException(
                "Seed dosyasında mükerrer hesap kodları var: " +
                string.Join(", ", duplicateCodes.Take(20)));

        var existingAccounts = await dbContext.AccountingAccounts
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);

        var accountByCode = existingAccounts.ToDictionary(
            x => x.Code,
            StringComparer.OrdinalIgnoreCase);

        var createdCount = 0;
        var existingCount = 0;

        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            foreach (var seed in accounts
                         .OrderBy(x => x.Level)
                         .ThenBy(x => x.Code))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (accountByCode.ContainsKey(seed.Code))
                {
                    existingCount++;
                    continue;
                }

                Guid? parentAccountId = null;
                var level = 1;

                if (!string.IsNullOrWhiteSpace(seed.ParentCode))
                {
                    if (!accountByCode.TryGetValue(
                            seed.ParentCode,
                            out var parent))
                        throw new InvalidOperationException(
                            $"{seed.Code} hesabının üst hesabı " +
                            $"{seed.ParentCode} bulunamadı.");

                    parentAccountId = parent.Id;
                    level = parent.Level + 1;
                }

                if (!Enum.TryParse<AccountingAccountNature>(
                        seed.Nature,
                        true,
                        out var nature))
                    throw new InvalidOperationException(
                        $"{seed.Code} hesabında geçersiz hesap karakteri: " +
                        seed.Nature);

                var account = new AccountingAccount
                {
                    CompanyId = companyId,
                    ParentAccountId = parentAccountId,
                    Code = seed.Code.Trim(),
                    Name = seed.Name.Trim(),
                    Nature = nature,
                    Level = level,
                    IsPostingAllowed = seed.IsPostingAllowed,
                    RequiresProject = seed.RequiresProject,
                    RequiresCostCenter = seed.RequiresCostCenter,
                    CurrencyCode = string.IsNullOrWhiteSpace(seed.CurrencyCode)
                        ? "TRY"
                        : seed.CurrencyCode.Trim().ToUpperInvariant(),
                    IsActive = seed.IsActive
                };

                dbContext.AccountingAccounts.Add(account);
                accountByCode[account.Code] = account;
                createdCount++;
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        logger.LogInformation(
            "Enderun hesap planı kuruldu. Şirket: {CompanyId}, " +
            "Oluşturulan: {Created}, Mevcut: {Existing}, Toplam: {Total}",
            companyId,
            createdCount,
            existingCount,
            accounts.Count);

        return new AccountingAccountSeedResult(
            createdCount,
            existingCount,
            accounts.Count,
            $"{createdCount} hesap oluşturuldu, " +
            $"{existingCount} mevcut hesap atlandı. " +
            $"Toplam Enderun hesap planı: {accounts.Count}.");
    }

    private async Task<IReadOnlyCollection<SeedAccount>> LoadAccountsAsync(
        CancellationToken cancellationToken)
    {
        var fileName = "enderun-accounting-accounts.json";

        var possiblePaths = new[]
        {
            Path.Combine(
                environment.ContentRootPath,
                "Data",
                "Seeds",
                fileName),
            Path.Combine(
                AppContext.BaseDirectory,
                "Data",
                "Seeds",
                fileName),
            Path.GetFullPath(Path.Combine(
                environment.ContentRootPath,
                "..",
                "backend",
                "EnderunAI.Api",
                "Data",
                "Seeds",
                fileName))
        };

        var filePath = possiblePaths.FirstOrDefault(File.Exists);

        if (filePath is null)
            throw new FileNotFoundException(
                "Enderun hesap planı JSON dosyası bulunamadı.",
                possiblePaths[0]);

        await using var stream = File.OpenRead(filePath);
        var accounts = await JsonSerializer.DeserializeAsync<List<SeedAccount>>(
            stream,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            },
            cancellationToken);

        return accounts ?? [];
    }
}
