using System.Net;

namespace EnderunAI.Api.Services.Email;

public static class EmployerPortalEmailTemplate
{
    public static string Build(string projectName, string portalUrl, string? employerName)
    {
        var greetingName = string.IsNullOrWhiteSpace(employerName)
            ? "Merhaba,"
            : $"Sayın {WebUtility.HtmlEncode(employerName)},";

        var safeProjectName = WebUtility.HtmlEncode(projectName);
        var safeUrl = WebUtility.HtmlEncode(portalUrl);

        const string logoUrl = "https://enderunai.com.tr/logo-full-white.png";

        return $$"""
        <!doctype html>
        <html lang="tr">
        <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>{{safeProjectName}} - Saha Takip Portalı</title>
        </head>
        <body style="margin:0;padding:0;background:#f7f5ee;font-family:Arial,Helvetica,sans-serif;color:#1a2422;">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f7f5ee;padding:32px 0;">
            <tr>
              <td align="center">
                <table role="presentation" width="100%" style="max-width:520px;background:#ffffff;border-radius:12px;overflow:hidden;border:1px solid #dcd6c8;">
                  <tr>
                    <td style="background:#0b2c2d;padding:24px 32px;border-bottom:3px solid #f1a522;">
                      <img src="{{logoUrl}}" alt="Enderun Enerji" height="28" style="height:28px;width:auto;display:block;" />
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:32px;">
                      <p style="margin:0 0 16px;font-size:15px;line-height:1.6;">{{greetingName}}</p>
                      <p style="margin:0 0 24px;font-size:15px;line-height:1.6;">
                        <strong>{{safeProjectName}}</strong> sahasındaki günlük ilerlemeyi aşağıdaki
                        bağlantıdan 7/24 takip edebilirsiniz. Bilgiler her akşam güncellenmektedir.
                      </p>
                      <table role="presentation" cellpadding="0" cellspacing="0" style="margin:0 0 24px;">
                        <tr>
                          <td style="border-radius:8px;background:#18797c;">
                            <a href="{{safeUrl}}"
                               style="display:inline-block;padding:14px 32px;color:#ffffff;text-decoration:none;font-size:15px;font-weight:bold;border-radius:8px;">
                              Portalı Aç
                            </a>
                          </td>
                        </tr>
                      </table>
                      <p style="margin:0;font-size:13px;line-height:1.6;color:#5c6b68;">
                        Buton çalışmazsa aşağıdaki bağlantıyı tarayıcınıza kopyalayabilirsiniz:<br />
                        <a href="{{safeUrl}}" style="color:#18797c;word-break:break-all;">{{safeUrl}}</a>
                      </p>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:16px 32px;background:#f7f5ee;border-top:1px solid #dcd6c8;">
                      <p style="margin:0;font-size:12px;color:#5c6b68;">Enderun Enerji · Saha Takip Portalı</p>
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;
    }
}
