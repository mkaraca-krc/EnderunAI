using EnderunAI.Api.Contracts.Personnel;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models;
using EnderunAI.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/personnel")]
[Route("api/hr/personnel")]
public sealed class PersonnelController(AppDbContext db) : ControllerBase
{
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
                x.MonthlySalary,
                x.Status,
                x.IsActive,
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
                x.MonthlySalary,
                x.Status,
                x.IsActive,
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
