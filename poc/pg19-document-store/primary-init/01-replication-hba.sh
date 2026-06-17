#!/bin/bash
# The official image's POSTGRES_HOST_AUTH_METHOD=trust writes `host all all all trust`, which
# does NOT cover replication connections — pg_hba matches the `replication` pseudo-database only
# via an entry that names it explicitly. The standby's pg_basebackup therefore needs this line.
# Runs once during the primary's initdb phase (/docker-entrypoint-initdb.d); the final server
# reads the appended pg_hba.conf on start. Throwaway-POC trust, never a real-world pattern.
set -e
cat >> "$PGDATA/pg_hba.conf" <<'EOF'
host replication all all trust
EOF
