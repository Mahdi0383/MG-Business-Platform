using MG.BusinessPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MG.BusinessPlatform.Infrastructure.Persistence.Migrations;

[DbContext(typeof(BusinessDbContext))]
[Migration("20261013000000_InitialStorefrontSchema")]
public sealed class InitialStorefrontSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "StoreSettings",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                StoreName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                OnlineOrderingEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                PublicPublicationEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                PublicHostname = table.Column<string>(type: "TEXT", maxLength: 253, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StoreSettings", x => x.Id);
                table.CheckConstraint("CK_StoreSettings_Singleton", "Id = 1");
            });

        migrationBuilder.CreateTable(
            name: "StoreProducts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Sku = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                IsAvailable = table.Column<bool>(type: "INTEGER", nullable: false),
                StockQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StoreProducts", x => x.Id);
                table.CheckConstraint("CK_StoreProduct_Price", "CAST(UnitPrice AS NUMERIC) >= 0");
                table.CheckConstraint("CK_StoreProduct_Stock", "StockQuantity >= 0");
            });

        migrationBuilder.CreateTable(
            name: "CustomerOrders",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OrderNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                CustomerName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                CustomerPhone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                DeliveryAddress = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                TotalAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerOrders", x => x.Id);
                table.CheckConstraint("CK_CustomerOrder_Total", "CAST(TotalAmount AS NUMERIC) >= 0");
            });

        migrationBuilder.CreateTable(
            name: "CustomerOrderLines",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                OrderId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductId = table.Column<Guid>(type: "TEXT", nullable: false),
                ProductName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                UnitPrice = table.Column<decimal>(type: "TEXT", nullable: false),
                Quantity = table.Column<int>(type: "INTEGER", nullable: false),
                LineTotal = table.Column<decimal>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CustomerOrderLines", x => x.Id);
                table.CheckConstraint("CK_OrderLine_Price", "CAST(UnitPrice AS NUMERIC) >= 0 AND CAST(LineTotal AS NUMERIC) >= 0");
                table.CheckConstraint("CK_OrderLine_Quantity", "Quantity > 0");
                table.ForeignKey(
                    name: "FK_CustomerOrderLines_CustomerOrders_OrderId",
                    column: x => x.OrderId,
                    principalTable: "CustomerOrders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_StoreProducts_Sku", table: "StoreProducts", column: "Sku", unique: true);
        migrationBuilder.CreateIndex(name: "IX_CustomerOrders_OrderNumber", table: "CustomerOrders", column: "OrderNumber", unique: true);
        migrationBuilder.CreateIndex(name: "IX_CustomerOrders_IdempotencyKey", table: "CustomerOrders", column: "IdempotencyKey", unique: true);
        migrationBuilder.CreateIndex(name: "IX_CustomerOrderLines_OrderId", table: "CustomerOrderLines", column: "OrderId");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        BusinessDbContextModelSnapshot.BuildModelCore(modelBuilder);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "CustomerOrderLines");
        migrationBuilder.DropTable(name: "StoreProducts");
        migrationBuilder.DropTable(name: "StoreSettings");
        migrationBuilder.DropTable(name: "CustomerOrders");
    }
}
