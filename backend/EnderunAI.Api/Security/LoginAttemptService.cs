using System.Collections.Concurrent;

namespace EnderunAI.Api.Security;

/// <summary>
/// GİRİŞ DENEME SAYACI — ANAHTAR BAZLI (IP ve KULLANICI ADI AYRI).
///
/// ═══ NEDEN İKİ ANAHTAR (2026-09-15) ═══
///
/// Yalnız IP saymak iki yönden eksikti:
///   · dağıtık bir deneme (çok IP, tek hesap) hiç sayılmazdı,
///   · tek IP'nin arkasındaki bir ofisin tamamı birlikte sayılırdı.
///
/// Anahtar önekle ayrılıyor: `ip:&lt;adres&gt;` ve `kul:&lt;kullanıcı&gt;`.
///
/// ═══ KİLİT SÜRELİDİR, KALICI DEĞİL ═══
///
/// Kalıcı hesap kilidi, saldırganın elinde HİZMET ENGELLEME aracına
/// dönüşür: kullanıcı adını bilen herkes o hesabı kapatabilirdi.
/// Kilit kendiliğinden açılır.
/// </summary>
public interface ILoginAttemptService
{
    bool IsLocked(string anahtar, out TimeSpan remaining);

    /// <summary>
    /// Başarısızlığı sayar. **Dönüş: bu çağrıyla KİLİTLENDİ mi.**
    /// Çağıran, kilit ANINDA bir denetim satırı yazsın diye —
    /// her reddedilen isteğe satır yazmak sel altında kaydın kendisini
    /// yük hâline getirirdi.
    /// </summary>
    bool RecordFailure(string anahtar, int esik = 5);

    void RecordSuccess(string anahtar);
}

public sealed class LoginAttemptService : ILoginAttemptService
{
    /// <summary>
    /// IP için eşik. KULLANICI ADI için çağıran daha cömert bir eşik
    /// verir — kullanıcı adı kilidi gerçek bir insanı etkiler ve
    /// hizmet engelleme yüzeyidir.
    /// </summary>
    private const int MaxFailures = 5;
    private static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

    private sealed class State
    {
        public int FailureCount;
        public DateTime FirstFailureAtUtc;
        public DateTime? LockedUntilUtc;
    }

    private readonly ConcurrentDictionary<string, State> _states = new();

    public bool IsLocked(string ipAddress, out TimeSpan remaining)
    {
        remaining = TimeSpan.Zero;

        if (!_states.TryGetValue(ipAddress, out var state))
            return false;

        if (state.LockedUntilUtc is null)
            return false;

        var now = DateTime.UtcNow;
        if (state.LockedUntilUtc.Value <= now)
        {
            _states.TryRemove(ipAddress, out _);
            return false;
        }

        remaining = state.LockedUntilUtc.Value - now;
        return true;
    }

    public bool RecordFailure(string ipAddress, int esik = MaxFailures)
    {
        var now = DateTime.UtcNow;

        var state = _states.AddOrUpdate(
            ipAddress,
            _ => new State { FailureCount = 1, FirstFailureAtUtc = now },
            (_, existing) =>
            {
                if (now - existing.FirstFailureAtUtc > AttemptWindow)
                {
                    existing.FailureCount = 1;
                    existing.FirstFailureAtUtc = now;
                }
                else
                {
                    existing.FailureCount++;
                }

                return existing;
            });

        // ZATEN KİLİTLİYSE "yeni kilitlendi" DEME: çağıran her
        // reddedilen istekte denetim satırı yazmasın.
        var oncedenKilitli = state.LockedUntilUtc is not null && state.LockedUntilUtc > now;

        if (state.FailureCount >= esik)
        {
            state.LockedUntilUtc = now.Add(LockDuration);
            return !oncedenKilitli;
        }

        return false;
    }

    public void RecordSuccess(string ipAddress)
    {
        _states.TryRemove(ipAddress, out _);
    }
}
