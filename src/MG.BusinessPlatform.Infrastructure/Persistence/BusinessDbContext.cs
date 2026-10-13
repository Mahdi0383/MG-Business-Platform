using Microsoft.EntityFrameworkCore;

namespace MG.BusinessPlatform.Infrastructure.Persistence;

public sealed class LedgerAccountRecord
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string AccountType { get; set; } = string.Empty;
    public bool IsPostingAllowed { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public sealed class SalesInvoiceRecord
{
    public Guid Id { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public Guid OrderId { get; set; }
    public DateOnly IssueDate { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "IRR";
    public decimal TotalAmount { get; set; }
    public decimal CostTotal { get; set; }
    public decimal PaidAmount { get; set; }
    public string Status { get; set; } = "Issued";
    public DateTimeOffset CreatedAt { get; set; }
    public List<SalesInvoiceLineRecord> Lines { get; set; } = [];
}

public sealed class SalesInvoiceLineRecord
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public SalesInvoiceRecord? Invoice { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public decimal LineTotal { get; set; }
    public decimal CostTotal { get; set; }
}

public sealed class JournalEntryRecord
{
    public Guid Id { get; set; }
    public string EntryNumber { get; set; } = string.Empty;
    public DateOnly EntryDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CurrencyCode { get; set; } = "IRR";
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<JournalLineRecord> Lines { get; set; } = [];
}

public sealed class JournalLineRecord
{
    public Guid Id { get; set; }
    public Guid JournalEntryId { get; set; }
    public JournalEntryRecord? JournalEntry { get; set; }
    public Guid AccountId { get; set; }
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Side { get; set; } = string.Empty;
    public decimal Amount { get; set; }
}

public sealed class PaymentRecord
{
    public Guid Id { get; set; }
    public string PaymentNumber { get; set; } = string.Empty;
    public Guid InvoiceId { get; set; }
    public Guid JournalEntryId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string Method { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string CurrencyCode { get; set; } = "IRR";
    public string Reference { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class StoreProduct
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public string CurrencyCode { get; set; } = "IRR";
    public bool IsAvailable { get; set; } = true;
    public int StockQuantity { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class CustomerRecord
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SupplierRecord
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TaxIdentifier { get; set; } = string.Empty;
    public string PaymentTerms { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class StoreSettings
{
    public int Id { get; set; } = 1;
    public string StoreName { get; set; } = "فروشگاه شما";
    public string CurrencyCode { get; set; } = "IRR";
    public bool OnlineOrderingEnabled { get; set; }
    public bool PublicPublicationEnabled { get; set; }
    public string PublicHostname { get; set; } = string.Empty;
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

public sealed class StockMovementRecord
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string MovementType { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int SignedQuantity { get; set; }
    public decimal UnitCost { get; set; }
    public string CurrencyCode { get; set; } = "IRR";
    public string Reference { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

public sealed class CustomerOrderLine
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public CustomerOrder? Order { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public decimal UnitCost { get; set; }
    public int Quantity { get; set; }
    public decimal LineTotal { get; set; }
}

public sealed class BusinessDbContext(DbContextOptions<BusinessDbContext> options) : DbContext(options)
{
    public DbSet<StoreProduct> StoreProducts => Set<StoreProduct>();
    public DbSet<LedgerAccountRecord> LedgerAccounts => Set<LedgerAccountRecord>();
    public DbSet<SalesInvoiceRecord> SalesInvoices => Set<SalesInvoiceRecord>();
    public DbSet<SalesInvoiceLineRecord> SalesInvoiceLines => Set<SalesInvoiceLineRecord>();
    public DbSet<JournalEntryRecord> JournalEntries => Set<JournalEntryRecord>();
    public DbSet<JournalLineRecord> JournalLines => Set<JournalLineRecord>();
    public DbSet<PaymentRecord> Payments => Set<PaymentRecord>();
    public DbSet<CustomerRecord> Customers => Set<CustomerRecord>();
    public DbSet<SupplierRecord> Suppliers => Set<SupplierRecord>();
    public DbSet<StoreSettings> StoreSettings => Set<StoreSettings>();
    public DbSet<CustomerOrder> CustomerOrders => Set<CustomerOrder>();
    public DbSet<CustomerOrderLine> CustomerOrderLines => Set<CustomerOrderLine>();
    public DbSet<StockMovementRecord> StockMovements => Set<StockMovementRecord>();

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
            entity.ToTable(t => t.HasCheckConstraint("CK_StoreProduct_Price", "CAST(UnitPrice AS NUMERIC) >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_StoreProduct_Cost", "CAST(UnitCost AS NUMERIC) >= 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_StoreProduct_Stock", "StockQuantity >= 0"));
        });

        modelBuilder.Entity<LedgerAccountRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Code).HasMaxLength(20).IsRequired();
            entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
            entity.Property(x => x.AccountType).HasMaxLength(20).IsRequired();
            entity.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<SalesInvoiceRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.InvoiceNumber).HasMaxLength(40).IsRequired();
            entity.Property(x => x.CustomerName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.CustomerPhone).HasMaxLength(40).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(30).IsRequired();
            entity.HasIndex(x => x.InvoiceNumber).IsUnique();
            entity.HasIndex(x => x.OrderId).IsUnique();
            entity.ToTable(t => t.HasCheckConstraint("CK_SalesInvoice_Amounts", "CAST(TotalAmount AS NUMERIC) >= 0 AND CAST(CostTotal AS NUMERIC) >= 0 AND CAST(PaidAmount AS NUMERIC) >= 0 AND CAST(PaidAmount AS NUMERIC) <= CAST(TotalAmount AS NUMERIC)"));
            entity.HasMany(x => x.Lines).WithOne(x => x.Invoice).HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<CustomerOrder>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SalesInvoiceLineRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            entity.ToTable(t => t.HasCheckConstraint("CK_SalesInvoiceLine_Quantity", "Quantity > 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_SalesInvoiceLine_Amounts", "CAST(UnitPrice AS NUMERIC) >= 0 AND CAST(UnitCost AS NUMERIC) >= 0 AND CAST(LineTotal AS NUMERIC) >= 0 AND CAST(CostTotal AS NUMERIC) >= 0"));
        });

        modelBuilder.Entity<JournalEntryRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.EntryNumber).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.SourceType).HasMaxLength(40).IsRequired();
            entity.HasIndex(x => x.EntryNumber).IsUnique();
            entity.HasIndex(x => new { x.SourceType, x.SourceId }).IsUnique();
            entity.HasMany(x => x.Lines).WithOne(x => x.JournalEntry).HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JournalLineRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.AccountCode).HasMaxLength(20).IsRequired();
            entity.Property(x => x.AccountName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Side).HasMaxLength(10).IsRequired();
            entity.HasIndex(x => x.AccountId);
            entity.ToTable(t => t.HasCheckConstraint("CK_JournalLine_Side", "Side IN ('Debit','Credit')"));
            entity.ToTable(t => t.HasCheckConstraint("CK_JournalLine_Amount", "CAST(Amount AS NUMERIC) > 0"));
            entity.HasOne<LedgerAccountRecord>().WithMany().HasForeignKey(x => x.AccountId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PaymentNumber).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Method).HasMaxLength(30).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Reference).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Notes).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => x.PaymentNumber).IsUnique();
            entity.HasIndex(x => x.InvoiceId);
            entity.HasOne<SalesInvoiceRecord>().WithMany().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<JournalEntryRecord>().WithMany().HasForeignKey(x => x.JournalEntryId).OnDelete(DeleteBehavior.Restrict);
            entity.ToTable(t => t.HasCheckConstraint("CK_Payment_Amount", "CAST(Amount AS NUMERIC) > 0"));
        });

        modelBuilder.Entity<CustomerRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(1000).IsRequired();
            entity.HasIndex(x => x.DisplayName);
            entity.HasIndex(x => x.Phone);
        });

        modelBuilder.Entity<SupplierRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.Address).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.TaxIdentifier).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PaymentTerms).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => x.DisplayName);
            entity.HasIndex(x => x.Phone);
        });

        modelBuilder.Entity<StoreSettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.StoreName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.PublicHostname).HasMaxLength(253).IsRequired();
            entity.ToTable(t => t.HasCheckConstraint("CK_StoreSettings_Singleton", "Id = 1"));
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
            entity.ToTable(t => t.HasCheckConstraint("CK_CustomerOrder_Total", "CAST(TotalAmount AS NUMERIC) >= 0"));
            entity.HasMany(x => x.Lines).WithOne(x => x.Order).HasForeignKey(x => x.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StockMovementRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MovementType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.CurrencyCode).HasMaxLength(3).IsRequired();
            entity.Property(x => x.Reference).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Note).HasMaxLength(500).IsRequired();
            entity.HasIndex(x => new { x.ProductId, x.OccurredAt });
            entity.ToTable(t => t.HasCheckConstraint("CK_StockMovement_Quantity", "Quantity > 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_StockMovement_SignedQuantity", "SignedQuantity = Quantity OR SignedQuantity = -Quantity"));
            entity.ToTable(t => t.HasCheckConstraint("CK_StockMovement_UnitCost", "CAST(UnitCost AS NUMERIC) >= 0"));
        });

        modelBuilder.Entity<CustomerOrderLine>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderLine_Quantity", "Quantity > 0"));
            entity.ToTable(t => t.HasCheckConstraint("CK_OrderLine_Price", "CAST(UnitPrice AS NUMERIC) >= 0 AND CAST(UnitCost AS NUMERIC) >= 0 AND CAST(LineTotal AS NUMERIC) >= 0"));
        });
    }
}
