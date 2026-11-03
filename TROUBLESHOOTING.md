# Устранение проблем / Troubleshooting

## Проблема: Unable to get image 'mcr.microsoft.com/mssql/server'

### ✅ Решение 1: Используйте SQL Server 2019 (УЖЕ ИСПРАВЛЕНО)

Образ SQL Server 2019 более стабилен на Windows с Docker Desktop.
Файл `docker-compose.yml` уже обновлен для использования `2019-latest`.

### ✅ Решение 2: Очистите Docker кеш и попробуйте снова

```powershell
# Остановите все контейнеры
docker-compose down

# Очистите Docker кеш
docker system prune -a

# Попробуйте снова
docker-compose up -d
```

### ✅ Решение 3: Проверьте настройки Docker Desktop

1. Откройте **Docker Desktop**
2. Перейдите в **Settings** (⚙️)
3. Убедитесь, что используются **Linux containers** (не Windows containers)
4. Перезапустите Docker Desktop

**Как переключиться на Linux containers:**
- Правый клик на иконку Docker в трее
- Если видите "Switch to Linux containers...", кликните на это
- Дождитесь перезапуска Docker

### ✅ Решение 4: Увеличьте ресурсы Docker

SQL Server требует минимум 2 GB RAM на инстанс.

1. Откройте **Docker Desktop → Settings → Resources**
2. Установите:
   - **Memory:** минимум 8 GB (рекомендуется 12-16 GB)
   - **CPUs:** минимум 4
3. Нажмите **Apply & Restart**

### ✅ Решение 5: Проверьте подключение к интернету

Попробуйте загрузить образ вручную:

```powershell
docker pull mcr.microsoft.com/mssql/server:2019-latest
```

Если ошибка продолжается:
- Проверьте proxy настройки
- Проверьте firewall/антивирус
- Попробуйте использовать VPN (если в вашем регионе ограничен доступ)

### ✅ Решение 6: Ручная загрузка образов

```powershell
# Загрузите все необходимые образы по очереди
docker pull mcr.microsoft.com/mssql/server:2019-latest
docker pull mcr.microsoft.com/dotnet/sdk:9.0
docker pull mcr.microsoft.com/dotnet/aspnet:9.0

# После загрузки запустите сервисы
docker-compose up -d
```

## Альтернативное решение: Локальный SQL Server

Если Docker продолжает давать сбои, можете использовать локальный SQL Server:

### Шаг 1: Установите SQL Server локально

