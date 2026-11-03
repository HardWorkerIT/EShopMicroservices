# 🚀 EShop Microservices - Quick Start Guide

## ✅ Готово к использованию!

Все три микросервиса (Customer, Order, Product) упакованы в Docker контейнеры с отдельными базами данных MS SQL Server.

## 📦 Что создано:

### API Gateway:
- **Aggregator** → порт `5000` → объединяет все микросервисы

### Микросервисы:
- **Customer Service** → порт `5001` → БД: `CustomerDB` (порт 1433)
- **Order Service** → порт `5002` → БД: `OrderDB` (порт 1434)
- **Product Service** → порт `5003` → БД: `ProductDB` (порт 1435)

## 🏃 Быстрый запуск:

### Вариант 1: Автоматический (рекомендуется)

```powershell
# Просто запустите этот скрипт:
.\start-services.bat
```

Перед первым запуском скопируйте `.env.example` в `.env` и замените все демонстрационные значения секретов уникальными паролями и ключами. Для каждого сервиса задаются отдельные runtime и migration пароли; `.env` исключён из Git.
Затем выполните `docker compose up --build -d`. Compose создаёт базы и отдельные логины автоматически: runtime-логины имеют только права чтения/записи, а startup migrations используют отдельные migration credentials.

### Вариант 2: Через PowerShell

```powershell
# 1. После настройки .env запустить сервисы и базы
docker compose up --build -d

# 2. Следить за состоянием и логами
docker compose ps
docker compose logs -f
```

### Запуск без сборки образов

```bash
docker-compose up -d
```

Используйте этот вариант, если образы уже собраны. Базы и отдельные логины создаются автоматически.

## 🌐 Доступ к сервисам:

После запуска откройте в браузере:

- 🌐 **Aggregator (API Gateway):** http://localhost:5000/swagger
- 👤 **Customer Service:** http://localhost:5001/swagger
- 📦 **Order Service:** http://localhost:5002/swagger
- 🛍️ **Product Service:** http://localhost:5003/swagger

**Рекомендуется использовать Aggregator** для обращения ко всем сервисам!

## 🗄️ Подключение к базам данных:

Используйте SQL Server Management Studio (SSMS) или Azure Data Studio:

### Customer Database
- Server: `localhost,1433`
- Database: `CustomerDB`
- User: `sa`
- Password: `<DB_PASSWORD from .env>`

### Order Database
- Server: `localhost,1434`
- Database: `OrderDB`
- User: `sa`
- Password: `<DB_PASSWORD from .env>`

### Product Database
- Server: `localhost,1435`
- Database: `ProductDB`
- User: `sa`
- Password: `<DB_PASSWORD from .env>`

## 🛠️ Управление контейнерами:

### Просмотр статуса:
```powershell
docker-compose ps
```

### Просмотр логов:
```powershell
# Все сервисы
docker-compose logs -f

# Конкретный сервис
docker logs customer-service -f
docker logs customer-sqlserver -f
```

### Остановка:
```powershell
docker-compose stop
```

### Перезапуск:
```powershell
docker-compose restart
```

### Полная остановка и удаление (включая данные):
```powershell
docker-compose down -v
```

## 📝 Тестовые данные:

При первом запуске автоматически создаются тестовые данные:

### Customer Service:
- 1 клиент: `Guest` из `Moscow`

### Order Service:
- 3 заказа со статусом `InProgress`

### Product Service:
- 3 компьютера:
  - Computer 1 - 1000₽
  - Computer 2 - 1500₽
  - Computer 3 - 500₽

## 🔧 Устранение неполадок:

### Проблема: Контейнеры не запускаются

1. Убедитесь, что Docker Desktop запущен
2. Проверьте, что порты 5001-5003 и 1433-1435 свободны:
   ```powershell
   netstat -ano | findstr ":5001 :5002 :5003 :1433 :1434 :1435"
   ```

### Проблема: Ошибка подключения к базе данных

1. Убедитесь, что SQL Server контейнеры запущены:
   ```powershell
   docker ps | findstr sqlserver
   ```

2. Создайте базы данных вручную:
   ```powershell
   .\create-databases.ps1
   ```

3. Перезапустите микросервисы:
   ```powershell
   docker restart customer-service order-service product-service
   ```

### Проблема: Недостаточно памяти

SQL Server требует минимум 2 GB RAM на инстанс. Увеличьте выделенную память:
1. Docker Desktop → Settings → Resources
2. Установите Memory: минимум 8 GB
3. Нажмите Apply & Restart

## 📊 Архитектура:

```
┌──────────────────────────────────────────────────────────┐
│                    Docker Network                         │
│                  (eshop-network)                          │
│                                                           │
│                  ┌──────────────┐                        │
│                  │  Aggregator  │                        │
│                  │ API Gateway  │                        │
│                  │    :5000     │                        │
│                  └──────┬───────┘                        │
│                         │                                 │
│          ┌──────────────┼──────────────┐                 │
│          │              │              │                 │
│  ┌───────▼──────┐  ┌───▼──────┐  ┌───▼──────┐          │
│  │   Customer   │  │  Order   │  │ Product  │          │
│  │   Service    │  │ Service  │  │ Service  │          │
│  │    :5001     │  │  :5002   │  │  :5003   │          │
│  └──────┬───────┘  └────┬─────┘  └────┬─────┘          │
│         │               │             │                  │
│         ▼               ▼             ▼                  │
│  ┌──────────┐    ┌──────────┐  ┌──────────┐            │
│  │ SQL 2019 │    │ SQL 2019 │  │ SQL 2019 │            │
│  │customer  │    │  order   │  │ product  │            │
│  │   -db    │    │   -db    │  │   -db    │            │
│  └──────────┘    └──────────┘  └──────────┘            │
└──────────────────────────────────────────────────────────┘
```

## 📚 Дополнительная информация:

- **Технологии:** .NET 9.0, Entity Framework Core 9.0, SQL Server 2019
- **Swagger:** Доступен для всех микросервисов
- **Автоматическая миграция:** БД создаются и инициализируются автоматически
- **Retry Policy:** Включен EnableRetryOnFailure для устойчивости к сбоям
- **Обработка исключений:** Глобальный exception handler во всех сборках
- **Логирование:** Все ошибки автоматически логируются с полным контекстом

## ⚠️ Важно для Production:

Для production окружения обязательно:
1. Смените пароль SQL Server
2. Используйте Docker Secrets для паролей
3. Настройте HTTPS
4. Добавьте API Gateway
5. Настройте мониторинг и логирование
6. Используйте отдельные серверы БД

## 🎉 Готово!

Ваши микросервисы готовы к использованию!

Откройте http://localhost:5000/swagger (Aggregator) и начните тестирование!

## 📖 Документация:

- **README.md** - Подробное описание проекта и архитектуры
- **ERROR-HANDLING.md** - Детали системы обработки ошибок
- **DOCKER-README.md** - Docker конфигурация
- **TROUBLESHOOTING.md** - Решение проблем
- **CHANGES.md** - История изменений

