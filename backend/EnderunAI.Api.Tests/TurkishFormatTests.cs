using System.Globalization;
using System.Text.RegularExpressions;
using EnderunAI.Api.Formatting;
using Xunit;

namespace EnderunAI.Api.Tests;

/// <summary>
/// Türkçe sayı biçimi.
///
/// Asıl güvence, sunucu kültürü ne olursa olsun ÇIKTININ TÜRKÇE
/// kalması: binlik nokta, ondalık virgül. Invariant kültürde
/// "60,000.00" çıkar ve Türkçe okuyan kullanıcı bunu ALTMIŞ diye
/// anlar — tutarın bin katı yanlış okunması hakediş onayında geri
/// dönüşü zor bir hata.
/// </summary>
public sealed class TurkishFormatTests
{
    /// <summary>
    /// Testin kendisi kültürden etkilenmesin diye çağrı invariant
    /// kültür altında yapılıyor: üretimdeki durumun aynısı.
    /// </summary>
    private static T UnderInvariantCulture<T>(Func<T> action)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Amount_UsesDotForThousandsAndCommaForDecimals()
    {
        var text = UnderInvariantCulture(() => TurkishFormat.Amount(60_000m));

        Assert.Equal("60.000,00", text);
    }

    [Fact]
    public void Amount_KeepsTwoDecimals()
    {
        Assert.Equal("1.234,50",
            UnderInvariantCulture(() => TurkishFormat.Amount(1_234.5m)));
        Assert.Equal("0,00",
            UnderInvariantCulture(() => TurkishFormat.Amount(0m)));
        Assert.Equal("-4.010,00",
            UnderInvariantCulture(() => TurkishFormat.Amount(-4_010m)));
    }

    [Fact]
    public void Quantity_KeepsFourDecimals()
    {
        Assert.Equal("1.250,7500",
            UnderInvariantCulture(() => TurkishFormat.Quantity(1_250.75m)));
    }

    [Fact]
    public void Whole_DropsDecimals()
    {
        Assert.Equal("320",
            UnderInvariantCulture(() => TurkishFormat.Whole(320m)));
        Assert.Equal("86.746.260",
            UnderInvariantCulture(() => TurkishFormat.Whole(86_746_259.84m)));
    }

    [Fact]
    public void Number_UsesTheRequestedPrecision()
    {
        Assert.Equal("12,3",
            UnderInvariantCulture(() => TurkishFormat.Number(12.34m, 1)));
    }

    /// <summary>
    /// Asıl tuzağın kendisi: ham biçim invariant kültürde ayıraçları
    /// TERS verir. Bu test o farkı görünür tutuyor — biri
    /// TurkishFormat'ı "gereksiz" diye kaldırmaya kalkarsa burada
    /// kırılır.
    /// </summary>
    [Fact]
    public void RawFormattingWouldSwapTheSeparators()
    {
        var raw = UnderInvariantCulture(() => $"{60_000m:N2}");

        Assert.Equal("60,000.00", raw);
        Assert.NotEqual(raw, TurkishFormat.Amount(60_000m));
    }

    /// <summary>
    /// Kod tabanında Türkçe metin içinde ham sayı biçimi kalmamalı.
    ///
    /// Tarama <c>:N&lt;rakam&gt;</c> arıyor; rakamsız <c>:N</c> Guid'in
    /// tiresiz biçimidir (dosya adı, poz kodu) ve sayı değildir, o
    /// yüzden kapsam dışında.
    /// </summary>
    [Fact]
    public void NoRawNumberFormatRemainsInApplicationCode()
    {
        var root = FindApiProjectRoot();
        var pattern = new Regex(@":N\d\}");

        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     root, "*.cs", SearchOption.AllDirectories))
        {
            // Migration'lar üretilmiş kod; obj/bin derleme çıktısı.
            if (file.Contains("Migrations") ||
                file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            var lineNumber = 0;

            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;

                var trimmed = line.TrimStart();

                if (trimmed.StartsWith("//"))
                    continue;

                if (pattern.IsMatch(line))
                {
                    offenders.Add(
                        $"{Path.GetFileName(file)}:{lineNumber} {trimmed}");
                }
            }
        }

        Assert.True(offenders.Count == 0,
            "Türkçe metinde ham sayı biçimi kaldı; TurkishFormat " +
            "kullanılmalı:\n" + string.Join("\n", offenders));
    }

    private static string FindApiProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "EnderunAI.Api");

            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("EnderunAI.Api klasörü bulunamadı.");
    }
}
