using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gateway.Infrastructure.Migrations
{
    public partial class AuditAppendOnly_Triggers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION audit_append_only()
RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'Audit tables are append-only';
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_no_update_delete_auditevents ON ""AuditEvents"";
CREATE TRIGGER trg_no_update_delete_auditevents
BEFORE UPDATE OR DELETE ON ""AuditEvents""
FOR EACH ROW EXECUTE FUNCTION audit_append_only();

DROP TRIGGER IF EXISTS trg_no_update_delete_auditeventhashes ON ""AuditEventHashes"";
CREATE TRIGGER trg_no_update_delete_auditeventhashes
BEFORE UPDATE OR DELETE ON ""AuditEventHashes""
FOR EACH ROW EXECUTE FUNCTION audit_append_only();

");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS trg_no_update_delete_auditevents ON ""AuditEvents"";
DROP TRIGGER IF EXISTS trg_no_update_delete_auditeventhashes ON ""AuditEventHashes"";
DROP FUNCTION IF EXISTS audit_append_only();

");
        }
    }
}
