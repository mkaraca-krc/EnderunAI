using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnderunAI.Api.Migrations
{
    /// <inheritdoc />
    public partial class SyncPriceDifferenceBoqReservationModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill/sync migration: every table this migration would
            // otherwise generate (price_difference_*, project_boq*,
            // stock_reservations, rfq*, purchase_order*, goods_receipt*)
            // was already created against this database by earlier
            // migrations (AddPriceDifferenceCore, AddProjectBoqCore,
            // AddRfqModule, AddPurchaseOrderModule, AddGoodsReceiptModule,
            // AddCurrentAccountAccountingLinks) whose EF model classes were
            // never added to AppDbContext, so the model snapshot never knew
            // about them. This migration exists only to bring the snapshot
            // back in sync with the actual schema; it must not attempt to
            // recreate tables that already exist, so it is intentionally a
            // no-op.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty — see Up().
        }
    }
}
