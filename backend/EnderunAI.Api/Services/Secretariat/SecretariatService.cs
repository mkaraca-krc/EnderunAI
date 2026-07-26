using EnderunAI.Api.Contracts.Secretariat;
using EnderunAI.Api.Data;
using EnderunAI.Api.Models.Secretariat;
using EnderunAI.Api.Services.DocumentNumbers;
using Microsoft.EntityFrameworkCore;

namespace EnderunAI.Api.Services.Secretariat;

public sealed class SecretariatService(
    AppDbContext db,
    IDocumentNumberService documentNumberService) : ISecretariatService
{
    public async Task<IReadOnlyCollection<CorrespondenceResponse>> GetCorrespondenceAsync(
        Guid? companyId,
        Guid? projectId,
        SecretariatDocumentDirection? direction,
        SecretariatDocumentStatus? status,
        string? search,
        DateTime? startDate,
        DateTime? endDate,
        CancellationToken cancellationToken = default)
    {
        var result = new List<CorrespondenceResponse>();
        var term = Normalize(search);

        if (direction is null or SecretariatDocumentDirection.Incoming)
        {
            var query = db.IncomingDocuments.AsNoTracking();
            if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
            if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
            if (status.HasValue) query = query.Where(x => x.Status == status.Value);
            if (startDate.HasValue) query = query.Where(x => x.DocumentDate >= startDate.Value);
            if (endDate.HasValue) query = query.Where(x => x.DocumentDate < endDate.Value.Date.AddDays(1));
            if (term is not null)
            {
                query = query.Where(x =>
                    EF.Functions.ILike(x.DocumentNumber, $"%{term}%") ||
                    EF.Functions.ILike(x.Subject, $"%{term}%") ||
                    EF.Functions.ILike(x.SenderName, $"%{term}%") ||
                    (x.SenderOrganization != null && EF.Functions.ILike(x.SenderOrganization, $"%{term}%")));
            }

            var items = await query
                .OrderByDescending(x => x.DocumentDate)
                .ThenByDescending(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var counts = await AttachmentCountsAsync(
                SecretariatDocumentDirection.Incoming,
                items.Select(x => x.Id),
                cancellationToken);
            result.AddRange(items.Select(x =>
                ToCorrespondence(x, counts.GetValueOrDefault(x.Id))));
        }

        if (direction is null or SecretariatDocumentDirection.Outgoing)
        {
            var query = db.OutgoingDocuments.AsNoTracking();
            if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
            if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
            if (status.HasValue) query = query.Where(x => x.Status == status.Value);
            if (startDate.HasValue) query = query.Where(x => x.DocumentDate >= startDate.Value);
            if (endDate.HasValue) query = query.Where(x => x.DocumentDate < endDate.Value.Date.AddDays(1));
            if (term is not null)
            {
                query = query.Where(x =>
                    EF.Functions.ILike(x.DocumentNumber, $"%{term}%") ||
                    EF.Functions.ILike(x.Subject, $"%{term}%") ||
                    EF.Functions.ILike(x.RecipientName, $"%{term}%") ||
                    (x.RecipientOrganization != null && EF.Functions.ILike(x.RecipientOrganization, $"%{term}%")));
            }

            var items = await query
                .OrderByDescending(x => x.DocumentDate)
                .ThenByDescending(x => x.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var counts = await AttachmentCountsAsync(
                SecretariatDocumentDirection.Outgoing,
                items.Select(x => x.Id),
                cancellationToken);
            result.AddRange(items.Select(x =>
                ToCorrespondence(x, counts.GetValueOrDefault(x.Id))));
        }

        return result
            .OrderByDescending(x => x.DocumentDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToArray();
    }

    public async Task<CorrespondenceDetailResponse?> GetCorrespondenceAsync(
        SecretariatDocumentDirection direction,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        CorrespondenceResponse? document;
        if (direction == SecretariatDocumentDirection.Incoming)
        {
            var item = await db.IncomingDocuments
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            document = item is null ? null : ToCorrespondence(
                item,
                await AttachmentCountAsync(direction, id, cancellationToken));
        }
        else
        {
            var item = await db.OutgoingDocuments
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            document = item is null ? null : ToCorrespondence(
                item,
                await AttachmentCountAsync(direction, id, cancellationToken));
        }

        if (document is null) return null;

        var attachments = await db.DocumentAttachments
            .AsNoTracking()
            .Where(x => x.Direction == direction && x.DocumentId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new DocumentAttachmentResponse(
                x.Id, x.Direction, x.DocumentId, x.FileName, x.StoredFileName,
                x.FilePath, x.ContentType, x.FileSize, x.Description, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        var workflow = await db.DocumentWorkflows
            .AsNoTracking()
            .Where(x => x.Direction == direction && x.DocumentId == id)
            .OrderByDescending(x => x.ActionAtUtc)
            .Select(x => new DocumentWorkflowResponse(
                x.Id, x.Action, x.Action.ToString(), x.FromUserName, x.ToUserName,
                x.Description, x.ActionAtUtc))
            .ToListAsync(cancellationToken);

        return new CorrespondenceDetailResponse(document, attachments, workflow);
    }

    public async Task<CorrespondenceDetailResponse> CreateCorrespondenceAsync(
        CreateCorrespondenceRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(request.CompanyId, cancellationToken);
        var registeredAt = request.RegistrationDate ?? DateTime.UtcNow;
        var documentNumber = Normalize(request.DocumentNumber) ??
            await documentNumberService.GenerateAsync(
                request.CompanyId,
                request.Direction == SecretariatDocumentDirection.Incoming
                    ? "SECRETARIAT_INCOMING"
                    : "SECRETARIAT_OUTGOING",
                request.Direction == SecretariatDocumentDirection.Incoming ? "GE" : "GI",
                cancellationToken);

        Guid id;
        if (request.Direction == SecretariatDocumentDirection.Incoming)
        {
            var item = new IncomingDocument
            {
                CompanyId = request.CompanyId,
                ProjectId = request.ProjectId,
                CategoryId = request.CategoryId,
                DocumentNumber = documentNumber,
                ExternalDocumentNumber = Normalize(request.ReferenceNumber),
                DocumentDate = request.DocumentDate,
                RegisteredAtUtc = registeredAt,
                SenderName = Required(request.SenderName ?? request.InstitutionName, "Gönderen"),
                SenderOrganization = Normalize(request.InstitutionName),
                Subject = Required(request.Subject, "Konu"),
                Description = Normalize(request.Description),
                DeliveryMethod = Normalize(request.DeliveryMethod),
                Priority = request.Priority,
                Status = SecretariatDocumentStatus.Registered,
                AssignedToUserId = request.AssignedToUserId,
                AssignedToName = Normalize(request.AssignedToName),
                DueDate = request.DueDate,
                Notes = Normalize(request.Notes),
                CreatedByUserId = userId
            };
            db.IncomingDocuments.Add(item);
            id = item.Id;
        }
        else
        {
            var item = new OutgoingDocument
            {
                CompanyId = request.CompanyId,
                ProjectId = request.ProjectId,
                CategoryId = request.CategoryId,
                DocumentNumber = documentNumber,
                DocumentDate = request.DocumentDate,
                RegisteredAtUtc = registeredAt,
                RecipientName = Required(request.RecipientName ?? request.InstitutionName, "Alıcı"),
                RecipientOrganization = Normalize(request.InstitutionName),
                Subject = Required(request.Subject, "Konu"),
                Description = Normalize(request.Description),
                DeliveryMethod = Normalize(request.DeliveryMethod),
                ReferenceNumber = Normalize(request.ReferenceNumber),
                SignedByName = Normalize(request.SignedByName),
                Priority = request.Priority,
                Status = SecretariatDocumentStatus.Draft,
                Notes = Normalize(request.Notes),
                CreatedByUserId = userId
            };
            db.OutgoingDocuments.Add(item);
            id = item.Id;
        }

        db.DocumentWorkflows.Add(new DocumentWorkflow
        {
            CompanyId = request.CompanyId,
            Direction = request.Direction,
            DocumentId = id,
            Action = SecretariatWorkflowAction.Created,
            FromUserId = userId,
            FromUserName = Normalize(userName),
            Description = "Evrak kaydı oluşturuldu.",
            CreatedByUserId = userId
        });
        await db.SaveChangesAsync(cancellationToken);
        return (await GetCorrespondenceAsync(request.Direction, id, cancellationToken))!;
    }

    public async Task<CorrespondenceDetailResponse?> UpdateCorrespondenceAsync(
        SecretariatDocumentDirection direction,
        Guid id,
        UpdateCorrespondenceRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        Guid companyId;
        if (direction == SecretariatDocumentDirection.Incoming)
        {
            var item = await db.IncomingDocuments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (item is null) return null;
            companyId = item.CompanyId;
            item.ProjectId = request.ProjectId;
            item.CategoryId = request.CategoryId;
            item.ExternalDocumentNumber = Normalize(request.ReferenceNumber);
            item.DocumentDate = request.DocumentDate;
            item.SenderName = Required(request.SenderName ?? request.InstitutionName, "Gönderen");
            item.SenderOrganization = Normalize(request.InstitutionName);
            item.Subject = Required(request.Subject, "Konu");
            item.Description = Normalize(request.Description);
            item.DeliveryMethod = Normalize(request.DeliveryMethod);
            item.Priority = request.Priority;
            item.Status = request.Status;
            item.AssignedToUserId = request.AssignedToUserId;
            item.AssignedToName = Normalize(request.AssignedToName);
            item.DueDate = request.DueDate;
            item.CompletedAtUtc = request.Status == SecretariatDocumentStatus.Completed
                ? item.CompletedAtUtc ?? DateTime.UtcNow
                : null;
            item.ArchivedAtUtc = request.Status == SecretariatDocumentStatus.Archived
                ? item.ArchivedAtUtc ?? DateTime.UtcNow
                : null;
            item.Notes = Normalize(request.Notes);
            Touch(item, userId);
        }
        else
        {
            var item = await db.OutgoingDocuments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (item is null) return null;
            companyId = item.CompanyId;
            item.ProjectId = request.ProjectId;
            item.CategoryId = request.CategoryId;
            item.DocumentDate = request.DocumentDate;
            item.RecipientName = Required(request.RecipientName ?? request.InstitutionName, "Alıcı");
            item.RecipientOrganization = Normalize(request.InstitutionName);
            item.Subject = Required(request.Subject, "Konu");
            item.Description = Normalize(request.Description);
            item.DeliveryMethod = Normalize(request.DeliveryMethod);
            item.ReferenceNumber = Normalize(request.ReferenceNumber);
            item.SignedByName = Normalize(request.SignedByName);
            item.Priority = request.Priority;
            item.Status = request.Status;
            item.SentAtUtc = request.SentAtUtc;
            item.CompletedAtUtc = request.Status == SecretariatDocumentStatus.Completed
                ? item.CompletedAtUtc ?? DateTime.UtcNow
                : null;
            item.ArchivedAtUtc = request.Status == SecretariatDocumentStatus.Archived
                ? item.ArchivedAtUtc ?? DateTime.UtcNow
                : null;
            item.Notes = Normalize(request.Notes);
            Touch(item, userId);
        }

        db.DocumentWorkflows.Add(new DocumentWorkflow
        {
            CompanyId = companyId,
            Direction = direction,
            DocumentId = id,
            Action = SecretariatWorkflowAction.Commented,
            FromUserId = userId,
            FromUserName = Normalize(userName),
            Description = "Evrak bilgileri güncellendi.",
            CreatedByUserId = userId
        });
        await db.SaveChangesAsync(cancellationToken);
        return await GetCorrespondenceAsync(direction, id, cancellationToken);
    }

    public async Task<bool> DeleteCorrespondenceAsync(
        SecretariatDocumentDirection direction,
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (direction == SecretariatDocumentDirection.Incoming)
        {
            var item = await db.IncomingDocuments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (item is null) return false;
            Delete(item, userId);
        }
        else
        {
            var item = await db.OutgoingDocuments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
            if (item is null) return false;
            Delete(item, userId);
        }
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AddWorkflowAsync(
        SecretariatDocumentDirection direction,
        Guid documentId,
        DocumentWorkflowRequest request,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default)
    {
        var companyId = await GetDocumentCompanyIdAsync(direction, documentId, cancellationToken);
        if (!companyId.HasValue) return false;

        db.DocumentWorkflows.Add(new DocumentWorkflow
        {
            CompanyId = companyId.Value,
            Direction = direction,
            DocumentId = documentId,
            Action = request.Action,
            FromUserId = userId,
            FromUserName = Normalize(userName),
            ToUserId = request.ToUserId,
            ToUserName = Normalize(request.ToUserName),
            Description = Normalize(request.Description),
            CreatedByUserId = userId
        });
        await ApplyWorkflowStatusAsync(direction, documentId, request, userId, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<bool> ArchiveCorrespondenceAsync(
        SecretariatDocumentDirection direction,
        Guid documentId,
        Guid? userId,
        string? userName,
        CancellationToken cancellationToken = default) =>
        AddWorkflowAsync(
            direction,
            documentId,
            new DocumentWorkflowRequest(
                SecretariatWorkflowAction.Archived,
                null,
                null,
                "Evrak arşivlendi."),
            userId,
            userName,
            cancellationToken);

    public async Task<DocumentAttachmentResponse?> AddAttachmentAsync(
        SecretariatDocumentDirection direction,
        Guid documentId,
        string fileName,
        string storedFileName,
        string filePath,
        string? contentType,
        long fileSize,
        string? description,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var companyId = await GetDocumentCompanyIdAsync(direction, documentId, cancellationToken);
        if (!companyId.HasValue) return null;
        var item = new DocumentAttachment
        {
            CompanyId = companyId.Value,
            Direction = direction,
            DocumentId = documentId,
            FileName = Required(fileName, "Dosya adı"),
            StoredFileName = Required(storedFileName, "Saklanan dosya adı"),
            FilePath = Required(filePath, "Dosya yolu"),
            ContentType = Normalize(contentType),
            FileSize = fileSize,
            Description = Normalize(description),
            CreatedByUserId = userId
        };
        db.DocumentAttachments.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return ToAttachment(item);
    }

    public async Task<DocumentAttachmentResponse?> GetAttachmentAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.DocumentAttachments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == attachmentId, cancellationToken);
        return item is null ? null : ToAttachment(item);
    }

    public async Task<bool> DeleteAttachmentAsync(
        Guid attachmentId,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.DocumentAttachments
            .SingleOrDefaultAsync(x => x.Id == attachmentId, cancellationToken);
        if (item is null) return false;
        Delete(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<DocumentCategoryResponse>> GetCategoriesAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        var query = db.DocumentCategories.AsNoTracking();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        return await query
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new DocumentCategoryResponse(
                x.Id, x.CompanyId, x.Code, x.Name, x.Description, x.IsDefault, x.IsActive))
            .ToListAsync(cancellationToken);
    }

    public async Task<DocumentCategoryResponse> CreateCategoryAsync(
        CreateDocumentCategoryRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(request.CompanyId, cancellationToken);
        var code = Required(request.Code, "Kategori kodu").ToUpperInvariant();
        if (await db.DocumentCategories.AnyAsync(
                x => x.CompanyId == request.CompanyId && x.Code == code,
                cancellationToken))
            throw new InvalidOperationException("Bu kategori kodu zaten kullanılıyor.");
        if (request.IsDefault)
        {
            await db.DocumentCategories
                .Where(x => x.CompanyId == request.CompanyId && x.IsDefault)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDefault, false), cancellationToken);
        }
        var item = new DocumentCategory
        {
            CompanyId = request.CompanyId,
            Code = code,
            Name = Required(request.Name, "Kategori adı"),
            Description = Normalize(request.Description),
            IsDefault = request.IsDefault,
            CreatedByUserId = userId
        };
        db.DocumentCategories.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return ToCategory(item);
    }

    public async Task<DocumentCategoryResponse?> UpdateCategoryAsync(
        Guid id,
        UpdateDocumentCategoryRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.DocumentCategories.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return null;
        if (request.IsDefault)
        {
            await db.DocumentCategories
                .Where(x => x.CompanyId == item.CompanyId && x.Id != id && x.IsDefault)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.IsDefault, false), cancellationToken);
        }
        item.Name = Required(request.Name, "Kategori adı");
        item.Description = Normalize(request.Description);
        item.IsDefault = request.IsDefault;
        item.IsActive = request.IsActive;
        Touch(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return ToCategory(item);
    }

    public async Task<IReadOnlyCollection<CargoResponse>> GetCargoAsync(
        Guid? companyId,
        Guid? projectId,
        CargoDirection? direction,
        CargoStatus? status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = db.CargoShipments.AsNoTracking();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (direction.HasValue) query = query.Where(x => x.Direction == direction.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        var term = Normalize(search);
        if (term is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.TrackingNumber, $"%{term}%") ||
                EF.Functions.ILike(x.CargoCompany, $"%{term}%") ||
                (x.SenderName != null && EF.Functions.ILike(x.SenderName, $"%{term}%")) ||
                (x.RecipientName != null && EF.Functions.ILike(x.RecipientName, $"%{term}%")));
        }
        var items = await query
            .OrderByDescending(x => x.CargoDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        return items.Select(ToCargo).ToArray();
    }

    public async Task<CargoResponse?> GetCargoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var item = await db.CargoShipments.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return item is null ? null : ToCargo(item);
    }

    public async Task<CargoResponse> CreateCargoAsync(
        CreateCargoRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(request.CompanyId, cancellationToken);
        var trackingNumber = Required(request.TrackingNumber, "Takip numarası");
        if (await db.CargoShipments.AnyAsync(
                x => x.CompanyId == request.CompanyId && x.TrackingNumber == trackingNumber,
                cancellationToken))
            throw new InvalidOperationException("Bu kargo takip numarası zaten kayıtlı.");
        var item = new CargoShipment
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            Direction = request.Direction,
            TrackingNumber = trackingNumber,
            CargoCompany = Required(request.CargoCompany, "Kargo firması"),
            SenderName = Normalize(request.SenderName),
            RecipientName = Normalize(request.RecipientName),
            InstitutionName = Normalize(request.InstitutionName),
            CargoDate = request.CargoDate,
            ExpectedDeliveryDate = request.ExpectedDeliveryDate,
            Description = Normalize(request.Description),
            CreatedByUserId = userId
        };
        db.CargoShipments.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return ToCargo(item);
    }

    public async Task<CargoResponse?> UpdateCargoAsync(
        Guid id,
        UpdateCargoRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.CargoShipments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return null;
        item.ProjectId = request.ProjectId;
        item.CargoCompany = Required(request.CargoCompany, "Kargo firması");
        item.SenderName = Normalize(request.SenderName);
        item.RecipientName = Normalize(request.RecipientName);
        item.InstitutionName = Normalize(request.InstitutionName);
        item.CargoDate = request.CargoDate;
        item.ExpectedDeliveryDate = request.ExpectedDeliveryDate;
        item.DeliveredAtUtc = request.Status == CargoStatus.Delivered
            ? request.DeliveredAtUtc ?? item.DeliveredAtUtc ?? DateTime.UtcNow
            : request.DeliveredAtUtc;
        item.DeliveredToName = Normalize(request.DeliveredToName);
        item.Description = Normalize(request.Description);
        item.Status = request.Status;
        Touch(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return ToCargo(item);
    }

    public async Task<bool> DeleteCargoAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
    {
        var item = await db.CargoShipments.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return false;
        Delete(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<VisitorResponse>> GetVisitorsAsync(
        Guid? companyId,
        Guid? projectId,
        VisitorStatus? status,
        DateTime? startDate,
        DateTime? endDate,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = db.VisitorRecords.AsNoTracking();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (startDate.HasValue) query = query.Where(x => x.PlannedVisitAtUtc >= startDate.Value);
        if (endDate.HasValue) query = query.Where(x => x.PlannedVisitAtUtc < endDate.Value.Date.AddDays(1));
        var term = Normalize(search);
        if (term is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.FullName, $"%{term}%") ||
                EF.Functions.ILike(x.PersonToVisit, $"%{term}%") ||
                (x.CompanyName != null && EF.Functions.ILike(x.CompanyName, $"%{term}%")) ||
                (x.VehiclePlate != null && EF.Functions.ILike(x.VehiclePlate, $"%{term}%")));
        }
        var items = await query
            .OrderByDescending(x => x.PlannedVisitAtUtc)
            .ToListAsync(cancellationToken);
        return items.Select(ToVisitor).ToArray();
    }

    public async Task<VisitorResponse> CreateVisitorAsync(
        CreateVisitorRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(request.CompanyId, cancellationToken);
        var item = new VisitorRecord
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            FullName = Required(request.FullName, "Ziyaretçi adı"),
            IdentityNumber = Normalize(request.IdentityNumber),
            PhoneNumber = Normalize(request.PhoneNumber),
            Email = Normalize(request.Email),
            CompanyName = Normalize(request.CompanyName),
            VehiclePlate = Normalize(request.VehiclePlate)?.ToUpperInvariant(),
            VisitorCardNumber = Normalize(request.VisitorCardNumber),
            PersonToVisit = Required(request.PersonToVisit, "Görüşülecek kişi"),
            DepartmentName = Normalize(request.DepartmentName),
            VisitPurpose = Required(request.VisitPurpose, "Ziyaret amacı"),
            PlannedVisitAtUtc = request.PlannedVisitAtUtc,
            ApprovedByName = Normalize(request.ApprovedByName),
            Description = Normalize(request.Description),
            CreatedByUserId = userId
        };
        db.VisitorRecords.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return ToVisitor(item);
    }

    public async Task<VisitorResponse?> CheckInVisitorAsync(
        Guid id,
        string? receivedByName,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.VisitorRecords.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return null;
        if (item.Status is VisitorStatus.CheckedOut or VisitorStatus.Cancelled or VisitorStatus.Rejected)
            throw new InvalidOperationException("Bu ziyaret kaydı için giriş yapılamaz.");
        item.Status = VisitorStatus.CheckedIn;
        item.CheckInAtUtc ??= DateTime.UtcNow;
        item.ReceivedByName = Normalize(receivedByName);
        Touch(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return ToVisitor(item);
    }

    public async Task<VisitorResponse?> CheckOutVisitorAsync(
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.VisitorRecords.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return null;
        if (item.Status != VisitorStatus.CheckedIn)
            throw new InvalidOperationException("Yalnızca içerideki ziyaretçi için çıkış yapılabilir.");
        item.Status = VisitorStatus.CheckedOut;
        item.CheckOutAtUtc = DateTime.UtcNow;
        Touch(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return ToVisitor(item);
    }

    public async Task<bool> DeleteVisitorAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
    {
        var item = await db.VisitorRecords.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return false;
        Delete(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<PhoneNoteResponse>> GetPhoneNotesAsync(
        Guid? companyId,
        Guid? projectId,
        PhoneNoteStatus? status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = db.PhoneNotes.AsNoTracking();
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        var term = Normalize(search);
        if (term is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.CallerName, $"%{term}%") ||
                EF.Functions.ILike(x.Subject, $"%{term}%") ||
                EF.Functions.ILike(x.ResponsibleName, $"%{term}%") ||
                (x.InstitutionName != null && EF.Functions.ILike(x.InstitutionName, $"%{term}%")));
        }
        var items = await query
            .OrderByDescending(x => x.ReceivedAtUtc)
            .ToListAsync(cancellationToken);
        return items.Select(ToPhoneNote).ToArray();
    }

    public async Task<PhoneNoteResponse> CreatePhoneNoteAsync(
        CreatePhoneNoteRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(request.CompanyId, cancellationToken);
        var item = new PhoneNote
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            CallerName = Required(request.CallerName, "Arayan kişi"),
            PhoneNumber = Normalize(request.PhoneNumber),
            InstitutionName = Normalize(request.InstitutionName),
            Subject = Required(request.Subject, "Konu"),
            Message = Required(request.Message, "Mesaj"),
            ResponsibleName = Required(request.ResponsibleName, "İletilecek kişi"),
            ReceivedAtUtc = request.ReceivedAtUtc ?? DateTime.UtcNow,
            Notes = Normalize(request.Notes),
            CreatedByUserId = userId
        };
        db.PhoneNotes.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return ToPhoneNote(item);
    }

    public async Task<PhoneNoteResponse?> UpdatePhoneNoteAsync(
        Guid id,
        UpdatePhoneNoteRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.PhoneNotes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return null;
        item.ProjectId = request.ProjectId;
        item.CallerName = Required(request.CallerName, "Arayan kişi");
        item.PhoneNumber = Normalize(request.PhoneNumber);
        item.InstitutionName = Normalize(request.InstitutionName);
        item.Subject = Required(request.Subject, "Konu");
        item.Message = Required(request.Message, "Mesaj");
        item.ResponsibleName = Required(request.ResponsibleName, "İletilecek kişi");
        item.ReceivedAtUtc = request.ReceivedAtUtc;
        SetPhoneNoteStatus(item, request.Status);
        item.Notes = Normalize(request.Notes);
        Touch(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return ToPhoneNote(item);
    }

    public async Task<PhoneNoteResponse?> UpdatePhoneNoteStatusAsync(
        Guid id,
        PhoneNoteStatus status,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.PhoneNotes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return null;
        SetPhoneNoteStatus(item, status);
        Touch(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return ToPhoneNote(item);
    }

    public async Task<bool> DeletePhoneNoteAsync(Guid id, Guid? userId, CancellationToken cancellationToken = default)
    {
        var item = await db.PhoneNotes.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (item is null) return false;
        Delete(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyCollection<ScheduleResponse>> GetSchedulesAsync(
        SecretariatScheduleType type,
        Guid? companyId,
        Guid? projectId,
        SecretariatScheduleStatus? status,
        DateTime? startDate,
        DateTime? endDate,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var query = db.SecretariatScheduleEntries.AsNoTracking().Where(x => x.Type == type);
        if (companyId.HasValue) query = query.Where(x => x.CompanyId == companyId.Value);
        if (projectId.HasValue) query = query.Where(x => x.ProjectId == projectId.Value);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (startDate.HasValue) query = query.Where(x => x.StartAtUtc >= startDate.Value);
        if (endDate.HasValue) query = query.Where(x => x.StartAtUtc < endDate.Value.Date.AddDays(1));
        var term = Normalize(search);
        if (term is not null)
        {
            query = query.Where(x =>
                EF.Functions.ILike(x.Title, $"%{term}%") ||
                (x.ContactName != null && EF.Functions.ILike(x.ContactName, $"%{term}%")) ||
                (x.CompanyName != null && EF.Functions.ILike(x.CompanyName, $"%{term}%")) ||
                (x.Location != null && EF.Functions.ILike(x.Location, $"%{term}%")));
        }
        var items = await query
            .OrderBy(x => x.StartAtUtc)
            .ToListAsync(cancellationToken);
        return items.Select(ToSchedule).ToArray();
    }

    public async Task<ScheduleResponse> CreateScheduleAsync(
        SecretariatScheduleType type,
        CreateScheduleRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        await EnsureCompanyAsync(request.CompanyId, cancellationToken);
        ValidateSchedule(request.StartAtUtc, request.EndAtUtc);
        var item = new SecretariatScheduleEntry
        {
            CompanyId = request.CompanyId,
            ProjectId = request.ProjectId,
            Type = type,
            Title = Required(request.Title, type == SecretariatScheduleType.Meeting ? "Toplantı başlığı" : "Randevu başlığı"),
            ContactName = Normalize(request.ContactName),
            CompanyName = Normalize(request.CompanyName),
            Location = Normalize(request.Location),
            StartAtUtc = request.StartAtUtc,
            EndAtUtc = request.EndAtUtc,
            OwnerName = Normalize(request.OwnerName),
            Participants = Normalize(request.Participants),
            Description = Normalize(request.Description),
            ReminderAtUtc = request.ReminderAtUtc,
            Notes = Normalize(request.Notes),
            CreatedByUserId = userId
        };
        db.SecretariatScheduleEntries.Add(item);
        await db.SaveChangesAsync(cancellationToken);
        return ToSchedule(item);
    }

    public async Task<ScheduleResponse?> UpdateScheduleAsync(
        SecretariatScheduleType type,
        Guid id,
        UpdateScheduleRequest request,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.SecretariatScheduleEntries
            .SingleOrDefaultAsync(x => x.Id == id && x.Type == type, cancellationToken);
        if (item is null) return null;
        ValidateSchedule(request.StartAtUtc, request.EndAtUtc);
        item.ProjectId = request.ProjectId;
        item.Title = Required(request.Title, "Başlık");
        item.ContactName = Normalize(request.ContactName);
        item.CompanyName = Normalize(request.CompanyName);
        item.Location = Normalize(request.Location);
        item.StartAtUtc = request.StartAtUtc;
        item.EndAtUtc = request.EndAtUtc;
        item.OwnerName = Normalize(request.OwnerName);
        item.Participants = Normalize(request.Participants);
        item.Description = Normalize(request.Description);
        item.ReminderAtUtc = request.ReminderAtUtc;
        SetScheduleStatus(item, request.Status);
        item.Notes = Normalize(request.Notes);
        Touch(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return ToSchedule(item);
    }

    public async Task<ScheduleResponse?> UpdateScheduleStatusAsync(
        SecretariatScheduleType type,
        Guid id,
        SecretariatScheduleStatus status,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.SecretariatScheduleEntries
            .SingleOrDefaultAsync(x => x.Id == id && x.Type == type, cancellationToken);
        if (item is null) return null;
        SetScheduleStatus(item, status);
        Touch(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return ToSchedule(item);
    }

    public async Task<bool> DeleteScheduleAsync(
        SecretariatScheduleType type,
        Guid id,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var item = await db.SecretariatScheduleEntries
            .SingleOrDefaultAsync(x => x.Id == id && x.Type == type, cancellationToken);
        if (item is null) return false;
        Delete(item, userId);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<SecretariatDashboardResponse> GetDashboardAsync(
        Guid? companyId,
        CancellationToken cancellationToken = default)
    {
        var start = DateTime.UtcNow.Date;
        var end = start.AddDays(1);
        var incoming = db.IncomingDocuments.AsNoTracking();
        var outgoing = db.OutgoingDocuments.AsNoTracking();
        var cargo = db.CargoShipments.AsNoTracking();
        var visitors = db.VisitorRecords.AsNoTracking();
        var notes = db.PhoneNotes.AsNoTracking();
        var schedule = db.SecretariatScheduleEntries.AsNoTracking();
        if (companyId.HasValue)
        {
            incoming = incoming.Where(x => x.CompanyId == companyId.Value);
            outgoing = outgoing.Where(x => x.CompanyId == companyId.Value);
            cargo = cargo.Where(x => x.CompanyId == companyId.Value);
            visitors = visitors.Where(x => x.CompanyId == companyId.Value);
            notes = notes.Where(x => x.CompanyId == companyId.Value);
            schedule = schedule.Where(x => x.CompanyId == companyId.Value);
        }

        var pendingStatuses = new[]
        {
            SecretariatDocumentStatus.Registered,
            SecretariatDocumentStatus.Assigned,
            SecretariatDocumentStatus.InProgress,
            SecretariatDocumentStatus.Answered
        };
        var todayIncoming = await incoming.CountAsync(
            x => x.RegisteredAtUtc >= start && x.RegisteredAtUtc < end, cancellationToken);
        var todayOutgoing = await outgoing.CountAsync(
            x => x.RegisteredAtUtc >= start && x.RegisteredAtUtc < end, cancellationToken);
        var pendingDocuments =
            await incoming.CountAsync(x => pendingStatuses.Contains(x.Status), cancellationToken) +
            await outgoing.CountAsync(x => pendingStatuses.Contains(x.Status), cancellationToken);
        var overdueDocuments = await incoming.CountAsync(
            x => x.DueDate < DateTime.UtcNow && pendingStatuses.Contains(x.Status), cancellationToken);
        var cargoInTransit = await cargo.CountAsync(
            x => x.Status is CargoStatus.Registered or CargoStatus.InTransit, cancellationToken);
        var visitorsInside = await visitors.CountAsync(
            x => x.Status == VisitorStatus.CheckedIn, cancellationToken);
        var openPhoneNotes = await notes.CountAsync(
            x => x.Status is PhoneNoteStatus.New or PhoneNoteStatus.Informed, cancellationToken);
        var todayMeetings = await schedule.CountAsync(
            x => x.Type == SecretariatScheduleType.Meeting &&
                 x.StartAtUtc >= start && x.StartAtUtc < end &&
                 x.Status != SecretariatScheduleStatus.Cancelled, cancellationToken);
        var todayAppointments = await schedule.CountAsync(
            x => x.Type == SecretariatScheduleType.Appointment &&
                 x.StartAtUtc >= start && x.StartAtUtc < end &&
                 x.Status != SecretariatScheduleStatus.Cancelled, cancellationToken);

        var activities = new List<SecretariatRecentActivityResponse>();
        activities.AddRange(await db.DocumentWorkflows.AsNoTracking()
            .Where(x => !companyId.HasValue || x.CompanyId == companyId.Value)
            .OrderByDescending(x => x.ActionAtUtc)
            .Take(8)
            .Select(x => new SecretariatRecentActivityResponse(
                "Evrak", x.DocumentId, x.Description ?? x.Action.ToString(),
                x.Action.ToString(), x.FromUserName, x.ActionAtUtc))
            .ToListAsync(cancellationToken));
        activities.AddRange(await notes
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(5)
            .Select(x => new SecretariatRecentActivityResponse(
                "Telefon", x.Id, x.Subject, x.Status.ToString(),
                x.ResponsibleName, x.CreatedAtUtc))
            .ToListAsync(cancellationToken));
        activities.AddRange(await schedule
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(5)
            .Select(x => new SecretariatRecentActivityResponse(
                x.Type == SecretariatScheduleType.Meeting ? "Toplantı" : "Randevu",
                x.Id, x.Title, x.Status.ToString(), x.OwnerName, x.CreatedAtUtc))
            .ToListAsync(cancellationToken));

        return new SecretariatDashboardResponse(
            todayIncoming,
            todayOutgoing,
            pendingDocuments,
            overdueDocuments,
            cargoInTransit,
            visitorsInside,
            openPhoneNotes,
            todayMeetings,
            todayAppointments,
            activities.OrderByDescending(x => x.ActionAtUtc).Take(12).ToArray());
    }

    private async Task<Dictionary<Guid, int>> AttachmentCountsAsync(
        SecretariatDocumentDirection direction,
        IEnumerable<Guid> ids,
        CancellationToken cancellationToken)
    {
        var values = ids.ToArray();
        if (values.Length == 0) return [];
        return await db.DocumentAttachments.AsNoTracking()
            .Where(x => x.Direction == direction && values.Contains(x.DocumentId))
            .GroupBy(x => x.DocumentId)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);
    }

    private Task<int> AttachmentCountAsync(
        SecretariatDocumentDirection direction,
        Guid id,
        CancellationToken cancellationToken) =>
        db.DocumentAttachments.AsNoTracking()
            .CountAsync(x => x.Direction == direction && x.DocumentId == id, cancellationToken);

    private async Task<Guid?> GetDocumentCompanyIdAsync(
        SecretariatDocumentDirection direction,
        Guid id,
        CancellationToken cancellationToken)
    {
        return direction == SecretariatDocumentDirection.Incoming
            ? await db.IncomingDocuments.Where(x => x.Id == id)
                .Select(x => (Guid?)x.CompanyId).SingleOrDefaultAsync(cancellationToken)
            : await db.OutgoingDocuments.Where(x => x.Id == id)
                .Select(x => (Guid?)x.CompanyId).SingleOrDefaultAsync(cancellationToken);
    }

    private async Task ApplyWorkflowStatusAsync(
        SecretariatDocumentDirection direction,
        Guid id,
        DocumentWorkflowRequest request,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        var status = request.Action switch
        {
            SecretariatWorkflowAction.Registered => SecretariatDocumentStatus.Registered,
            SecretariatWorkflowAction.Assigned => SecretariatDocumentStatus.Assigned,
            SecretariatWorkflowAction.Read or SecretariatWorkflowAction.Commented =>
                SecretariatDocumentStatus.InProgress,
            SecretariatWorkflowAction.Answered => SecretariatDocumentStatus.Answered,
            SecretariatWorkflowAction.Completed => SecretariatDocumentStatus.Completed,
            SecretariatWorkflowAction.Archived => SecretariatDocumentStatus.Archived,
            SecretariatWorkflowAction.Reopened => SecretariatDocumentStatus.InProgress,
            SecretariatWorkflowAction.Cancelled => SecretariatDocumentStatus.Cancelled,
            _ => (SecretariatDocumentStatus?)null
        };
        if (!status.HasValue) return;

        if (direction == SecretariatDocumentDirection.Incoming)
        {
            var item = await db.IncomingDocuments.SingleAsync(x => x.Id == id, cancellationToken);
            item.Status = status.Value;
            if (request.Action == SecretariatWorkflowAction.Assigned)
            {
                item.AssignedToUserId = request.ToUserId;
                item.AssignedToName = Normalize(request.ToUserName);
            }
            item.CompletedAtUtc = status == SecretariatDocumentStatus.Completed ? DateTime.UtcNow : item.CompletedAtUtc;
            item.ArchivedAtUtc = status == SecretariatDocumentStatus.Archived ? DateTime.UtcNow : item.ArchivedAtUtc;
            Touch(item, userId);
        }
        else
        {
            var item = await db.OutgoingDocuments.SingleAsync(x => x.Id == id, cancellationToken);
            item.Status = status.Value;
            item.CompletedAtUtc = status == SecretariatDocumentStatus.Completed ? DateTime.UtcNow : item.CompletedAtUtc;
            item.ArchivedAtUtc = status == SecretariatDocumentStatus.Archived ? DateTime.UtcNow : item.ArchivedAtUtc;
            Touch(item, userId);
        }
    }

    private async Task EnsureCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty ||
            !await db.Companies.AnyAsync(x => x.Id == companyId, cancellationToken))
            throw new InvalidOperationException("Geçerli bir şirket seçilmelidir.");
    }

    private static void ValidateSchedule(DateTime start, DateTime? end)
    {
        if (end.HasValue && end.Value < start)
            throw new InvalidOperationException("Bitiş zamanı başlangıç zamanından önce olamaz.");
    }

    private static void SetPhoneNoteStatus(PhoneNote item, PhoneNoteStatus status)
    {
        item.Status = status;
        if (status == PhoneNoteStatus.Informed) item.InformedAtUtc ??= DateTime.UtcNow;
        if (status == PhoneNoteStatus.Returned) item.ReturnedAtUtc ??= DateTime.UtcNow;
    }

    private static void SetScheduleStatus(
        SecretariatScheduleEntry item,
        SecretariatScheduleStatus status)
    {
        item.Status = status;
        item.CompletedAtUtc = status == SecretariatScheduleStatus.Completed
            ? item.CompletedAtUtc ?? DateTime.UtcNow
            : null;
    }

    private static CorrespondenceResponse ToCorrespondence(IncomingDocument x, int attachmentCount) =>
        new(
            x.Id, x.CompanyId, x.ProjectId, x.CategoryId,
            SecretariatDocumentDirection.Incoming, "Gelen Evrak",
            x.DocumentNumber, x.ExternalDocumentNumber, x.DocumentDate, x.RegisteredAtUtc,
            x.Subject, x.SenderName, null, x.SenderOrganization, x.DeliveryMethod,
            x.ExternalDocumentNumber, x.Description, null, x.Priority, x.Priority.ToString(),
            x.Status, x.Status.ToString(), x.AssignedToUserId, x.AssignedToName, x.DueDate,
            null, x.CompletedAtUtc, x.ArchivedAtUtc, x.Notes, attachmentCount, x.CreatedAtUtc);

    private static CorrespondenceResponse ToCorrespondence(OutgoingDocument x, int attachmentCount) =>
        new(
            x.Id, x.CompanyId, x.ProjectId, x.CategoryId,
            SecretariatDocumentDirection.Outgoing, "Giden Evrak",
            x.DocumentNumber, null, x.DocumentDate, x.RegisteredAtUtc,
            x.Subject, null, x.RecipientName, x.RecipientOrganization, x.DeliveryMethod,
            x.ReferenceNumber, x.Description, x.SignedByName, x.Priority, x.Priority.ToString(),
            x.Status, x.Status.ToString(), null, null, null, x.SentAtUtc,
            x.CompletedAtUtc, x.ArchivedAtUtc, x.Notes, attachmentCount, x.CreatedAtUtc);

    private static DocumentAttachmentResponse ToAttachment(DocumentAttachment x) =>
        new(
            x.Id, x.Direction, x.DocumentId, x.FileName, x.StoredFileName,
            x.FilePath, x.ContentType, x.FileSize, x.Description, x.CreatedAtUtc);

    private static DocumentCategoryResponse ToCategory(DocumentCategory x) =>
        new(x.Id, x.CompanyId, x.Code, x.Name, x.Description, x.IsDefault, x.IsActive);

    private static CargoResponse ToCargo(CargoShipment x) =>
        new(
            x.Id, x.CompanyId, x.ProjectId, x.Direction, x.Direction.ToString(),
            x.TrackingNumber, x.CargoCompany, x.SenderName, x.RecipientName,
            x.InstitutionName, x.CargoDate, x.ExpectedDeliveryDate, x.DeliveredAtUtc,
            x.DeliveredToName, x.Description, x.Status, x.Status.ToString(), x.CreatedAtUtc);

    private static VisitorResponse ToVisitor(VisitorRecord x) =>
        new(
            x.Id, x.CompanyId, x.ProjectId, x.FullName, x.IdentityNumber,
            x.PhoneNumber, x.Email, x.CompanyName, x.VehiclePlate, x.VisitorCardNumber,
            x.PersonToVisit, x.DepartmentName, x.VisitPurpose, x.PlannedVisitAtUtc,
            x.CheckInAtUtc, x.CheckOutAtUtc, x.ApprovedByName, x.ReceivedByName,
            x.Description, x.Status, x.Status.ToString(), x.CreatedAtUtc);

    private static PhoneNoteResponse ToPhoneNote(PhoneNote x) =>
        new(
            x.Id, x.CompanyId, x.ProjectId, x.CallerName, x.PhoneNumber,
            x.InstitutionName, x.Subject, x.Message, x.ResponsibleName,
            x.ReceivedAtUtc, x.InformedAtUtc, x.ReturnedAtUtc, x.Status,
            x.Status.ToString(), x.Notes, x.CreatedAtUtc);

    private static ScheduleResponse ToSchedule(SecretariatScheduleEntry x) =>
        new(
            x.Id, x.CompanyId, x.ProjectId, x.Type, x.Type.ToString(), x.Title,
            x.ContactName, x.CompanyName, x.Location, x.StartAtUtc, x.EndAtUtc,
            x.OwnerName, x.Participants, x.Description, x.ReminderAtUtc,
            x.CompletedAtUtc, x.Status, x.Status.ToString(), x.Notes, x.CreatedAtUtc);

    private static string Required(string? value, string field)
    {
        var normalized = Normalize(value);
        return normalized ?? throw new InvalidOperationException($"{field} zorunludur.");
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Touch(EnderunAI.Api.Models.BaseEntity entity, Guid? userId)
    {
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.UpdatedByUserId = userId;
    }

    private static void Delete(EnderunAI.Api.Models.BaseEntity entity, Guid? userId)
    {
        entity.IsDeleted = true;
        entity.IsActive = false;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedByUserId = userId;
    }
}
