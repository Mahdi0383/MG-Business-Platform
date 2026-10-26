# Product CSV import/export

The Products tab supports UTF-8 CSV import/export. Export columns are `SKU,Name,Description,UnitPrice,UnitCost,CurrencyCode,StockQuantity,IsAvailable`.

Required columns: SKU (unique, max 80 characters), Name (max 200 characters), UnitPrice (non-negative decimal using a dot as decimal separator). Optional columns: Description (max 2000 characters), UnitCost (defaults to 0), CurrencyCode (defaults to IRR), StockQuantity (defaults to 0), and IsAvailable (true/false, 1/0, or Persian فعال/غیرفعال; defaults to active). Persian column-header aliases are supported.

Quoted fields and escaped double quotes are supported, including commas inside quoted values. Import validates the complete file before writing. If any row is invalid, nothing is imported. Existing SKUs are skipped case-insensitively and never overwritten; duplicate SKUs within the file are errors. New products and opening-stock movements are committed in one database transaction. Imported opening stock is recorded as an `OpeningBalance` movement with unit cost.

Safety limits: 10 MB per import, 10,000 data rows per import, and 100,000 products per export. The export is UTF-8 with BOM, quotes fields and neutralizes formula-like prefixes for spreadsheet safety. CSV is a product-catalog transfer format only; it does not migrate invoices, payments, journals, customers, suppliers, game sessions, or historical stock movements.