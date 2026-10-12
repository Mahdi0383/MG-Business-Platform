using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

public sealed class StoreProduct
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public string CurrencyCode { get; set; } = "IRR";
    public bool IsAvailable { get; set; } = true;
    public int StockQuantity { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CustomerOrder
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string DeliveryAddress { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Status { get; set; } = "Received";
    public string CurrencyCode { get; set; } = "IRR";
    public decimal TotalAmount { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<CustomerOrderLine> Lines { get; set; } = [];
}

public sealed class CustomerOrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public CustomerOrder? Order { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class BusinessDbContext(DbContextOptions<BusinessDbContext> options) : DbContext(options)
{
    public DbSet<StoreProduct> StoreProducts => Set<StoreProduct>();
    public DbSet<CustomerOrder> CustomerOrders => Set<CustomerOrder>();
    public DbSet<CustomerOrderLine> CustomerOrderLines => Set<CustomerOrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoreProduct>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.Sku).IsUnique();
            entity.Property(x => x.Sku).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.ToTable(t => t.HasCheckConstraint("CK_StoreProduct_Price", "UnitPrice >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_StoreProduct_Stock", "StockQuantity >= 0"));
        });

        modelBuilder.Entity<CustomerOrder>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.OrderNumber).IsUnique();
            entity.HasIndex(x => x.IdempotencyKey).IsUnique();
            entity.Property(x => x.OrderNumber).HasMaxLength(40).IsRequired();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(100).IsRequired();
            entity.Property(x => x.CustomerName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.CustomerPhone).HasMaxLength(40).IsRequired();
            entity.Property(x => x.DeliveryAddress).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(1000);
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.ToTable(t => t.HasCheckConstraint("CK_CustomerOrder_Total", "TotalAmount >= 0"));
            entity.HasMany(x => x.Lines).WithOne(x => x.Order).HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CustomerOrderLine>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderLine_Quantity", "Quantity > 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderLine_Price", "UnitPrice >= 0 AND LineTotal >= 0"));
        });
    }
}
