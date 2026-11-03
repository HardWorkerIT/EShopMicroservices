# Обновления проекта

## 2026-11-03 — зависимости и проверки безопасности

- Microsoft.Data.SqlClient обновлён до 6.1.8; его транзитивная зависимость System.Security.Cryptography.Pkcs теперь разрешается в исправленную версию 9.0.21.
- Проверка `dotnet list Integration.Tests/Integration.Tests.csproj package --vulnerable --include-transitive` не находит уязвимых пакетов.
- NuGet Audit уровней NU1901–NU1904 повышены до ошибок сборки.
- Добавлен Dependabot: еженедельные обновления NuGet, Docker-образов и GitHub Actions; NuGet patch/minor сгруппированы по проектам.
- CI настроен на запуск SQL Server и проверку миграций Customer, Order и Product в чистых отдельных базах.
- Исправлены advisory [GHSA-846f-f764-76mx](https://github.com/dotnet/runtime/security/advisories/GHSA-846f-f764-76mx) и [GHSA-2fmv-rv2c-59x7](https://github.com/dotnet/runtime/security/advisories/GHSA-2fmv-rv2c-59x7).

# Изменения для Docker контейнеризации

## Дата: 2025-11-20

## Обзор
Все три микросервиса (Customer, Order, Product) упакованы в отдельные Docker контейнеры с индивидуальными базами данных MS SQL Server.

## Созданные файлы

### 1. Docker файлы
- `Customer.Microservice/Dockerfile` - образ для Customer Service
- `Order.Microservice/Dockerfile` - образ для Order Service
- `Product.Microservice/Dockerfile` - образ для Product Service
- `.dockerignore` - исключения для Docker build

### 2. Docker Compose
- `docker-compose.yml` - оркестрация всех сервисов и баз данных

### 3. Документация
- `DOCKER-README.md` - полная документация по использованию
- `CHANGES.md` - этот файл с описанием изменений

### 4. Вспомогательные скрипты
- `docker-manager.ps1` - PowerShell скрипт для управления сервисами
- `start-services.bat` - быстрый запуск сервисов
- `stop-services.bat` - быстрая остановка сервисов

## Измененные файлы

### 1. Startup.cs (все три микросервиса)
**Что изменено:**
- Заменено `UseSqlite()` на `UseSqlServer()` для работы с MS SQL Server

**Файлы:**
- `Customer.Microservice/Startup.cs`
- `Order.Microservice/Startup.cs`
- `Product.Microservice/Startup.cs`

### 2. appsettings.json (все три микросервиса)
**Что изменено:**
- Обновлены connection strings для подключения к SQL Server
- Каждый сервис подключается к своей базе данных

**Файлы:**
- `Customer.Microservice/appsettings.json`
  - Connection String: `Server=customer-db;Database=CustomerDB;...`
  
- `Order.Microservice/appsettings.json`
  - Connection String: `Server=order-db;Database=OrderDB;...`
  
- `Product.Microservice/appsettings.json`
  - Connection String: `Server=product-db;Database=ProductDB;...`

## Архитектура

```
┌─────────────────────────────────────────────────────────────┐
│                      Docker Network                          │
│                    (eshop-network)                           │
│                                                              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │   Customer   │  │    Order     │  │   Product    │     │
│  │   Service    │  │   Service    │  │   Service    │     │
│  │  :5001       │  │  :5002       │  │  :5003       │     │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘     │
│         │                  │                  │              │
│         ▼                  ▼                  ▼              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │   SQL Server │  │   SQL Server │  │   SQL Server │     │
│  │ customer-db  │  │   order-db   │  │  product-db  │     │
│  │  :1433       │  │  :1434       │  │  :1435       │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
│         │                  │                  │              │
│         ▼                  ▼                  ▼              │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐     │
│  │   Volume     │  │   Volume     │  │   Volume     │     │
│  │customer-db-  │  │ order-db-    │  │ product-db-  │     │
│  │    data      │  │    data      │  │    data      │     │
│  └──────────────┘  └──────────────┘  └──────────────┘     │
└─────────────────────────────────────────────────────────────┘
```

## Порты

### Внешние порты (доступны на хост-машине):
- `5001` → Customer Service (HTTP)
- `5002` → Order Service (HTTP)
- `5003` → Product Service (HTTP)
- `1433` → Customer SQL Server
- `1434` → Order SQL Server
- `1435` → Product SQL Server

### Внутренние порты (внутри Docker сети):
- Все сервисы слушают порт 80 внутри контейнера
- Все SQL Server слушают порт 1433 внутри контейнера

## Базы данных

### Customer Database
- **Контейнер:** customer-sqlserver
- **Имя БД:** CustomerDB
- **Порт:** 1433 (внешний)
- **Volume:** customer-db-data
- **Данные:** Клиенты

### Order Database
- **Контейнер:** order-sqlserver
- **Имя БД:** OrderDB
- **Порт:** 1434 (внешний)
- **Volume:** order-db-data
- **Данные:** Заказы

### Product Database
- **Контейнер:** product-sqlserver
- **Имя БД:** ProductDB
- **Порт:** 1435 (внешний)
- **Volume:** product-db-data
- **Данные:** Товары

## Credentials

**⚠️ ВАЖНО:** Используйте эти credentials только для разработки!

- **Пользователь:** sa
- **Пароль:** <DB_PASSWORD from .env>

Для production окружения обязательно смените пароль!

## Автоматическая инициализация

При первом запуске каждая база данных автоматически:
1. Создается (EnsureCreated)
2. Заполняется тестовыми данными
3. Готова к использованию

### Тестовые данные:
- **Customer:** 1 клиент (Guest, Moscow)
- **Order:** 3 заказа (все InProgress)
- **Product:** 3 компьютера (цены: 500, 1000, 1500)

## Быстрый запуск

### Вариант 1: Двойной клик (Windows)
Просто дважды кликните на файл `start-services.bat`

### Вариант 2: PowerShell скрипт
```powershell
.\docker-manager.ps1 start
```

### Вариант 3: Docker Compose
```bash
docker-compose up -d
```

## Проверка работы

После запуска откройте в браузере:
- http://localhost:5001/swagger - Customer Service
- http://localhost:5002/swagger - Order Service
- http://localhost:5003/swagger - Product Service

## Возможные проблемы и решения

### Проблема 1: Порты заняты
**Решение:** Измените порты в `docker-compose.yml`

### Проблема 2: Недостаточно памяти
**Решение:** Увеличьте память для Docker Desktop (Настройки → Resources → Memory)

### Проблема 3: SQL Server не запускается
**Решение:** 
- Проверьте логи: `docker-compose logs customer-db`
- Убедитесь, что выделено минимум 2GB RAM на контейнер

### Проблема 4: Сервисы не могут подключиться к БД
**Решение:**
- Дождитесь полного запуска SQL Server (healthcheck)
- Проверьте сетевое подключение: `docker-compose exec customer-service ping customer-db`

## Дополнительные команды

### Просмотр логов
```bash
# Все сервисы
docker-compose logs -f

# Конкретный сервис
docker-compose logs -f customer-service
docker-compose logs -f customer-db
```

### Перезапуск
```bash
# Все сервисы
docker-compose restart

# Конкретный сервис
docker-compose restart customer-service
```

### Полная очистка
```bash
# Остановка и удаление всех контейнеров и данных
docker-compose down -v
```

## Следующие шаги

Рекомендуется добавить:
1. API Gateway (Ocelot)
2. Централизованное логирование (ELK Stack)
3. Мониторинг (Prometheus + Grafana)
4. Service Discovery (Consul)
5. Message Bus (RabbitMQ/Kafka)
6. Distributed Tracing (Jaeger)
7. Health Checks для всех сервисов
8. CI/CD Pipeline

## Техническая информация

- **.NET Version:** 9.0
- **SQL Server Version:** 2022 (Developer Edition)
- **Docker Compose Version:** 3.8
- **Entity Framework Core:** 9.0.6
- **Multi-stage Build:** Да (build → publish → final)

## Безопасность

### Для production обязательно:
1. Измените пароль SQL Server
2. Используйте Docker Secrets для паролей
3. Настройте HTTPS для всех endpoints
4. Используйте непривилегированные пользователи в контейнерах
5. Сканируйте образы на уязвимости
6. Настройте network policies
7. Включите TLS для SQL Server
8. Используйте Azure Key Vault или аналог для secrets

## Контакты

При возникновении проблем обратитесь к документации:
- `DOCKER-README.md` - полная документация
- Docker Documentation: https://docs.docker.com/
- .NET Documentation: https://docs.microsoft.com/dotnet/

---

**Статус:** ✅ Готово к использованию
**Дата создания:** 20.11.2025
**Версия:** 1.0.0

