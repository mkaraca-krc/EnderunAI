ENDERUN AI BACKEND V1 - KURULUM

1) WinSCP ile ZIP dosyasını /root klasörüne yükleyin.
2) Paketi açın:
   mkdir -p /root/backend-v1
   unzip -o /root/Enderun-AI-Backend-V1-Identity.zip -d /root/backend-v1

3) Yedek:
   cd /var/www/enderun-ai/backend
   cp -a EnderunAI.Api "EnderunAI.Api-yedek-$(date +%Y%m%d-%H%M%S)"

4) Kopyalama:
   cp -a /root/backend-v1/. /var/www/enderun-ai/backend/EnderunAI.Api/

5) Paketler:
   cd /var/www/enderun-ai/backend/EnderunAI.Api
   dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL --version 8.*
   dotnet add package Microsoft.EntityFrameworkCore.Design --version 8.*
   dotnet add package Microsoft.AspNetCore.Authentication.JwtBearer --version 8.*

6) dotnet-ef:
   dotnet tool install --global dotnet-ef --version 8.*
   export PATH="$PATH:/root/.dotnet/tools"

7) Gizli ayarlar:
   mkdir -p /etc/enderunai
   nano /etc/enderunai/backend.env

   DB_CONNECTION=Host=127.0.0.1;Port=5432;Database=enderun_ai;Username=enderun_user;Password=VERITABANI_SIFRESI
   JWT_SECRET=OPENSSL_CIKTISI
   SEED_ADMIN_USERNAME=mehmet
   SEED_ADMIN_PASSWORD=YENI_GUCLU_SIFRE
   SEED_ADMIN_FULLNAME=Mehmet Karacabey

8) Migration:
   set -a
   source /etc/enderunai/backend.env
   set +a
   dotnet ef migrations add InitialIdentity
   dotnet ef database update

9) Publish:
   dotnet publish -c Release -o /var/www/enderun-ai/publish

10) Systemd servis dosyasına:
    EnvironmentFile=/etc/enderunai/backend.env

11) Restart:
    systemctl daemon-reload
    systemctl restart enderunai-backend
    systemctl status enderunai-backend --no-pager

12) Test:
    curl http://127.0.0.1:5155/api/health
