using EnderunAI.Api.Contracts.Personnel;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using EnderunAI.Api.Services.HumanResources;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/personnel")]
[Route("api/hr/personnel")]
public sealed class PersonnelController(
    AppDbContext db,
    ISalaryVisibilityService salaryVisibility) : ControllerBase
{
    /// <summary>
    /// Personel kartlarındaki veri eksikleri, engelledikleri sürece
    /// göre sınıflandırılmış.
    ///
    /// Eksiklik kaydı ENGELLEMİYOR; bu uç eksikliğin neye mal olduğunu
    /// söylüyor. Canlıda aktif personelin yarısından fazlasında SGK
    /// sicil yok — zorunluluk konsaydı bu kayıtların hiçbiri
    /// düzenlenemezdi.
    ///
    /// Ücret rakamı DÖNMÜYOR: yalnızca kartın var olup olmadığı
    /// bakılıyor. Tutar görmek maaş izni ister, veri eksiği görmek
    /// istemez.
    /// </summary>
    [HttpGet("veri-eksikleri")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> DataCompleteness(
        [FromQuery] Guid? companyId,
        [FromServices] Data.HumanResources.HrDbContext hrDb,
        CancellationToken cancellationToken)
    {
        var query = db.Personnel
            .AsNoTracking()
            .Where(x => x.Status == PersonnelStatus.Active && x.IsActive);

        if (companyId is Guid id)
            query = query.Where(x => x.CompanyId == id);

        var personnel = await query
            .Select(x => new
            {
                x.Id,
                x.EmployeeNumber,
                x.FirstName,
                x.LastName,
                x.IdentityNumber,
                x.BirthDate,
                x.Phone,
                x.SgkRegistrationNumber,
                x.EmploymentStartDate,
                x.JobTitle,
                x.BranchId,
                WorkLocationType = (int)x.WorkLocationType,
                HasActiveSiteAssignment = x.SiteAssignments.Any(a => a.EndDate == null)
            })
            .ToListAsync(cancellationToken);

        var ids = personnel.Select(x => x.Id).ToList();

        // Bugün yürürlükte olan kart aranıyor: süresi geçmiş bir kart,
        // kart yokmuş gibi bordroyu engeller.
        var today = DateTime.UtcNow.Date;

        var withSalaryCard = (await hrDb.SalaryDefinitions
            .AsNoTracking()
            .Where(x => ids.Contains(x.PersonnelId) &&
                        x.EffectiveStartDate <= today &&
                        (x.EffectiveEndDate == null || x.EffectiveEndDate >= today))
            .Select(x => x.PersonnelId)
            .ToListAsync(cancellationToken))
            .ToHashSet();

        var summary = PersonnelDataCompletenessCalculator.Summarize(
            personnel
                .Select(x => new PersonnelDataInput(
                    Id: x.Id,
                    EmployeeNumber: x.EmployeeNumber,
                    FullName: $"{x.FirstName} {x.LastName}".Trim(),
                    IdentityNumber: x.IdentityNumber,
                    BirthDate: x.BirthDate,
                    Phone: x.Phone,
                    SgkRegistrationNumber: x.SgkRegistrationNumber,
                    EmploymentStartDate: x.EmploymentStartDate,
                    JobTitle: x.JobTitle,
                    BranchId: x.BranchId,
                    WorkLocationType: x.WorkLocationType,
                    HasActiveSiteAssignment: x.HasActiveSiteAssignment,
                    HasSalaryCard: withSalaryCard.Contains(x.Id)))
                .ToList());

        return Ok(summary);
    }

    [HttpGet]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetAll(
        [FromQuery] Guid? companyId,
        [FromQuery] Guid? projectId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var query = db.Personnel.AsNoTracking();

        if (companyId.HasValue)
            query = query.Where(x => x.CompanyId == companyId.Value);

        if (projectId.HasValue)
        {
            query = query.Where(x =>
                x.Assignments.Any(a =>
                    a.ProjectId == projectId.Value &&
                    a.IsActive &&
                    !a.IsDeleted));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();

            query = query.Where(x =>
                x.FirstName.ToLower().Contains(term) ||
                x.LastName.ToLower().Contains(term) ||
                x.EmployeeNumber.ToLower().Contains(term) ||
                (x.IdentityNumber != null &&
                 x.IdentityNumber.Contains(term)));
        }

        var canViewSalary = await salaryVisibility.CanViewSalaryAsync(cancellationToken);

        var items = await query
            .OrderBy(x => x.FirstName)
            .ThenBy(x => x.LastName)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.BranchId,
                BranchName = x.Branch != null ? x.Branch.Name : null,
                x.EmployeeNumber,
                x.FirstName,
                x.LastName,
                FullName = x.FirstName + " " + x.LastName,
                x.IdentityNumber,
                x.Phone,
                x.Email,
                x.JobTitle,
                x.Profession,
                x.EmploymentStartDate,
                x.EmploymentEndDate,
                // Ücret gizliliği: yetkisiz kullanıcıya tutar hiç dönmez.
                MonthlySalary = canViewSalary ? x.MonthlySalary : null,
                x.Status,
                x.IsActive,
                WorkLocationType = (int)x.WorkLocationType,
                // Görev yeri belirlenmemiş VEYA şantiye seçilip aktif
                // ataması olmayan personel atama bekliyor sayılır.
                IsAwaitingWorkLocation =
                    x.WorkLocationType == EnderunAI.Api.Models.WorkLocationType.Unassigned ||
                    (x.WorkLocationType == EnderunAI.Api.Models.WorkLocationType.ProjectSite &&
                     !x.SiteAssignments.Any(a => a.IsActive && !a.IsDeleted && a.EndDate == null)),
                ActiveAssignments = x.Assignments
                    .Where(a => a.IsActive && !a.IsDeleted && a.EndDate == null)
                    .Select(a => new
                    {
                        a.Id,
                        a.ProjectId,
                        ProjectCode = a.Project.Code,
                        ProjectName = a.Project.Name,
                        a.Role,
                        a.StartDate,
                        a.IsPrimaryAssignment
                    }),
                ActiveSiteAssignment = x.SiteAssignments
                    .Where(a => a.IsActive && !a.IsDeleted && a.EndDate == null)
                    .Select(a => new
                    {
                        a.Id,
                        a.ProjectSiteId,
                        SiteCode = a.ProjectSite.Code,
                        SiteName = a.ProjectSite.Name,
                        ProjectId = a.ProjectSite.ProjectId,
                        ProjectCode = a.ProjectSite.Project.Code,
                        ProjectName = a.ProjectSite.Project.Name,
                        a.Role,
                        a.StartDate
                    })
                    .FirstOrDefault()
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelView)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var canViewSalary = await salaryVisibility.CanViewSalaryAsync(cancellationToken);

        var item = await db.Personnel
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.CompanyId,
                CompanyName = x.Company.Name,
                x.BranchId,
                BranchName = x.Branch != null ? x.Branch.Name : null,
                x.EmployeeNumber,
                x.FirstName,
                x.LastName,
                FullName = x.FirstName + " " + x.LastName,
                x.IdentityNumber,
                x.BirthDate,
                x.Phone,
                x.Email,
                x.Address,
                x.JobTitle,
                x.Profession,
                x.SgkRegistrationNumber,
                x.EmploymentStartDate,
                x.EmploymentEndDate,
                // Ücret gizliliği: yetkisiz kullanıcıya tutar hiç dönmez.
                MonthlySalary = canViewSalary ? x.MonthlySalary : null,
                x.Status,
                x.IsActive,
                WorkLocationType = (int)x.WorkLocationType,
                IsAwaitingWorkLocation =
                    x.WorkLocationType == EnderunAI.Api.Models.WorkLocationType.Unassigned ||
                    (x.WorkLocationType == EnderunAI.Api.Models.WorkLocationType.ProjectSite &&
                     !x.SiteAssignments.Any(a => a.IsActive && !a.IsDeleted && a.EndDate == null)),
                Assignments = x.Assignments
                    .OrderByDescending(a => a.StartDate)
                    .Select(a => new
                    {
                        a.Id,
                        a.ProjectId,
                        ProjectCode = a.Project.Code,
                        ProjectName = a.Project.Name,
                        a.StartDate,
                        a.EndDate,
                        a.Role,
                        a.Notes,
                        a.IsPrimaryAssignment,
                        a.IsActive
                    }),
                ActiveSiteAssignment = x.SiteAssignments
                    .Where(a => a.IsActive && !a.IsDeleted && a.EndDate == null)
                    .Select(a => new
                    {
                        a.Id,
                        a.ProjectSiteId,
                        SiteCode = a.ProjectSite.Code,
                        SiteName = a.ProjectSite.Name,
                        ProjectId = a.ProjectSite.ProjectId,
                        ProjectCode = a.ProjectSite.Project.Code,
                        ProjectName = a.ProjectSite.Project.Name,
                        a.Role,
                        a.StartDate
                    })
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? NotFound(new { message = "Personel bulunamadı." })
            : Ok(item);
    }

    [HttpPost]
    [RequirePermission(PermissionCatalog.Keys.PersonnelCreate)]
    public async Task<IActionResult> Create(
        CreatePersonnelRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeNumber))
            return BadRequest(new { message = "Personel numarası zorunludur." });

        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(new { message = "Ad ve soyad zorunludur." });
        }

        var companyExists = await db.Companies.AnyAsync(
            x => x.Id == request.CompanyId && x.IsActive,
            cancellationToken);

        if (!companyExists)
            return BadRequest(new { message = "Geçerli bir şirket seçilmelidir." });

        if (request.BranchId.HasValue)
        {
            var branchExists = await db.Branches.AnyAsync(
                x => x.Id == request.BranchId.Value &&
                     x.CompanyId == request.CompanyId &&
                     x.IsActive,
                cancellationToken);

            if (!branchExists)
                return BadRequest(new { message = "Seçilen şube şirkete ait değil veya pasif." });
        }

        var employeeNumber = request.EmployeeNumber.Trim().ToUpperInvariant();

        if (await db.Personnel.AnyAsync(
                x => x.CompanyId == request.CompanyId &&
                     x.EmployeeNumber == employeeNumber,
                cancellationToken))
        {
            return Conflict(new { message = "Bu personel numarası zaten kullanılıyor." });
        }

        // Kimlik numarası BOŞ bırakılabilir ama yanlış girilemez.
        // Yanlış numara sisteme sessizce girer ve ancak SGK bildirimi
        // reddedildiğinde — aylar sonra — ortaya çıkardı.
        if (TurkishIdentityNumber.Describe(request.IdentityNumber) is string problem)
            return BadRequest(new { message = problem });

        if (!string.IsNullOrWhiteSpace(request.IdentityNumber) &&
            await db.Personnel.AnyAsync(
                x => x.IdentityNumber == request.IdentityNumber.Trim(),
                cancellationToken))
        {
            return Conflict(new { message = "Bu kimlik numarasıyla kayıtlı personel bulunuyor." });
        }

        var personnel = new Personnel
        {
            CompanyId = request.CompanyId,
            BranchId = request.BranchId,
            EmployeeNumber = employeeNumber,
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            IdentityNumber = request.IdentityNumber?.Trim(),
            BirthDate = UtcDate(request.BirthDate),
            Phone = request.Phone?.Trim(),
            Email = request.Email?.Trim(),
            Address = request.Address?.Trim(),
            JobTitle = request.JobTitle?.Trim(),
            Profession = request.Profession?.Trim(),
            SgkRegistrationNumber = request.SgkRegistrationNumber?.Trim(),
            EmploymentStartDate = UtcDate(request.EmploymentStartDate),
            MonthlySalary = request.MonthlySalary,
            Status = PersonnelStatus.Active
        };

        db.Personnel.Add(personnel);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Personel kaydı oluşturuldu.",
            personnel.Id,
            personnel.EmployeeNumber,
            personnel.FirstName,
            personnel.LastName
        });
    }

    [HttpPut("{id:guid}")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> Update(
        Guid id,
        UpdatePersonnelRequest request,
        CancellationToken cancellationToken)
    {
        var personnel = await db.Personnel
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (personnel is null)
            return NotFound(new { message = "Personel bulunamadı." });

        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName))
        {
            return BadRequest(new { message = "Ad ve soyad zorunludur." });
        }

        // Değişmemiş bozuk bir numara güncellemeyi kilitlemesin: kontrol
        // yalnızca GİRİLEN değer değiştiğinde uygulanıyor. (Canlıdaki 75
        // kaydın tamamı geçerli; bu güvenlik ağı ileriye dönük.)
        var incomingIdentity = request.IdentityNumber?.Trim();

        if (!string.Equals(incomingIdentity, personnel.IdentityNumber,
                StringComparison.Ordinal) &&
            TurkishIdentityNumber.Describe(incomingIdentity) is string problem)
        {
            return BadRequest(new { message = problem });
        }

        if (request.BranchId.HasValue)
        {
            var branchExists = await db.Branches.AnyAsync(
                x => x.Id == request.BranchId.Value &&
                     x.CompanyId == personnel.CompanyId &&
                     x.IsActive,
                cancellationToken);

            if (!branchExists)
                return BadRequest(new { message = "Seçilen şube şirkete ait değil veya pasif." });
        }

        if (!Enum.IsDefined(typeof(PersonnelStatus), request.Status))
            return BadRequest(new { message = "Geçersiz personel durumu." });

        personnel.BranchId = request.BranchId;
        personnel.FirstName = request.FirstName.Trim();
        personnel.LastName = request.LastName.Trim();
        personnel.IdentityNumber = request.IdentityNumber?.Trim();
        personnel.BirthDate = UtcDate(request.BirthDate);
        personnel.Phone = request.Phone?.Trim();
        personnel.Email = request.Email?.Trim();
        personnel.Address = request.Address?.Trim();
        personnel.JobTitle = request.JobTitle?.Trim();
        personnel.Profession = request.Profession?.Trim();
        personnel.SgkRegistrationNumber = request.SgkRegistrationNumber?.Trim();
        personnel.EmploymentStartDate = UtcDate(request.EmploymentStartDate);
        personnel.EmploymentEndDate = UtcDate(request.EmploymentEndDate);
        personnel.MonthlySalary = request.MonthlySalary;
        personnel.Status = (PersonnelStatus)request.Status;
        personnel.IsActive = request.IsActive;
        personnel.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Personel kaydı güncellendi." });
    }

    [HttpPost("{id:guid}/assignments")]
    /// <summary>
    /// Personel kartından görev yeri belirleme: merkez mi, şantiye mi.
    ///
    /// Şantiye seçilirse mevcut aktif atama kapatılıp yenisi açılır —
    /// şantiye ekranındaki "tek aktif atama" kuralı burada da geçerli.
    /// Merkez veya atanmadı seçilirse aktif şantiye ataması kapatılır;
    /// aksi halde personel hem merkezde hem şantiyede görünürdü.
    /// </summary>
    [HttpPut("{id:guid}/gorev-yeri")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> SetWorkLocation(
        Guid id,
        SetWorkLocationRequest request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(typeof(WorkLocationType), request.WorkLocationType))
            return BadRequest(new { message = "Geçersiz görev yeri türü." });

        var personnel = await db.Personnel
            .SingleOrDefaultAsync(x => x.Id == id && x.IsActive, cancellationToken);

        if (personnel is null)
            return NotFound(new { message = "Aktif personel bulunamadı." });

        var locationType = (WorkLocationType)request.WorkLocationType;
        var now = DateTime.UtcNow;

        var activeAssignments = await db.ProjectSiteAssignments
            .Where(x => x.PersonnelId == id && x.IsActive && x.EndDate == null)
            .ToListAsync(cancellationToken);

        if (locationType == WorkLocationType.ProjectSite)
        {
            if (request.ProjectSiteId is not Guid siteId)
                return BadRequest(new { message = "Şantiye seçilmelidir." });

            var site = await db.ProjectSites
                .Include(x => x.Project)
                .SingleOrDefaultAsync(x => x.Id == siteId && x.IsActive, cancellationToken);

            if (site is null)
                return NotFound(new { message = "Aktif şantiye bulunamadı." });

            if (site.Project.CompanyId != personnel.CompanyId)
            {
                return BadRequest(new
                {
                    message = "Personel ve şantiye aynı şirkete ait olmalıdır."
                });
            }

            // Zaten aynı şantiyedeyse yeni kayıt açma; tarih/rol
            // güncellemesi atama ekranının işi.
            if (activeAssignments.Any(x => x.ProjectSiteId == siteId))
            {
                personnel.WorkLocationType = locationType;
                personnel.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);

                return Ok(new { message = "Personel zaten bu şantiyede görevli." });
            }

            CloseAssignments(activeAssignments, now);

            db.ProjectSiteAssignments.Add(new ProjectSiteAssignment
            {
                PersonnelId = id,
                ProjectSiteId = siteId,
                StartDate = UtcDate(request.StartDate ?? now),
                Role = request.Role?.Trim(),
                Notes = request.Notes?.Trim()
            });
        }
        else
        {
            CloseAssignments(activeAssignments, now);

            if (locationType == WorkLocationType.HeadOffice)
            {
                if (request.BranchId is Guid branchId)
                {
                    var branchExists = await db.Branches.AnyAsync(
                        x => x.Id == branchId && x.CompanyId == personnel.CompanyId,
                        cancellationToken);

                    if (!branchExists)
                        return BadRequest(new { message = "Şube bulunamadı." });

                    personnel.BranchId = branchId;
                }
                else
                {
                    // Birim seçilmediyse şirketin merkez ofisine atanır.
                    // Daha önce burada personelin eski şubesi ne ise o
                    // kalıyordu; merkeze atanan personel şantiye şubesinde
                    // görünebiliyor, masraf merkezi de yanlış çıkıyordu.
                    var headOfficeId = await db.Branches
                        .Where(x => x.CompanyId == personnel.CompanyId &&
                                    x.IsHeadOffice && x.IsActive)
                        .Select(x => (Guid?)x.Id)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (headOfficeId is null)
                    {
                        return BadRequest(new
                        {
                            message = "Şirkette merkez birimi tanımlı değil. " +
                                      "Şubeler ekranından merkez ofisi tanımlayın."
                        });
                    }

                    personnel.BranchId = headOfficeId;
                }
            }
        }

        personnel.WorkLocationType = locationType;
        personnel.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = locationType switch
            {
                WorkLocationType.HeadOffice => "Personel merkeze atandı.",
                WorkLocationType.ProjectSite => "Personel şantiyeye atandı.",
                _ => "Personelin görev yeri kaldırıldı."
            },
            workLocationType = (int)locationType
        });
    }

    private static void CloseAssignments(
        IReadOnlyCollection<ProjectSiteAssignment> assignments, DateTime now)
    {
        foreach (var assignment in assignments)
        {
            assignment.EndDate = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
            assignment.IsActive = false;
            assignment.UpdatedAtUtc = now;
        }
    }

    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> AssignToProject(
        Guid id,
        AssignPersonnelRequest request,
        CancellationToken cancellationToken)
    {
        var personnel = await db.Personnel
            .SingleOrDefaultAsync(
                x => x.Id == id && x.IsActive,
                cancellationToken);

        if (personnel is null)
            return NotFound(new { message = "Aktif personel bulunamadı." });

        var project = await db.Projects
            .SingleOrDefaultAsync(
                x => x.Id == request.ProjectId && x.IsActive,
                cancellationToken);

        if (project is null)
            return NotFound(new { message = "Aktif proje bulunamadı." });

        if (project.CompanyId != personnel.CompanyId)
        {
            return BadRequest(new
            {
                message = "Personel ve proje aynı şirkete ait olmalıdır."
            });
        }

        var hasActiveAssignment = await db.PersonnelAssignments.AnyAsync(
            x => x.PersonnelId == id &&
                 x.ProjectId == request.ProjectId &&
                 x.IsActive &&
                 x.EndDate == null,
            cancellationToken);

        if (hasActiveAssignment)
        {
            return Conflict(new
            {
                message = "Personel bu projede zaten aktif olarak görevli."
            });
        }

        if (request.IsPrimaryAssignment)
        {
            var primaryAssignments = await db.PersonnelAssignments
                .Where(x =>
                    x.PersonnelId == id &&
                    x.IsPrimaryAssignment &&
                    x.IsActive &&
                    x.EndDate == null)
                .ToListAsync(cancellationToken);

            foreach (var assignment in primaryAssignments)
            {
                assignment.IsPrimaryAssignment = false;
                assignment.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        var newAssignment = new PersonnelAssignment
        {
            PersonnelId = id,
            ProjectId = request.ProjectId,
            StartDate = UtcDate(request.StartDate),
            EndDate = UtcDate(request.EndDate),
            Role = request.Role?.Trim(),
            Notes = request.Notes?.Trim(),
            IsPrimaryAssignment = request.IsPrimaryAssignment
        };

        db.PersonnelAssignments.Add(newAssignment);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new
        {
            message = "Personel projeye atandı.",
            newAssignment.Id
        });
    }

    [HttpPut("assignments/{assignmentId:guid}/close")]
    [RequirePermission(PermissionCatalog.Keys.PersonnelEdit)]
    public async Task<IActionResult> CloseAssignment(
        Guid assignmentId,
        [FromQuery] DateTime? endDate,
        CancellationToken cancellationToken)
    {
        var assignment = await db.PersonnelAssignments
            .SingleOrDefaultAsync(
                x => x.Id == assignmentId,
                cancellationToken);

        if (assignment is null)
            return NotFound(new { message = "Proje görevlendirmesi bulunamadı." });

        assignment.EndDate = endDate.HasValue
            ? UtcDate(endDate.Value)
            : UtcDate(DateTime.UtcNow);
        assignment.IsActive = false;
        assignment.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "Proje görevlendirmesi kapatıldı." });
    }
    private static DateTime UtcDate(DateTime value)
    {
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    private static DateTime? UtcDate(DateTime? value)
    {
        return value.HasValue
            ? UtcDate(value.Value)
            : null;
    }

}
