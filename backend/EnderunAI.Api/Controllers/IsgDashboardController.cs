using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.Isg;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

/// <summary>
/// İSG paneli özeti.
///
/// Kaza sayıları AYRI izinle korunuyor: isg.view olan herkes süre
/// takibini görür ama kaza defterine erişimi olmayan kullanıcıya kaza
/// rakamı dönmez — sayı bile kendi başına bilgi taşır.
/// </summary>
[ApiController]
[Authorize]
[Route("api/isg/dashboard")]
public sealed class IsgDashboardController(
    AppDbContext db,
    IUserAuthorizationService authorizationService,
    Security.CurrentUser.ICurrentUserService currentUser) : ControllerBase
{
    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.IsgView)]
    public async Task<IActionResult> Get(
        [FromQuery] Guid? companyId, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var horizon = today.AddDays(IsgValidityCalculator.WarningDays);

        var health = db.IsgHealthReports.AsNoTracking();
        var trainings = db.IsgTrainings.AsNoTracking();
        var certificates = db.IsgCertificates.AsNoTracking();
        var documents = db.IsgSiteDocuments.AsNoTracking();
        var contracts = db.IsgOsgbContracts.AsNoTracking();
        var personnel = db.Personnel.AsNoTracking()
            .Where(x => x.Status != PersonnelStatus.Terminated);

        if (companyId.HasValue)
        {
            health = health.Where(x => x.CompanyId == companyId.Value);
            trainings = trainings.Where(x => x.CompanyId == companyId.Value);
            certificates = certificates.Where(x => x.CompanyId == companyId.Value);
            documents = documents.Where(x => x.CompanyId == companyId.Value);
            contracts = contracts.Where(x => x.CompanyId == companyId.Value);
            personnel = personnel.Where(x => x.CompanyId == companyId.Value);
        }

        var activePersonnelIds = await personnel
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);

        // Geçerli sağlık raporu / temel eğitimi olan personel: eksikleri
        // saymak için önce olanları buluyoruz.
        var personnelWithValidHealth = await health
            .Where(x => x.ValidUntil == null || x.ValidUntil >= today)
            .Select(x => x.PersonnelId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var personnelWithValidTraining = await trainings
            .Where(x => (x.TrainingType == IsgTrainingType.Basic ||
                         x.TrainingType == IsgTrainingType.Refresher) &&
                        (x.ValidUntil == null || x.ValidUntil >= today))
            .Select(x => x.PersonnelId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var missingHealth = activePersonnelIds
            .Except(personnelWithValidHealth)
            .Count();

        var missingTraining = activePersonnelIds
            .Except(personnelWithValidTraining)
            .Count();

        var response = new Dictionary<string, object?>
        {
            ["saglikRaporu"] = new
            {
                suresiDoldu = await health.CountAsync(
                    x => x.ValidUntil != null && x.ValidUntil < today, cancellationToken),
                yakindaDoluyor = await health.CountAsync(
                    x => x.ValidUntil != null &&
                         x.ValidUntil >= today && x.ValidUntil <= horizon,
                    cancellationToken),
                eksikPersonel = missingHealth
            },
            ["egitim"] = new
            {
                suresiDoldu = await trainings.CountAsync(
                    x => x.ValidUntil != null && x.ValidUntil < today, cancellationToken),
                yakindaDoluyor = await trainings.CountAsync(
                    x => x.ValidUntil != null &&
                         x.ValidUntil >= today && x.ValidUntil <= horizon,
                    cancellationToken),
                temelEgitimiEksikPersonel = missingTraining
            },
            ["sertifika"] = new
            {
                suresiDoldu = await certificates.CountAsync(
                    x => x.ExpiryDate != null && x.ExpiryDate < today, cancellationToken),
                yakindaDoluyor = await certificates.CountAsync(
                    x => x.ExpiryDate != null &&
                         x.ExpiryDate >= today && x.ExpiryDate <= horizon,
                    cancellationToken)
            },
            ["sahaBelgeleri"] = new
            {
                suresiDoldu = await documents.CountAsync(
                    x => x.ValidUntil != null && x.ValidUntil < today, cancellationToken),
                yakindaDoluyor = await documents.CountAsync(
                    x => x.ValidUntil != null &&
                         x.ValidUntil >= today && x.ValidUntil <= horizon,
                    cancellationToken),
                riskDegerlendirmesiOlanSantiye = await documents
                    .Where(x => x.DocumentType == IsgSiteDocumentType.RiskAssessment &&
                                (x.ValidUntil == null || x.ValidUntil >= today) &&
                                x.ProjectSiteId != null)
                    .Select(x => x.ProjectSiteId)
                    .Distinct()
                    .CountAsync(cancellationToken)
            },
            ["osgb"] = new
            {
                aktifSozlesme = await contracts.CountAsync(
                    x => x.StartDate <= today &&
                         (x.EndDate == null || x.EndDate >= today),
                    cancellationToken),
                suresiDoluyor = await contracts.CountAsync(
                    x => x.EndDate != null &&
                         x.EndDate >= today && x.EndDate <= horizon,
                    cancellationToken),
                suresiDoldu = await contracts.CountAsync(
                    x => x.EndDate != null && x.EndDate < today, cancellationToken)
            },
            ["aktifPersonel"] = activePersonnelIds.Count,
            ["uyariEsigiGun"] = IsgValidityCalculator.WarningDays
        };

        // Kaza rakamları yalnızca kaza defterini görebilene.
        if (await CanViewIncidentsAsync(cancellationToken))
        {
            var incidents = db.IsgIncidents.AsNoTracking();

            if (companyId.HasValue)
                incidents = incidents.Where(x => x.CompanyId == companyId.Value);

            var openIncidents = await incidents
                .Where(x => x.Status != IsgIncidentStatus.Closed)
                .Select(x => new { x.Severity })
                .ToListAsync(cancellationToken);

            var unnotified = await incidents
                .Where(x => x.IncidentType == IsgIncidentType.Accident && !x.SgkNotified)
                .ToListAsync(cancellationToken);

            var yearStart = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            response["kaza"] = new
            {
                acikKayit = openIncidents.Count,
                agirKayit = openIncidents.Count(x =>
                    x.Severity >= IsgIncidentSeverity.LostWorkday),
                sgkBildirimiGecikmis = unnotified.Count(
                    IsgIncidentService.IsNotificationOverdue),
                buYilKaza = await incidents.CountAsync(
                    x => x.IncidentType == IsgIncidentType.Accident &&
                         x.IncidentDateTime >= yearStart,
                    cancellationToken),
                buYilRamakKala = await incidents.CountAsync(
                    x => x.IncidentType == IsgIncidentType.NearMiss &&
                         x.IncidentDateTime >= yearStart,
                    cancellationToken),
                buYilKayipIsGunu = await incidents
                    .Where(x => x.IncidentDateTime >= yearStart)
                    .SumAsync(x => x.LostWorkDays, cancellationToken)
            };
        }
        else
        {
            // Sayı bile bilgi taşır; yetkisi olmayana "görünmüyor" der.
            response["kaza"] = null;
            response["kazaGizli"] = true;
        }

        return Ok(response);
    }

    private async Task<bool> CanViewIncidentsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId)
            return false;

        var snapshot = await authorizationService.GetAsync(userId, cancellationToken);

        return snapshot is not null && snapshot.IsActive &&
               snapshot.Permissions.Contains(
                   PermissionCatalog.Keys.IsgIncidentView, StringComparer.OrdinalIgnoreCase);
    }
}
