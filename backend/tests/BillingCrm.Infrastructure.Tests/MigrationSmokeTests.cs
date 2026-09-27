using Microsoft.EntityFrameworkCore;

namespace BillingCrm.Infrastructure.Tests;

public class MigrationSmokeTests(LocalDbFixture database) : IClassFixture<LocalDbFixture>
{
    private async Task<int> CountAsync(string sql)
    {
        await using var context = database.CreateContext();
        return (await context.Database.SqlQueryRaw<int>(sql).ToListAsync()).Single();
    }

    [Fact]
    public async Task The_customers_table_exists()
    {
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'Customers'"));
    }

    [Fact]
    public async Task The_normalized_email_index_is_unique()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_Customers_NormalizedEmail' AND is_unique = 1"));
    }

    [Fact]
    public async Task The_type_check_constraint_exists()
    {
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = 'CK_Customers_Type'"));
    }

    [Fact]
    public async Task The_products_table_exists()
    {
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'Products'"));
    }

    [Fact]
    public async Task The_product_sku_index_is_unique_and_the_name_index_exists()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_Products_Sku' AND is_unique = 1"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_Products_Name'"));
    }

    [Theory]
    [InlineData("CK_Products_Unit")]
    [InlineData("CK_Products_UnitPrice")]
    [InlineData("CK_Products_VatPercent")]
    public async Task The_product_check_constraints_exist(string name)
    {
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = '{name}'"));
    }

    [Theory]
    [InlineData("UnitPrice", 18, 2)]
    [InlineData("VatPercent", 5, 2)]
    public async Task The_product_money_columns_are_exact_decimals(string column, int precision, int scale)
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Products' " +
            $"AND COLUMN_NAME = '{column}' AND DATA_TYPE = 'decimal' AND NUMERIC_PRECISION = {precision} AND NUMERIC_SCALE = {scale}"));
    }

    [Fact]
    public async Task Products_have_no_stock_or_quantity_column()
    {
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Products' " +
            "AND (COLUMN_NAME LIKE '%stock%' OR COLUMN_NAME LIKE '%quantity%' OR COLUMN_NAME LIKE '%inventory%')"));
    }

    [Fact]
    public async Task The_invoice_number_sequence_exists_from_1_to_999999_and_does_not_cycle()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.sequences WHERE name = 'InvoiceNumbers' " +
            "AND CAST(minimum_value AS bigint) = 1 AND CAST(maximum_value AS bigint) = 999999 " +
            "AND CAST(start_value AS bigint) = 1 AND CAST(increment AS bigint) = 1 AND is_cycling = 0"));
    }

    [Fact]
    public async Task The_invoice_number_sequence_is_not_cached_so_a_restart_cannot_skip_numbers()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.sequences WHERE name = 'InvoiceNumbers' AND is_cached = 0"));
    }

    [Theory]
    [InlineData("Invoices", "SentAtUtc", "datetime2")]
    [InlineData("Invoices", "CancelledAtUtc", "datetime2")]
    [InlineData("Invoices", "FrozenTotalNet", "decimal")]
    [InlineData("Invoices", "FrozenNetAfterDiscount", "decimal")]
    [InlineData("Invoices", "FrozenTotalVat", "decimal")]
    [InlineData("Invoices", "FrozenGrandTotal", "decimal")]
    [InlineData("InvoiceLines", "FrozenNet", "decimal")]
    [InlineData("InvoiceLines", "FrozenDiscountShare", "decimal")]
    [InlineData("InvoiceLines", "FrozenVat", "decimal")]
    public async Task The_status_columns_exist_are_nullable_and_frozen_amounts_are_decimal_18_2(string table, string column, string type)
    {
        var precision = type == "decimal" ? " AND NUMERIC_PRECISION = 18 AND NUMERIC_SCALE = 2" : string.Empty;
        Assert.Equal(1, await CountAsync(
            $"SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}' " +
            $"AND DATA_TYPE = '{type}' AND IS_NULLABLE = 'YES'{precision}"));
    }

    [Fact]
    public async Task The_invoice_row_version_is_a_rowversion_column()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'RowVersion' AND DATA_TYPE = 'timestamp'"));
    }

    [Fact]
    public async Task The_invoice_discount_is_an_exact_decimal_18_2_that_is_required_and_defaults_to_zero()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'Discount' " +
            "AND DATA_TYPE = 'decimal' AND NUMERIC_PRECISION = 18 AND NUMERIC_SCALE = 2 AND IS_NULLABLE = 'NO' " +
            "AND REPLACE(REPLACE(COLUMN_DEFAULT, '(', ''), ')', '') IN ('0', '0.0', '0.00')"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = 'CK_Invoices_Discount'"));
    }

    [Fact]
    public async Task The_only_amount_columns_on_invoices_and_lines_are_the_frozen_ones_written_when_an_invoice_leaves_Draft()
    {
        // Amounts are calculated while an invoice is a Draft. The only stored amounts are the Frozen* copies (spec 007) and the discount.
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'InvoiceLines' " +
            "AND (COLUMN_NAME LIKE '%Net%' OR COLUMN_NAME LIKE '%Vat' OR COLUMN_NAME LIKE '%Total%' OR COLUMN_NAME LIKE '%Amount%' " +
            "OR COLUMN_NAME LIKE '%Discount%') AND COLUMN_NAME <> 'VatPercent' AND COLUMN_NAME NOT LIKE 'Frozen%'"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' " +
            "AND (COLUMN_NAME LIKE '%Net%' OR COLUMN_NAME LIKE '%Vat%' OR COLUMN_NAME LIKE '%Total%' OR COLUMN_NAME LIKE '%Amount%') " +
            "AND COLUMN_NAME NOT LIKE 'Frozen%'"));
    }

    [Fact]
    public async Task The_invoice_tables_exist_and_the_number_index_is_unique()
    {
        Assert.Equal(2, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name IN ('Invoices', 'InvoiceLines')"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_Invoices_Number' AND is_unique = 1"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_Invoices_CreatedAtUtc_Number'"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_InvoiceLines_InvoiceId_Position' AND is_unique = 0"));
    }

    [Theory]
    [InlineData("CK_Invoices_Status")]
    [InlineData("CK_Invoices_StatusTimes")]
    [InlineData("CK_Invoices_FrozenAmounts")]
    [InlineData("CK_InvoiceLines_Unit")]
    [InlineData("CK_InvoiceLines_UnitPrice")]
    [InlineData("CK_InvoiceLines_VatPercent")]
    [InlineData("CK_InvoiceLines_Quantity")]
    public async Task The_invoice_check_constraints_exist(string name)
    {
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = '{name}'"));
    }

    [Theory]
    [InlineData("InvoiceLines", "UnitPrice", "decimal", 18, 2)]
    [InlineData("InvoiceLines", "VatPercent", "decimal", 5, 2)]
    [InlineData("InvoiceLines", "Quantity", "decimal", 9, 2)]
    public async Task The_invoice_line_numbers_are_exact_decimals(string table, string column, string type, int precision, int scale)
    {
        Assert.Equal(1, await CountAsync(
            $"SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{table}' " +
            $"AND COLUMN_NAME = '{column}' AND DATA_TYPE = '{type}' AND NUMERIC_PRECISION = {precision} AND NUMERIC_SCALE = {scale}"));
    }

    [Fact]
    public async Task The_due_date_is_a_date_and_the_snapshot_columns_exist()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Invoices' AND COLUMN_NAME = 'DueDate' AND DATA_TYPE = 'date'"));
        Assert.Equal(5, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'InvoiceLines' " +
            "AND COLUMN_NAME IN ('ProductName', 'ProductSku', 'Unit', 'UnitPrice', 'VatPercent')"));
    }

    [Fact]
    public async Task Invoices_have_no_live_total_amount_or_balance_column_only_the_frozen_copies_and_no_payment_columns()
    {
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME IN ('Invoices', 'InvoiceLines') " +
            "AND (COLUMN_NAME LIKE '%total%' OR COLUMN_NAME LIKE '%amount%') AND COLUMN_NAME NOT LIKE 'Frozen%'"));
        Assert.Equal(0, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME IN ('Invoices', 'InvoiceLines') " +
            "AND (COLUMN_NAME LIKE '%balance%' OR COLUMN_NAME LIKE '%paid%' OR COLUMN_NAME LIKE '%payment%' OR COLUMN_NAME LIKE '%overdue%')"));
    }

    [Fact]
    public async Task The_status_check_lists_exactly_the_four_stored_statuses_and_not_Overdue()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = 'CK_Invoices_Status' " +
            "AND definition LIKE '%Draft%' AND definition LIKE '%Sent%' AND definition LIKE '%Paid%' AND definition LIKE '%Cancelled%' " +
            "AND definition NOT LIKE '%Overdue%'"));
    }

    [Fact]
    public async Task The_payments_table_exists_with_exact_money_and_date_columns_and_no_cascade_to_it()
    {
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'Payments'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Payments' AND COLUMN_NAME = 'Amount' " +
            "AND DATA_TYPE = 'decimal' AND NUMERIC_PRECISION = 18 AND NUMERIC_SCALE = 2 AND IS_NULLABLE = 'NO'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Payments' AND COLUMN_NAME = 'PaidOn' AND DATA_TYPE = 'date'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Payments' AND COLUMN_NAME = 'Note' " +
            "AND DATA_TYPE = 'nvarchar' AND CHARACTER_MAXIMUM_LENGTH = 1000 AND IS_NULLABLE = 'YES'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE name = 'FK_Payments_Invoices_InvoiceId' AND delete_referential_action = 0"));
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_Payments_InvoiceId_RecordedAtUtc'"));
    }

    [Fact]
    public async Task The_payments_checks_require_a_positive_amount_within_the_ceiling_and_a_known_method()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = 'CK_Payments_Amount' " +
            "AND definition LIKE '%>(0)%' AND definition LIKE '%9999999999999.99%'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = 'CK_Payments_Method' " +
            "AND definition LIKE '%Cash%' AND definition LIKE '%Bank%' AND definition LIKE '%Other%'"));
    }

    // ---- invoice history (spec 010) ----

    [Fact]
    public async Task The_history_table_exists_with_the_expected_columns()
    {
        Assert.Equal(1, await CountAsync("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = 'InvoiceHistoryEntries'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID('InvoiceHistoryEntries') AND name = 'Sequence' " +
            "AND TYPE_NAME(user_type_id) = 'bigint' AND is_identity = 1"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'InvoiceHistoryEntries' AND COLUMN_NAME = 'OccurredAtUtc' " +
            "AND DATA_TYPE = 'datetime2' AND IS_NULLABLE = 'NO'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'InvoiceHistoryEntries' AND COLUMN_NAME = 'ActorEmail' " +
            "AND DATA_TYPE = 'nvarchar' AND CHARACTER_MAXIMUM_LENGTH = 256 AND IS_NULLABLE = 'NO'"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'InvoiceHistoryEntries' AND COLUMN_NAME = 'ChangesJson' " +
            "AND DATA_TYPE = 'nvarchar' AND CHARACTER_MAXIMUM_LENGTH = -1 AND IS_NULLABLE = 'NO'"));
        Assert.Equal(4, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'InvoiceHistoryEntries' " +
            "AND COLUMN_NAME IN ('Id', 'InvoiceId', 'Kind', 'ActorRole') AND IS_NULLABLE = 'NO'"));
    }

    [Theory]
    [InlineData("CK_InvoiceHistoryEntries_Kind")]
    [InlineData("CK_InvoiceHistoryEntries_ActorRole")]
    [InlineData("CK_InvoiceHistoryEntries_ChangesJson")]
    public async Task The_history_check_constraints_exist(string name)
    {
        Assert.Equal(1, await CountAsync($"SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = '{name}'"));
    }

    [Fact]
    public async Task The_history_kind_check_lists_exactly_the_five_kinds()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.check_constraints WHERE name = 'CK_InvoiceHistoryEntries_Kind' " +
            "AND definition LIKE '%Created%' AND definition LIKE '%DraftEdited%' AND definition LIKE '%Sent%' " +
            "AND definition LIKE '%Payment%' AND definition LIKE '%Cancelled%'"));
    }

    [Fact]
    public async Task The_history_indexes_exist_and_the_sequence_index_is_unique()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_InvoiceHistoryEntries_Sequence' AND is_unique = 1"));
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.indexes WHERE name = 'IX_InvoiceHistoryEntries_InvoiceId_Sequence'"));
    }

    [Fact]
    public async Task The_history_foreign_key_to_invoices_has_no_cascade()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID('InvoiceHistoryEntries') " +
            "AND referenced_object_id = OBJECT_ID('Invoices') AND delete_referential_action = 0 AND update_referential_action = 0"));
    }

    [Fact]
    public async Task The_history_immutability_trigger_exists_is_enabled_and_covers_update_and_delete()
    {
        Assert.Equal(1, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.triggers WHERE name = 'TR_InvoiceHistoryEntries_Immutable' " +
            "AND parent_id = OBJECT_ID('InvoiceHistoryEntries') AND is_instead_of_trigger = 1 AND is_disabled = 0"));
        Assert.Equal(2, await CountAsync(
            "SELECT COUNT(*) AS [Value] FROM sys.trigger_events e JOIN sys.triggers t ON t.object_id = e.object_id " +
            "WHERE t.name = 'TR_InvoiceHistoryEntries_Immutable' AND e.type_desc IN ('UPDATE', 'DELETE')"));
    }

    [Fact]
    public async Task The_database_collation_is_case_insensitive_which_search_relies_on()
    {
        await using var context = database.CreateContext();
        var collation = (await context.Database
            .SqlQueryRaw<string>("SELECT CAST(DATABASEPROPERTYEX(DB_NAME(), 'Collation') AS nvarchar(100)) AS [Value]")
            .ToListAsync()).Single();

        Assert.Contains("_CI_", collation);
    }
}
