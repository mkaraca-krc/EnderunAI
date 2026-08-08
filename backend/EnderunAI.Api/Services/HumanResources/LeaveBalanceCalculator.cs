namespace EnderunAI.Api.Services.HumanResources;

/// <param name="UsedDays">Onaylanmış yıllık izin günü.</param>
/// <param name="PendingDays">Onay bekleyen yıllık izin günü. Bakiyeden
/// düşülmez ama "kullanılabilir" hesabında sayılır: aynı günü iki kez
/// vaat etmemek için.</param>
public sealed record LeaveBalanceInput(
    Guid PersonnelId,
    string EmployeeNumber,
    string FullName,
    DateTime? EmploymentStartDate,
    decimal UsedDays,
    decimal PendingDays);

/// <param name="EntitlementDays">Bugüne kadar hak edilen TOPLAM gün.
/// Devir dahildir: hak edilen izin zaman aşımına uğramaz.</param>
/// <param name="RemainingDays">Hak ediş − kullanılan.</param>
/// <param name="AvailableDays">Kalan − onay bekleyen. Yeni talep bu
/// rakamla karşılaştırılır.</param>
/// <param name="NextAccrualDate">Bir sonraki hak edişin doğacağı gün.</param>
public sealed record LeaveBalance(
    Guid PersonnelId,
    string EmployeeNumber,
    string FullName,
    int ServiceDays,
    int ServiceYears,
    int EntitlementDays,
    decimal UsedDays,
    decimal PendingDays,
    decimal RemainingDays,
    decimal AvailableDays,
    int CurrentTierDays,
    DateOnly? NextAccrualDate,
    int NextAccrualDays,
    string? Note);

/// <summary>
/// Yıllık izin bakiyesi.
///
/// Saf ve veritabanısız.
///
/// HAK EDİŞ KURALI TEKRAR YAZILMADI: kademe tablosu (1 yıl 14, 5 yıl
/// üstü 20, 15 yıl üstü 26) zaten
/// <see cref="SeveranceCalculationService"/> içinde ve çıkış
/// tazminatında kullanılıyor. İkinci bir kural yazmak, aynı personel
/// için ekranda ve çıkışta farklı iki rakam üretirdi.
///
/// Onaylanan kararlar:
///   - DEVİR SINIRSIZ. Hak edilen izin zaman aşımına uğramaz; bakiye
///     tüm hak edişten tüm kullanımın düşülmesidir.
///   - Hak ediş her HİZMET YILI dolduğunda doğar; ilk yılını
///     doldurmayanın yıllık izin hakkı yoktur.
/// </summary>
public static class LeaveBalanceCalculator
{
    private const int DaysPerServiceYear = 365;

    public static LeaveBalance Calculate(LeaveBalanceInput input, DateOnly asOf)
    {
        if (input.EmploymentStartDate is not DateTime start)
        {
            return Empty(
                input,
                "İşe giriş tarihi girilmemiş; kıdem ve dolayısıyla izin hak edişi " +
                "hesaplanamıyor.");
        }

        var startDate = DateOnly.FromDateTime(start.Date);

        if (startDate > asOf)
        {
            return Empty(input, "İşe giriş tarihi ileri bir tarih.")
                with { NextAccrualDate = startDate.AddDays(DaysPerServiceYear) };
        }

        var serviceDays = asOf.DayNumber - startDate.DayNumber;
        var serviceYears = serviceDays / DaysPerServiceYear;

        var entitlement =
            SeveranceCalculationService.TotalAnnualLeaveEntitlement(serviceDays);

        var remaining = decimal.Round(entitlement - input.UsedDays, 2);
        var available = decimal.Round(remaining - input.PendingDays, 2);

        // Bir sonraki hak ediş: içinde bulunduğu hizmet yılının bitişi.
        var nextAccrual = startDate.AddDays((serviceYears + 1) * DaysPerServiceYear);

        var nextTier = SeveranceCalculationService.AnnualLeaveEntitlementFor(
            (serviceYears + 1) * DaysPerServiceYear);

        var note = serviceYears == 0
            ? "İlk hizmet yılı dolmadı; yıllık izin hakkı henüz doğmadı."
            : remaining < 0m
                ? "Kullanılan izin hak edişi aşıyor (avans izin verilmiş olabilir)."
                : null;

        return new LeaveBalance(
            PersonnelId: input.PersonnelId,
            EmployeeNumber: input.EmployeeNumber,
            FullName: input.FullName,
            ServiceDays: serviceDays,
            ServiceYears: serviceYears,
            EntitlementDays: entitlement,
            UsedDays: input.UsedDays,
            PendingDays: input.PendingDays,
            RemainingDays: remaining,
            AvailableDays: available,
            CurrentTierDays: SeveranceCalculationService.AnnualLeaveEntitlementFor(
                serviceDays),
            NextAccrualDate: nextAccrual,
            NextAccrualDays: nextTier,
            Note: note);
    }

    /// <summary>
    /// Talep bakiyeyi aşıyorsa uyarı metni; aşmıyorsa null.
    ///
    /// ENGELLEMEZ: avans izin gerçek bir uygulama ve engellemek onay
    /// merciini sistemin dışına iterdi. Onaylayanın görmesi yeter.
    /// </summary>
    public static string? DescribeOverdraft(LeaveBalance balance, decimal requestedDays)
    {
        if (requestedDays <= 0m)
            return null;

        var over = decimal.Round(requestedDays - balance.AvailableDays, 2);

        if (over <= 0m)
            return null;

        if (balance.EntitlementDays == 0)
        {
            return $"Bu personelin yıllık izin hakkı henüz doğmadı; " +
                   $"{requestedDays:0.##} günlük talep tamamen avans izindir.";
        }

        return $"Talep kullanılabilir bakiyeyi {over:0.##} gün aşıyor " +
               $"(kullanılabilir {balance.AvailableDays:0.##} gün).";
    }

    private static LeaveBalance Empty(LeaveBalanceInput input, string note) =>
        new(
            PersonnelId: input.PersonnelId,
            EmployeeNumber: input.EmployeeNumber,
            FullName: input.FullName,
            ServiceDays: 0,
            ServiceYears: 0,
            EntitlementDays: 0,
            UsedDays: input.UsedDays,
            PendingDays: input.PendingDays,
            RemainingDays: decimal.Round(-input.UsedDays, 2),
            AvailableDays: decimal.Round(-input.UsedDays - input.PendingDays, 2),
            CurrentTierDays: 0,
            NextAccrualDate: null,
            NextAccrualDays: 0,
            Note: note);
}
