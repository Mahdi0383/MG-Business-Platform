using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using MG.BusinessPlatform.Infrastructure.Persistence;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261117080000_AddCustomerOrderBranchOwnership")]
public sealed class AddCustomerOrderBranchOwnership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "BusinessId",
            table: "CustomerOrders",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "BranchId",
            table: "CustomerOrders",
            type: "TEXT",
            nullable: true);

        migrationBuilder.Sql("""
            UPDATE "CustomerOrders"
            SET "BusinessId" = (SELECT "BusinessId" FROM "StoreSettings" WHERE "Id" = 1),
                "BranchId" = (SELECT "DefaultBranchId" FROM "StoreSettings" WHERE "Id" = 1)
            WHERE "BusinessId" IS NULL OR "BranchId" IS NULL;
            """);

        migrationBuilder.Sql("""
            CREATE TEMP TABLE "__CustomerOrderOwnershipPreflight"
            (
                "Ok" INTEGER NOT NULL CHECK ("Ok" = 1)
            );

            INSERT INTO "__CustomerOrderOwnershipPreflight" ("Ok")
            SELECT CASE WHEN NOT EXISTS
            (
                SELECT 1
                FROM "CustomerOrders" AS customerOrder
                WHERE customerOrder."BusinessId" IS NULL
                   OR customerOrder."BranchId" IS NULL
                   OR NOT EXISTS
                   (
                       SELECT 1
                       FROM "Branches" AS branch
                       WHERE branch."BusinessId" = customerOrder."BusinessId"
                         AND branch."Id" = customerOrder."BranchId"
                   )
            ) THEN 1 ELSE 0 END;

            DROP TABLE "__CustomerOrderOwnershipPreflight";
            """);

        migrationBuilder.DropIndex(
            name: "IX_CustomerOrders_OrderNumber",
            table: "CustomerOrders");

        migrationBuilder.DropIndex(
            name: "IX_CustomerOrders_IdempotencyKey",
            table: "CustomerOrders");

        migrationBuilder.CreateIndex(
            name: "IX_CustomerOrders_BusinessId_BranchId",
            table: "CustomerOrders",
            columns: new[] { "BusinessId", "BranchId" });

        migrationBuilder.CreateIndex(
            name: "IX_CustomerOrders_BusinessId_OrderNumber",
            table: "CustomerOrders",
            columns: new[] { "BusinessId", "OrderNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CustomerOrders_BusinessId_IdempotencyKey",
            table: "CustomerOrders",
            columns: new[] { "BusinessId", "IdempotencyKey" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_CustomerOrders_Branches_BusinessId_BranchId",
            table: "CustomerOrders",
            columns: new[] { "BusinessId", "BranchId" },
            principalTable: "Branches",
            principalColumns: new[] { "BusinessId", "Id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.Sql("""
            CREATE TRIGGER "TR_CustomerOrders_Insert_Ownership"
            BEFORE INSERT ON "CustomerOrders"
            WHEN NEW."BusinessId" IS NULL
              OR NEW."BranchId" IS NULL
              OR NOT EXISTS
              (
                  SELECT 1 FROM "Branches" AS branch
                  WHERE branch."BusinessId" = NEW."BusinessId"
                    AND branch."Id" = NEW."BranchId"
                    AND branch."IsActive" = 1
              )
            BEGIN
                SELECT RAISE(ABORT, 'Customer order business and active branch ownership is required');
            END;

            CREATE TRIGGER "TR_CustomerOrders_Update_Ownership"
            BEFORE UPDATE OF "BusinessId", "BranchId" ON "CustomerOrders"
            WHEN NEW."BusinessId" IS NULL
              OR NEW."BranchId" IS NULL
              OR NOT EXISTS
              (
                  SELECT 1 FROM "Branches" AS branch
                  WHERE branch."BusinessId" = NEW."BusinessId"
                    AND branch."Id" = NEW."BranchId"
              )
            BEGIN
                SELECT RAISE(ABORT, 'Customer order business and branch ownership is invalid');
            END;

            CREATE TRIGGER "TR_CustomerOrderLines_Insert_ProductOwnership"
            BEFORE INSERT ON "CustomerOrderLines"
            WHEN NEW."ProductId" <> '00000000-0000-0000-0000-000000000000'
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM "CustomerOrders" AS customerOrder
                  JOIN "StoreProducts" AS product ON product."Id" = NEW."ProductId"
                  WHERE customerOrder."Id" = NEW."OrderId"
                    AND product."BusinessId" = customerOrder."BusinessId"
              )
            BEGIN
                SELECT RAISE(ABORT, 'Customer order line product must belong to the order business');
            END;

            CREATE TRIGGER "TR_CustomerOrderLines_Update_ProductOwnership"
            BEFORE UPDATE OF "OrderId", "ProductId" ON "CustomerOrderLines"
            WHEN NEW."ProductId" <> '00000000-0000-0000-0000-000000000000'
              AND NOT EXISTS
              (
                  SELECT 1
                  FROM "CustomerOrders" AS customerOrder
                  JOIN "StoreProducts" AS product ON product."Id" = NEW."ProductId"
                  WHERE customerOrder."Id" = NEW."OrderId"
                    AND product."BusinessId" = customerOrder."BusinessId"
              )
            BEGIN
                SELECT RAISE(ABORT, 'Customer order line product must belong to the order business');
            END;
            """);
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder) =>
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS "TR_CustomerOrderLines_Update_ProductOwnership";
            DROP TRIGGER IF EXISTS "TR_CustomerOrderLines_Insert_ProductOwnership";
            DROP TRIGGER IF EXISTS "TR_CustomerOrders_Update_Ownership";
            DROP TRIGGER IF EXISTS "TR_CustomerOrders_Insert_Ownership";
            """);

        migrationBuilder.DropForeignKey(
            name: "FK_CustomerOrders_Branches_BusinessId_BranchId",
            table: "CustomerOrders");

        migrationBuilder.DropIndex(
            name: "IX_CustomerOrders_BusinessId_BranchId",
            table: "CustomerOrders");

        migrationBuilder.DropIndex(
            name: "IX_CustomerOrders_BusinessId_OrderNumber",
            table: "CustomerOrders");

        migrationBuilder.DropIndex(
            name: "IX_CustomerOrders_BusinessId_IdempotencyKey",
            table: "CustomerOrders");

        migrationBuilder.CreateIndex(
            name: "IX_CustomerOrders_OrderNumber",
            table: "CustomerOrders",
            column: "OrderNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_CustomerOrders_IdempotencyKey",
            table: "CustomerOrders",
            column: "IdempotencyKey",
            unique: true);

        migrationBuilder.DropColumn(
            name: "BusinessId",
            table: "CustomerOrders");

        migrationBuilder.DropColumn(
            name: "BranchId",
            table: "CustomerOrders");
    }
}
