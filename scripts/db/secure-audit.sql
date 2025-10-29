-- Secure audit tables by revoking UPDATE/DELETE from the application role and allowing only INSERT
-- Replace :APP_ROLE with your application database role (e.g., app_user)

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = current_user) THEN
    RAISE NOTICE 'Run this script as a DB superuser or admin';
  END IF;
END$$;

-- Revoke broad privileges and grant least-privilege
REVOKE UPDATE, DELETE ON TABLE "AuditEvents" FROM :APP_ROLE;
REVOKE UPDATE, DELETE ON TABLE "AuditEventHashes" FROM :APP_ROLE;
REVOKE UPDATE, DELETE ON TABLE "AuditVerificationRuns" FROM :APP_ROLE;

GRANT INSERT, SELECT ON TABLE "AuditEvents" TO :APP_ROLE;
GRANT INSERT, SELECT ON TABLE "AuditEventHashes" TO :APP_ROLE;
GRANT INSERT, SELECT ON TABLE "AuditVerificationRuns" TO :APP_ROLE;

-- Optional: Revoke TRIGGER privilege so the role cannot disable append-only triggers
REVOKE TRIGGER ON TABLE "AuditEvents" FROM :APP_ROLE;
REVOKE TRIGGER ON TABLE "AuditEventHashes" FROM :APP_ROLE;

-- Verify
-- \dp+ "AuditEvents"

