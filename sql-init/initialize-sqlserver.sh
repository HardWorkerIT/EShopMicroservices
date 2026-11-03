#!/usr/bin/env bash
set -Eeuo pipefail

: "${SA_PASSWORD:?SA_PASSWORD must be set}"
: "${APP_DB_PASSWORD:?APP_DB_PASSWORD must be set}"
: "${MIGRATOR_DB_PASSWORD:?MIGRATOR_DB_PASSWORD must be set}"
: "${APP_LOGIN:?APP_LOGIN must be set}"
: "${MIGRATOR_LOGIN:?MIGRATOR_LOGIN must be set}"
: "${DATABASE_NAME:?DATABASE_NAME must be set}"

if [[ "$APP_LOGIN" == "$MIGRATOR_LOGIN" || "$APP_DB_PASSWORD" == "$MIGRATOR_DB_PASSWORD" ]]; then
    echo "Runtime and migration database credentials must be different." >&2
    exit 1
fi

/opt/mssql/bin/sqlservr &
sql_server_pid=$!
trap 'kill "$sql_server_pid" 2>/dev/null || true' EXIT

sqlcmd=(/opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "$SA_PASSWORD" -C -b)
ready=false
for attempt in {1..60}; do
    if "${sqlcmd[@]}" -d master -Q "SELECT 1" >/dev/null 2>&1; then
        ready=true
        break
    fi
    if ! kill -0 "$sql_server_pid" 2>/dev/null; then
        echo "SQL Server exited before becoming ready." >&2
        exit 1
    fi
    sleep 2
done

if [[ "$ready" != true ]]; then
    echo "SQL Server did not become ready within 120 seconds." >&2
    exit 1
fi

app_password_sql=$(printf '%s' "$APP_DB_PASSWORD" | sed "s/'/''/g")
migrator_password_sql=$(printf '%s' "$MIGRATOR_DB_PASSWORD" | sed "s/'/''/g")
"${sqlcmd[@]}" -d master -v APP_PASSWORD="$app_password_sql" -v MIGRATOR_PASSWORD="$migrator_password_sql" -Q "
IF DB_ID(N'${DATABASE_NAME}') IS NULL
    EXEC(N'CREATE DATABASE [${DATABASE_NAME}]');

IF SUSER_ID(N'${APP_LOGIN}') IS NULL
    CREATE LOGIN [${APP_LOGIN}] WITH PASSWORD = N'\$(APP_PASSWORD)', CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [${APP_LOGIN}] WITH PASSWORD = N'\$(APP_PASSWORD)', CHECK_POLICY = ON;

IF SUSER_ID(N'${MIGRATOR_LOGIN}') IS NULL
    CREATE LOGIN [${MIGRATOR_LOGIN}] WITH PASSWORD = N'\$(MIGRATOR_PASSWORD)', CHECK_POLICY = ON;
ELSE
    ALTER LOGIN [${MIGRATOR_LOGIN}] WITH PASSWORD = N'\$(MIGRATOR_PASSWORD)', CHECK_POLICY = ON;
"

"${sqlcmd[@]}" -d "$DATABASE_NAME" -Q "
IF DATABASE_PRINCIPAL_ID(N'${APP_LOGIN}') IS NULL
    CREATE USER [${APP_LOGIN}] FOR LOGIN [${APP_LOGIN}];

IF DATABASE_PRINCIPAL_ID(N'${MIGRATOR_LOGIN}') IS NULL
    CREATE USER [${MIGRATOR_LOGIN}] FOR LOGIN [${MIGRATOR_LOGIN}];

IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members
    WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'db_datareader')
      AND member_principal_id = DATABASE_PRINCIPAL_ID(N'${APP_LOGIN}')
)
    ALTER ROLE [db_datareader] ADD MEMBER [${APP_LOGIN}];

IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members
    WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'db_datawriter')
      AND member_principal_id = DATABASE_PRINCIPAL_ID(N'${APP_LOGIN}')
)
    ALTER ROLE [db_datawriter] ADD MEMBER [${APP_LOGIN}];

IF EXISTS (
    SELECT 1 FROM sys.database_role_members
    WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'db_ddladmin')
      AND member_principal_id = DATABASE_PRINCIPAL_ID(N'${APP_LOGIN}')
)
    ALTER ROLE [db_ddladmin] DROP MEMBER [${APP_LOGIN}];

IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members
    WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'db_datareader')
      AND member_principal_id = DATABASE_PRINCIPAL_ID(N'${MIGRATOR_LOGIN}')
)
    ALTER ROLE [db_datareader] ADD MEMBER [${MIGRATOR_LOGIN}];

IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members
    WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'db_datawriter')
      AND member_principal_id = DATABASE_PRINCIPAL_ID(N'${MIGRATOR_LOGIN}')
)
    ALTER ROLE [db_datawriter] ADD MEMBER [${MIGRATOR_LOGIN}];

IF NOT EXISTS (
    SELECT 1 FROM sys.database_role_members
    WHERE role_principal_id = DATABASE_PRINCIPAL_ID(N'db_ddladmin')
      AND member_principal_id = DATABASE_PRINCIPAL_ID(N'${MIGRATOR_LOGIN}')
)
    ALTER ROLE [db_ddladmin] ADD MEMBER [${MIGRATOR_LOGIN}];
"

wait "$sql_server_pid"
