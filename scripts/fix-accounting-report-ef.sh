#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FILE="$ROOT/backend/EnderunAI.Api/Controllers/AccountingReportsController.cs"

python3 - "$FILE" <<'PY'
from pathlib import Path
import sys

path = Path(sys.argv[1])
text = path.read_text(encoding="utf-8")

old_record = '''    private sealed record ReportRow(
        Guid VoucherId,
        DateTime VoucherDate,
        string VoucherNumber,
        int VoucherType,
        string? VoucherDescription,
        string? ReferenceNumber,
        string? SourceModule,
        int LineNumber,
        Guid AccountingAccountId,
        string AccountCode,
        string AccountName,
        string? LineDescription,
        Guid? CurrentAccountId,
        string? CurrentAccountCode,
        string? CurrentAccountTitle,
        Guid? ProjectId,
        string? ProjectCode,
        string? ProjectName,
        string? CostCenterCode,
        string? DocumentNumber,
        DateTime? DocumentDate,
        DateTime? DueDate,
        string CurrencyCode,
        decimal ExchangeRate,
        decimal DebitAmount,
        decimal CreditAmount,
        decimal DebitAmountLocal,
        decimal CreditAmountLocal);
'''

new_record = '''    private sealed class ReportRow
    {
        public Guid VoucherId { get; init; }
        public DateTime VoucherDate { get; init; }
        public string VoucherNumber { get; init; } = string.Empty;
        public int VoucherType { get; init; }
        public string? VoucherDescription { get; init; }
        public string? ReferenceNumber { get; init; }
        public string? SourceModule { get; init; }
        public int LineNumber { get; init; }
        public Guid AccountingAccountId { get; init; }
        public string AccountCode { get; init; } = string.Empty;
        public string AccountName { get; init; } = string.Empty;
        public string? LineDescription { get; init; }
        public Guid? CurrentAccountId { get; init; }
        public string? CurrentAccountCode { get; init; }
        public string? CurrentAccountTitle { get; init; }
        public Guid? ProjectId { get; init; }
        public string? ProjectCode { get; init; }
        public string? ProjectName { get; init; }
        public string? CostCenterCode { get; init; }
        public string? DocumentNumber { get; init; }
        public DateTime? DocumentDate { get; init; }
        public DateTime? DueDate { get; init; }
        public string CurrencyCode { get; init; } = string.Empty;
        public decimal ExchangeRate { get; init; }
        public decimal DebitAmount { get; init; }
        public decimal CreditAmount { get; init; }
        public decimal DebitAmountLocal { get; init; }
        public decimal CreditAmountLocal { get; init; }
    }
'''

old_project = '''    private static IQueryable<ReportRow> Project(
        IQueryable<AccountingVoucherLine> query)
    {
        return query.Select(x => new ReportRow(
            x.AccountingVoucherId,
            x.AccountingVoucher.VoucherDate,
            x.AccountingVoucher.VoucherNumber,
            (int)x.AccountingVoucher.VoucherType,
            x.AccountingVoucher.Description,
            x.AccountingVoucher.ReferenceNumber,
            x.AccountingVoucher.SourceModule,
            x.LineNumber,
            x.AccountingAccountId,
            x.AccountingAccount.Code,
            x.AccountingAccount.Name,
            x.Description,
            x.CurrentAccountId,
            x.CurrentAccount != null
                ? x.CurrentAccount.Code
                : null,
            x.CurrentAccount != null
                ? x.CurrentAccount.Title
                : null,
            x.ProjectId,
            x.Project != null
                ? x.Project.Code
                : null,
            x.Project != null
                ? x.Project.Name
                : null,
            x.CostCenterCode,
            x.DocumentNumber,
            x.DocumentDate,
            x.DueDate,
            x.CurrencyCode,
            x.ExchangeRate,
            x.DebitAmount,
            x.CreditAmount,
            x.DebitAmountLocal,
            x.CreditAmountLocal));
    }
'''

new_project = '''    private static IQueryable<ReportRow> Project(
        IQueryable<AccountingVoucherLine> query)
    {
        return query.Select(x => new ReportRow
        {
            VoucherId = x.AccountingVoucherId,
            VoucherDate = x.AccountingVoucher.VoucherDate,
            VoucherNumber = x.AccountingVoucher.VoucherNumber,
            VoucherType = (int)x.AccountingVoucher.VoucherType,
            VoucherDescription = x.AccountingVoucher.Description,
            ReferenceNumber = x.AccountingVoucher.ReferenceNumber,
            SourceModule = x.AccountingVoucher.SourceModule,
            LineNumber = x.LineNumber,
            AccountingAccountId = x.AccountingAccountId,
            AccountCode = x.AccountingAccount.Code,
            AccountName = x.AccountingAccount.Name,
            LineDescription = x.Description,
            CurrentAccountId = x.CurrentAccountId,
            CurrentAccountCode = x.CurrentAccount != null
                ? x.CurrentAccount.Code
                : null,
            CurrentAccountTitle = x.CurrentAccount != null
                ? x.CurrentAccount.Title
                : null,
            ProjectId = x.ProjectId,
            ProjectCode = x.Project != null
                ? x.Project.Code
                : null,
            ProjectName = x.Project != null
                ? x.Project.Name
                : null,
            CostCenterCode = x.CostCenterCode,
            DocumentNumber = x.DocumentNumber,
            DocumentDate = x.DocumentDate,
            DueDate = x.DueDate,
            CurrencyCode = x.CurrencyCode,
            ExchangeRate = x.ExchangeRate,
            DebitAmount = x.DebitAmount,
            CreditAmount = x.CreditAmount,
            DebitAmountLocal = x.DebitAmountLocal,
            CreditAmountLocal = x.CreditAmountLocal
        });
    }
'''

if old_record not in text:
    raise SystemExit("ReportRow record block was not found; file may have changed.")
if old_project not in text:
    raise SystemExit("Project method block was not found; file may have changed.")

text = text.replace(old_record, new_record, 1)
text = text.replace(old_project, new_project, 1)
path.write_text(text, encoding="utf-8")
print(f"Updated: {path}")
PY

cd "$ROOT/backend/EnderunAI.Api"
dotnet build -c Release

cd "$ROOT"
git add backend/EnderunAI.Api/Controllers/AccountingReportsController.cs
git commit -m "fix(accounting): use EF-translatable report projection"
git push origin "$(git branch --show-current)"

echo "Accounting report projection fix committed and pushed."
