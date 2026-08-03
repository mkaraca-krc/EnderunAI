using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <summary>
    /// payroll-bonus.* izin anahtarları salary.view / salary.manage
    /// olarak yeniden tanımlandı. İzin seed'i add-only olduğu için eski
    /// satırlar kendiliğinden silinmiyor; hiçbir uç tarafından
    /// kullanılmadıkları hâlde Yetki Matrisi ekranında görünmeye devam
    /// ederdi. Rol bağlantıları cascade ile birlikte temizlenir.
    /// </summary>
    public partial class RemoveLegacyPayrollBonusPermissions : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM role_permissions
                 WHERE ""PermissionId"" IN (
                    SELECT ""Id"" FROM permissions
                     WHERE ""Key"" LIKE 'payroll-bonus.%');

                DELETE FROM user_permission_overrides
                 WHERE ""PermissionId"" IN (
                    SELECT ""Id"" FROM permissions
                     WHERE ""Key"" LIKE 'payroll-bonus.%');

                DELETE FROM permissions WHERE ""Key"" LIKE 'payroll-bonus.%';
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Anahtarlar geri eklenir; rol bağlantıları seed tarafından
            // yeniden kurulur.
            migrationBuilder.Sql(@"
                INSERT INTO permissions (""Id"", ""Key"", ""Module"", ""Name"", ""Description"", ""IsActive"", ""IsDeleted"", ""CreatedAtUtc"")
                VALUES
                    (gen_random_uuid(), 'payroll-bonus.view', 'Ek Ödeme', 'Görüntüleme', 'Ek ödeme kayıtlarını görüntüler.', true, false, now()),
                    (gen_random_uuid(), 'payroll-bonus.create', 'Ek Ödeme', 'Oluşturma', 'Yeni ek ödeme kaydı oluşturur.', true, false, now()),
                    (gen_random_uuid(), 'payroll-bonus.edit', 'Ek Ödeme', 'Düzenleme', 'Ek ödeme kaydını günceller.', true, false, now()),
                    (gen_random_uuid(), 'payroll-bonus.delete', 'Ek Ödeme', 'Silme', 'Ek ödeme kaydını siler.', true, false, now())
                ON CONFLICT DO NOTHING;
            ");
        }
    }
}
