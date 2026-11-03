if (-not (Test-Path -LiteralPath '.env')) {
    Write-Error 'Missing .env. Copy .env.example to .env and set DB_PASSWORD first.'
    exit 1
}

# Database creation is handled by each service at startup. Compose waits for
# SQL Server health checks before it starts the APIs.
docker compose up --build -d
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

docker compose ps
