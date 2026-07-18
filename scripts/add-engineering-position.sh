#!/usr/bin/env bash
set -euo pipefail

ROOT="/var/www/enderun-ai"
APP="$ROOT/backend/EnderunAI.Api"
MODEL="$APP/Models/EngineeringPosition.cs"
DBCONTEXT="$APP/Data/AppDbContext.cs"
BACKUP="$ROOT/backups/engineering-position-$(date +%Y%m%d-%H%M%S)"

echo "==> Engineering Position kurulumu başlıyor..."

mkdir -p "$BACKUP"

cp "$DBCONTEXT" "$BACKUP/AppDbContext.cs"

if [ -f "$MODEL" ]; then
    cp "$MODEL" "$BACKUP/EngineeringPosition.cs"
fi

cat > "$MODEL" <<'CSHARP'
namespace EnderunAI.Api.Models;

public enum EngineeringPositionSource
{
    Official = 0,
    Enderun = 1
}

public enum EngineeringPositionDiscipline
{
    Electrical = 0,
    MediumVoltage = 1,
    LowCurrent = 2,
    DataCenter = 3,
    Fiber = 4,
    Mechanical = 5,
    Civil = 6,
    Other = 99
}

public enum EngineeringPositionStatus
{
    Draft = 0,
    Active = 1,
    Passive = 2,
    Archived = 3
}

public sealed class EngineeringPosition : BaseEntity
{
    public Guid CompanyId { get; set; }
    public Company Company { get; set; } = null!;

    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;

    public EngineeringPositionSource Source { get; set; }
    public EngineeringPositionDiscipline Discipline { get; set; }

    public EngineeringPositionStatus Status { get; set; }
        = EngineeringPositionStatus.Draft;

    public string? OfficialInstitution { get; set; }
    public string? OfficialCode { get; set; }
    public string? Category { get; set; }
    public string? Description { get; set; }
    public string? TechnicalSpecification { get; set; }
    public string? SearchKeywords { get; set; }

    public decimal DefaultMasterHours { get; set; }
    public decimal DefaultHelperHours { get; set; }
    public decimal DefaultMachineHours { get; set; }

    public int RevisionNumber { get; set; }
}
CSHARP

python3 <<'PY'
from pathlib import Path

path = Path("/var/www/enderun-ai/backend/EnderunAI.Api/Data/AppDbContext.cs")
text = path.read_text(encoding="utf-8")

dbset = """    public DbSet<EngineeringPosition> EngineeringPositions
        => Set<EngineeringPosition>();

"""

if "DbSet<EngineeringPosition>" not in text:
    marker = """    public DbSet<PurchaseRequestItem> PurchaseRequestItems => Set<PurchaseRequestItem>();

"""
    if marker not in text:
        raise SystemExit(
            "HATA: PurchaseRequestItem DbSet ekleme noktası bulunamadı."
        )

    text = text.replace(marker, marker + dbset)

configure_call = """        ConfigureEngineeringPositions(modelBuilder);
"""

if "ConfigureEngineeringPositions(modelBuilder);" not in text:
    marker = """        ConfigurePurchaseRequestItems(modelBuilder);
"""
    if marker not in text:
        raise SystemExit(
            "HATA: OnModelCreating ekleme noktası bulunamadı."
        )

    text = text.replace(marker, marker + configure_call)

configuration = """
    private static void ConfigureEngineeringPositions(
        ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<EngineeringPosition>(entity =>
        {
            entity.ToTable("engineering_positions");
            entity.HasKey(x => x.Id);

            entity.HasIndex(x => new
            {
                x.CompanyId,
                x.Code
            }).IsUnique();

            entity.HasIndex(x => new
            {
                x.CompanyId,
                x.Source,
                x.Discipline
            });

            entity.Property(x => x.Code)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.Name)
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(x => x.Unit)
                .HasMaxLength(30)
                .IsRequired();

            entity.Property(x => x.OfficialInstitution)
                .HasMaxLength(150);

            entity.Property(x => x.OfficialCode)
                .HasMaxLength(80);

            entity.Property(x => x.Category)
                .HasMaxLength(200);

            entity.Property(x => x.TechnicalSpecification)
                .HasMaxLength(4000);

            entity.Property(x => x.SearchKeywords)
                .HasMaxLength(1000);

            entity.Property(x => x.DefaultMasterHours)
                .HasPrecision(18, 4);

            entity.Property(x => x.DefaultHelperHours)
                .HasPrecision(18, 4);

            entity.Property(x => x.DefaultMachineHours)
                .HasPrecision(18, 4);

            entity.HasOne(x => x.Company)
                .WithMany()
                .HasForeignKey(x => x.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasQueryFilter(x => !x.IsDeleted);
        });
    }

"""

if "private static void ConfigureEngineeringPositions" not in text:
    last_brace = text.rfind("}")

    if last_brace == -1:
        raise SystemExit(
            "HATA: AppDbContext kapanış parantezi bulunamadı."
        )

    text = text[:last_brace] + configuration + text[last_brace:]

path.write_text(text, encoding="utf-8")

print("AppDbContext başarıyla güncellendi.")
PY

cd "$APP"

echo "==> İlk build çalıştırılıyor..."
dotnet build

if find Migrations -maxdepth 1 \
    -type f \
    -name "*AddEngineeringPositions*.cs" \
    | grep -q .
then
    echo "==> AddEngineeringPositions migration zaten mevcut."
else
    echo "==> Migration oluşturuluyor..."
    dotnet ef migrations add AddEngineeringPositions
fi

echo "==> Veritabanı güncelleniyor..."
dotnet ef database update

echo "==> Son build çalıştırılıyor..."
dotnet build

cd "$ROOT"

echo
echo "=============================================="
echo "Engineering Position başarıyla kuruldu."
echo "Yedek klasörü:"
echo "$BACKUP"
echo
echo "Git durumu:"
git status --short
echo
echo "Sonraki komutlar:"
echo 'git add backend/EnderunAI.Api scripts/add-engineering-position.sh'
echo 'git commit -m "feat: add engineering position library"'
echo 'git push origin feature/recipe-engine'
echo "=============================================="
