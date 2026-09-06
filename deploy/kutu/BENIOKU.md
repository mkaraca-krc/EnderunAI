# KUTU/1 — CC'NİN BAĞLANTIDAN BAĞIMSIZ HÂLE GELMESİ

Bu klasör, **canlıda koşan** dosyaların depo kopyasıdır. Kaynak yeri:

| Depo yolu | Canlı yol |
|---|---|
| `bin/*.sh` | `/usr/local/bin/` |
| `systemd/*` | `/etc/systemd/system/` |

## NEDEN BURADALAR — KURAL 73

2026-09-06'da sunucu âniden yeniden başladı ve geçici dizindeki iki
taslak kayboldu. Kural 73 o kayıptan doğdu: *"Çalışma ağacındaki
commit'lenmemiş iş, yeniden başlatmadan sağ çıkmaz."*

Bu betikler yeniden başlatmadan sağ çıktı — çünkü `/etc` ve
`/usr/local` kalıcı. Ama **disk giderse hepsi gider** ve o güne kadar
hiçbiri gözden geçirilmemiş, hiçbirinin geçmişi yok olurdu. Depoya
alınmalarının sebebi budur.

## SIR YOK

Hiçbirinde parola, belirteç ya da kişisel veri yok — kontrol edildi.
Değişkenler ayrı yerlerde durur ve **depoya girmez**:

- rapor adresi → `/root/.enderun-rapor-yolu` (0600)
- devir durumu → `/var/lib/enderun-ai/cc-devir.conf`

## ZAMAN AŞIMI — 2026-09-06'DA EKLENDİ

`cc-devir.service` ve `enderun-rapor.service` `Type=oneshot`'tır ve
systemd'nin bu tip için varsayılanı `TimeoutStartSec=infinity`'dir.
**Takılan bir koşu zamanlayıcıyı sonsuza kadar bloklar.** 2026-09-06
03:58–07:12 arası tam olarak bu oldu: rapor kanalı 3 saat 15 dakika
sessizce öldü ve kimse fark etmedi. Sınırlar artık açıkça yazılı;
`sleep 600` ile kanıtlandı (sonda Z1, DURUM.md / TUR 3).

## CANLIYA UYGULAMA

Bu klasör **otomatik dağıtılmaz.** Değişiklik yapılırsa elle kopyalanır
ve `systemctl daemon-reload` çağrılır. Otomatik eşitleme, canlı sistem
kabuğuna dokunduğu için ayrı bir karar konusudur.