Скачайте и установите:
- [SQL Server 2019 Express](https://www.microsoft.com/sql-server/sql-server-downloads)
- [SQL Server Management Studio (SSMS)](https://aka.ms/ssmsfullsetup)

### Шаг 2: Создайте три базы данных

Откройте SSMS и выполните:

```sql
CREATE DATABASE CustomerDB;
CREATE DATABASE OrderDB;
CREATE DATABASE ProductDB;
```

### Шаг 3: Обновите connection strings

В каждом `appsettings.json` замените на:

**Customer.Microservice/appsettings.json:**
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=CustomerDB;Integrated Security=True;TrustServerCertificate=True;"
}
```

**Order.Microservice/appsettings.json:**
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=OrderDB;Integrated Security=True;TrustServerCertificate=True;"
}
```

**Product.Microservice/appsettings.json:**
```json
"ConnectionStrings": {
  "DefaultConnection": "Server=localhost;Database=ProductDB;Integrated Security=True;TrustServerCertificate=True;"
}
```

### Шаг 4: Запустите сервисы без Docker

Откройте 3 терминала PowerShell:

**Терминал 1:**
```powershell
cd Customer.Microservice
dotnet run --urls "http://localhost:5001"
```

**Терминал 2:**
```powershell
cd Order.Microservice
dotnet run --urls "http://localhost:5002"
```

**Терминал 3:**
```powershell
cd Product.Microservice
dotnet run --urls "http://localhost:5003"
```

## Упрощенный docker-compose (только сервисы, без БД)

Если хотите контейнеризировать только сервисы, используя локальный SQL Server:

Создайте `docker-compose.local-db.yml`:

```yaml
version: '3.8'

services:
  customer-service:
    build:
      context: .
      dockerfile: Customer.Microservice/Dockerfile
    container_name: customer-service
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80
      - ConnectionStrings__DefaultConnection=Server=host.docker.internal;Database=CustomerDB;User Id=sa;Password=<DB_PASSWORD from .env>;TrustServerCertificate=True;
    ports:
      - "5001:80"
    extra_hosts:
      - "host.docker.internal:host-gateway"

  order-service:
    build:
      context: .
      dockerfile: Order.Microservice/Dockerfile
    container_name: order-service
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80
      - ConnectionStrings__DefaultConnection=Server=host.docker.internal;Database=OrderDB;User Id=sa;Password=<DB_PASSWORD from .env>;TrustServerCertificate=True;
    ports:
      - "5002:80"
    extra_hosts:
      - "host.docker.internal:host-gateway"

  product-service:
    build:
      context: .
      dockerfile: Product.Microservice/Dockerfile
    container_name: product-service
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:80
      - ConnectionStrings__DefaultConnection=Server=host.docker.internal;Database=ProductDB;User Id=sa;Password=<DB_PASSWORD from .env>;TrustServerCertificate=True;
    ports:
      - "5003:80"
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

Запуск:
```powershell
docker-compose -f docker-compose.local-db.yml up -d
```

## Другие распространенные проблемы

### Проблема: "Port is already allocated"

**Решение:**
```powershell
# Найдите процесс, использующий порт (например, 5001)
netstat -ano | findstr :5001

# Убейте процесс по PID
taskkill /PID <PID> /F
```

### Проблема: "Cannot connect to the Docker daemon"

**Решение:**
1. Убедитесь, что Docker Desktop запущен
2. Перезапустите Docker Desktop
3. Проверьте в PowerShell: `docker version`

### Проблема: Медленная загрузка образов

**Решение:**
Настройте зеркало Docker Registry:
1. Docker Desktop → Settings → Docker Engine
2. Добавьте:
```json
{
  "registry-mirrors": ["https://mirror.gcr.io"]
}
```

### Проблема: "no space left on device"

**Решение:**
```powershell
# Очистите неиспользуемые ресурсы
docker system prune -a --volumes

# Проверьте использование дискового пространства
docker system df
```

## Проверка системных требований

### Минимальные требования:
- **Windows 10/11** (64-bit, Pro/Enterprise/Education)
- **WSL 2** включен
- **Docker Desktop** последней версии
- **8 GB RAM** (для всех 3 SQL Server инстансов)
- **20 GB** свободного места на диске

### Проверка WSL 2:

```powershell
wsl --list --verbose
```

Если WSL 2 не установлен:
```powershell
wsl --install
wsl --set-default-version 2
```

## Логи и диагностика

### Просмотр логов Docker:

```powershell
# Логи всех сервисов
docker-compose logs

# Логи конкретного контейнера
docker logs customer-sqlserver
docker logs customer-service

# Следить за логами в реальном времени
docker logs -f customer-service
```

### Проверка состояния контейнеров:

```powershell
# Список всех контейнеров
docker ps -a

# Информация о конкретном контейнере
docker inspect customer-sqlserver

# Использование ресурсов
docker stats
```

### Подключение к контейнеру:

```powershell
# Bash в SQL Server контейнере
docker exec -it customer-sqlserver /bin/bash

# Проверка подключения к БД
docker exec -it customer-sqlserver /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P <DB_PASSWORD from .env>
```

## Контакты и помощь

Если проблема не решена:

1. Проверьте [Docker Desktop документацию](https://docs.docker.com/desktop/windows/)
2. Проверьте [SQL Server на Docker](https://hub.docker.com/_/microsoft-mssql-server)
3. Проверьте версию Docker: `docker --version`
4. Проверьте версию Docker Compose: `docker-compose --version`

## Быстрая проверка перед запуском

```powershell
# Проверка Docker
docker --version
docker-compose --version
docker info

# Проверка доступных ресурсов
wmic OS get FreePhysicalMemory
wmic cpu get NumberOfCores

# Проверка портов
netstat -ano | findstr ":5001 :5002 :5003 :1433 :1434 :1435"
```

Все порты должны быть свободны перед запуском!

