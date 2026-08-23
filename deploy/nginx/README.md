# nginx yapılandırma parçaları

Bu dizindeki dosyalar **canlı sunucuda kullanılan** nginx
yapılandırmasının parçalarıdır. Repoda durmalarının sebebi tek:

> Sunucu yeniden kurulduğunda ya da yapılandırma elle değiştiğinde
> koruma **sessizce kaybolmasın**. Repoda değilse yoktur.

## portal-token-maskeleme.conf

İşveren portalı bağlantısı `/portal/{token}` biçiminde, yani 256 bitlik
anahtar **URL yolunda** taşınıyor. nginx varsayılan olarak istek
satırının tamamını kaydeder; bu, anahtarın düz metin olarak
`/var/log/nginx/access.log` içine ve oradan log yedeklerine düşmesi
demektir. Kaydı okuyabilen herkes çalışan bir portal anahtarı elde
ederdi — uygulama tarafında token'ı maskelemek, kaydın kendisi
sızdırıyorsa anlamsız kalır.

### Kurulum

```bash
sudo cp deploy/nginx/portal-token-maskeleme.conf /etc/nginx/conf.d/
# nginx.conf içinde:
#   access_log /var/log/nginx/access.log;
# satırını şununla değiştir:
#   access_log /var/log/nginx/access.log maskeli;
sudo nginx -t && sudo systemctl reload nginx
```

### Doğrulama

Kod okuyarak değil, **görerek**: bir portal bağlantısı aç, sonra

```bash
sudo grep portal /var/log/nginx/access.log | tail -1
```

Çıktıda `/portal/***` görünmeli, token görünmemeli.

### Bunun çözmediği şey

Maskeleme **yalnızca sunucu kaydını** korur. Token URL yolunda
taşındığı sürece tarayıcı geçmişine, `Referer` başlığına ve paylaşılan
ekran görüntülerine de düşer. Kalıcı çözüm token'ı ilk açılışta kısa
ömürlü bir oturum çerezine dönüştürmektir; DURUM.md'de açık madde.
