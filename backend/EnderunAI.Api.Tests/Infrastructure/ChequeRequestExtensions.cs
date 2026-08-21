using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace EnderunAI.Api.Tests.Infrastructure;

/// <summary>
/// ÇEK İSTEKLERİNE EŞZAMANLI DEĞİŞİKLİK DAMGASI EKLER.
///
/// Çekin durumunu değiştiren HER uç damga istiyor (ciro, bankaya verme,
/// tahsil, ödeme, karşılıksız, iade, erteleme, dağılım, kırdırma, durum
/// geri alma, iptal, düzenleme). Testlerin her birinde damgayı elle
/// okumak, otuz küsur çağrı yerinde aynı üç satırı tekrarlamak
/// olurdu — ve biri unutulduğunda hata testin kendisinde çıkardı.
///
/// Damga ÇAĞRI ANINDA sunucudan okunuyor: testler art arda işlem
/// yapıyor ve her işlem damgayı ilerletiyor.
/// </summary>
public static class ChequeRequestExtensions
{
    /// <summary>Çekin o anki damgasını sunucudan okur.</summary>
    public static async Task<DateTime> ChequeRowVersionAsync(
        this HttpClient client, Guid chequeId)
    {
        var detail = await client.GetFromJsonAsync<JsonElement>(
            $"/api/cheques/{chequeId}");

        return detail.GetProperty("rowVersion").GetDateTime();
    }

    /// <summary>Gövdeye damgayı ekleyip POST eder.</summary>
    public static Task<HttpResponseMessage> PostChequeAsync(
        this HttpClient client, string url, Guid chequeId, object body) =>
        SendWithRowVersionAsync(client, HttpMethod.Post, url, chequeId, body);

    /// <summary>Gövdeye damgayı ekleyip PUT eder.</summary>
    public static Task<HttpResponseMessage> PutChequeAsync(
        this HttpClient client, string url, Guid chequeId, object body) =>
        SendWithRowVersionAsync(client, HttpMethod.Put, url, chequeId, body);

    private static async Task<HttpResponseMessage> SendWithRowVersionAsync(
        HttpClient client, HttpMethod method, string url, Guid chequeId, object body)
    {
        var node = JsonSerializer.SerializeToNode(body)!.AsObject();

        // Çağıran açıkça bir damga verdiyse ONA DOKUNULMAZ: bayat damga
        // senaryolarını sınayan testler kasten yanlış damga yolluyor.
        if (!node.ContainsKey("rowVersion"))
        {
            node["rowVersion"] = JsonValue.Create(
                await client.ChequeRowVersionAsync(chequeId));
        }

        var request = new HttpRequestMessage(method, url)
        {
            Content = JsonContent.Create(node)
        };

        return await client.SendAsync(request);
    }
}
