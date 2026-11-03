@echo off
setlocal

if not exist .env (
    echo Missing .env. Copy .env.example to .env and set DB_PASSWORD first.
    exit /b 1
)

docker compose up --build -d
if errorlevel 1 exit /b %errorlevel%

docker compose ps
echo.
echo Gateway: http://localhost:5000/swagger
echo Service health endpoints: /health on ports 5001, 5002, and 5003
endlocal
