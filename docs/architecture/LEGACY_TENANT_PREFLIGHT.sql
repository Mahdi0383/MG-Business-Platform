-- MG Business Platform legacy tenant-migration preflight
-- READ-ONLY diagnostic script. Run against a COPY of the SQLite database.
-- It intentionally performs no INSERT, UPDATE, DELETE, DDL, VACUUM or migration.
-- Any non-"ok" integrity result or unresolved ownership requires investigation
-- before the tenant migration is allowed to run.

PRAGMA query_only = ON;
PRAGMA foreign_keys = ON;

-- 1. SQLite structural integrity and existing FK violations.
PRAGMA integrity_check;
PRAGMA foreign_key_check;

-- 2. Actual persisted tables and columns that can carry branch ownership.
SELECT name AS table_name
FROM sqlite_schema
WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
ORDER BY name;

SELECT m.name AS table_name, c.name AS ownership_column, c.type AS declared_type,
       c."notnull" AS is_not_null
FROM sqlite_schema AS m
JOIN pragma_table_info(m.name) AS c
WHERE m.type = 'table'
  AND m.name NOT LIKE 'sqlite_%'
  AND c.name IN ('BusinessId', 'BranchId')
ORDER BY m.name, c.name;

-- 3. BranchId population. These are the three branch-bearing tables in the
-- inspected legacy EF model; values are not assumed to identify valid branches.
SELECT 'GameSessions' AS table_name, BranchId, COUNT(*) AS row_count
FROM GameSessions GROUP BY BranchId
UNION ALL
SELECT 'ReservationResources', BranchId, COUNT(*)
FROM ReservationResources GROUP BY BranchId
UNION ALL
SELECT 'PurchaseInvoices', BranchId, COUNT(*)
FROM PurchaseInvoices GROUP BY BranchId
ORDER BY table_name, BranchId;

-- 4. Row-count baseline for operational and financial records.
-- Preserve this output and compare it after a migration on a disposable copy.
SELECT 'StoreProducts' AS table_name, COUNT(*) AS row_count FROM StoreProducts
UNION ALL SELECT 'Customers', COUNT(*) FROM Customers
UNION ALL SELECT 'Suppliers', COUNT(*) FROM Suppliers
UNION ALL SELECT 'CustomerOrders', COUNT(*) FROM CustomerOrders
UNION ALL SELECT 'CustomerOrderLines', COUNT(*) FROM CustomerOrderLines
UNION ALL SELECT 'StockMovements', COUNT(*) FROM StockMovements
UNION ALL SELECT 'SalesInvoices', COUNT(*) FROM SalesInvoices
UNION ALL SELECT 'SalesInvoiceLines', COUNT(*) FROM SalesInvoiceLines
UNION ALL SELECT 'Payments', COUNT(*) FROM Payments
UNION ALL SELECT 'JournalEntries', COUNT(*) FROM JournalEntries
UNION ALL SELECT 'JournalLines', COUNT(*) FROM JournalLines
UNION ALL SELECT 'PurchaseInvoices', COUNT(*) FROM PurchaseInvoices
UNION ALL SELECT 'PurchaseInvoiceLines', COUNT(*) FROM PurchaseInvoiceLines
UNION ALL SELECT 'SupplierPayments', COUNT(*) FROM SupplierPayments
UNION ALL SELECT 'GameSessions', COUNT(*) FROM GameSessions
UNION ALL SELECT 'GameSessionPlayers', COUNT(*) FROM GameSessionPlayers
UNION ALL SELECT 'ReservationResources', COUNT(*) FROM ReservationResources
UNION ALL SELECT 'CustomerReservations', COUNT(*) FROM CustomerReservations
UNION ALL SELECT 'AuditEvents', COUNT(*) FROM AuditEvents
ORDER BY table_name;

-- 5. Financial and stock control totals. Decimal values are stored as TEXT
-- by EF Core SQLite; CAST to NUMERIC is used for diagnostic aggregation only.
SELECT CurrencyCode, COUNT(*) AS invoice_count,
       SUM(CAST(TotalAmount AS NUMERIC)) AS invoice_total,
       SUM(CAST(PaidAmount AS NUMERIC)) AS paid_total
FROM SalesInvoices GROUP BY CurrencyCode ORDER BY CurrencyCode;

SELECT CurrencyCode, COUNT(*) AS payment_count,
       SUM(CAST(Amount AS NUMERIC)) AS payment_total
FROM Payments GROUP BY CurrencyCode ORDER BY CurrencyCode;

SELECT e.CurrencyCode, COUNT(*) AS journal_line_count,
       SUM(CASE WHEN l.Side = 'Debit' THEN CAST(l.Amount AS NUMERIC) ELSE 0 END) AS debit_total,
       SUM(CASE WHEN l.Side = 'Credit' THEN CAST(l.Amount AS NUMERIC) ELSE 0 END) AS credit_total
FROM JournalLines AS l
JOIN JournalEntries AS e ON e.Id = l.JournalEntryId
GROUP BY e.CurrencyCode ORDER BY e.CurrencyCode;

SELECT CurrencyCode, COUNT(*) AS product_count,
       SUM(StockQuantity) AS current_stock_units
FROM StoreProducts GROUP BY CurrencyCode ORDER BY CurrencyCode;

SELECT CurrencyCode, COUNT(*) AS movement_count,
       SUM(SignedQuantity) AS net_movement_units
FROM StockMovements GROUP BY CurrencyCode ORDER BY CurrencyCode;

-- 6. Known ownership ambiguity: these IDs have no persisted Branch table/FK
-- in the inspected baseline. This query lists every distinct referenced ID.
SELECT 'GameSessions' AS source_table, BranchId FROM GameSessions
UNION
SELECT 'ReservationResources', BranchId FROM ReservationResources
UNION
SELECT 'PurchaseInvoices', BranchId FROM PurchaseInvoices
ORDER BY source_table, BranchId;
