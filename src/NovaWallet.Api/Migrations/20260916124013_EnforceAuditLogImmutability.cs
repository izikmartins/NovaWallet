using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaWallet.Api.Migrations
{
    public partial class EnforceAuditLogImmutability : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_audit_log_mutation()
                RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'Audit logs are immutable and cannot be updated or deleted.';
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_prevent_audit_log_mutation
                BEFORE UPDATE OR DELETE ON "AuditLogs"
                FOR EACH ROW
                EXECUTE FUNCTION prevent_audit_log_mutation();
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_prevent_audit_log_mutation ON "AuditLogs";
                DROP FUNCTION IF EXISTS prevent_audit_log_mutation();
                """);
        }
    }
}