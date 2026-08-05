using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddChequeReplacementChain : Migration
    {
        /// <summary>
        /// Çek erteleme zinciri: ertelenen çek "Replaced" durumuna geçer
        /// ve yerine geçen yeni çeke bağlanır. Zincir iki yönlü tutulur
        /// ki detayda hem önceki hem sonraki çek görünsün; kaç kez
        /// ertelendiği zincir uzunluğundan hesaplanır (sayaç alanı
        /// tutulsaydı zincirle tutarsız kalabilirdi).
        ///
        /// Mevcut çeklerde her iki alan da boş kalır; davranış değişmez.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReplacedByChequeId",
                table: "cheques",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReplacesChequeId",
                table: "cheques",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_cheques_ReplacedByChequeId",
                table: "cheques",
                column: "ReplacedByChequeId");

            migrationBuilder.CreateIndex(
                name: "IX_cheques_ReplacesChequeId",
                table: "cheques",
                column: "ReplacesChequeId");

            migrationBuilder.AddForeignKey(
                name: "FK_cheques_cheques_ReplacedByChequeId",
                table: "cheques",
                column: "ReplacedByChequeId",
                principalTable: "cheques",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_cheques_cheques_ReplacesChequeId",
                table: "cheques",
                column: "ReplacesChequeId",
                principalTable: "cheques",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cheques_cheques_ReplacedByChequeId",
                table: "cheques");

            migrationBuilder.DropForeignKey(
                name: "FK_cheques_cheques_ReplacesChequeId",
                table: "cheques");

            migrationBuilder.DropIndex(
                name: "IX_cheques_ReplacedByChequeId",
                table: "cheques");

            migrationBuilder.DropIndex(
                name: "IX_cheques_ReplacesChequeId",
                table: "cheques");

            migrationBuilder.DropColumn(
                name: "ReplacedByChequeId",
                table: "cheques");

            migrationBuilder.DropColumn(
                name: "ReplacesChequeId",
                table: "cheques");
        }
    }
}
