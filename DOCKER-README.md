# EShop Microservices - Docker Setup

Этот проект содержит микросервисную архитектуру для интернет-магазина с тремя отдельными сервисами, каждый из которых имеет свою базу данных MS SQL Server.

## Архитектура

Проект состоит из следующих компонентов:

### Микросервисы:
1. **Customer Service** - управление клиентами
   - Порт: 5001
   - База данных: customer-db (порт 1433)
   - Swagger UI: http://localhost:5001/swagger

2. **Order Service** - управление заказами
   - Порт: 5002
   - База данных: order-db (порт 1434)
   - Swagger UI: http://localhost:5002/swagger

3. **Product Service** - управление товарами
   - Порт: 5003
   - База данных: product-db (порт 1435)
   - Swagger UI: http://localhost:5003/swagger

### Базы данных:
Каждый микросервис имеет свою отдельную базу данных MS SQL Server 2022:
- **CustomerDB** - для сервиса Customer
- **OrderDB** - для сервиса Order
- **ProductDB** - для сервиса Product

## Требования

- Docker Desktop (Windows/Mac) или Docker Engine (Linux)
- Docker Compose v3.8+
- Минимум 8 GB RAM (рекомендуется 16 GB для комфортной работы всех SQL Server инстансов)

## Быстрый старт

### 1. Запуск всех сервисов

Из корневой директории проекта выполните:

```bash
cp .env.example .env
# Замените все демонстрационные пароли и ключи в .env уникальными секретами.
docker compose up --build -d
```

Compose дождётся готовности каждого SQL Server перед запуском соответствующего API. Эта команда:
- Скачает необходимые образы Docker
- Создаст 3 контейнера с MS SQL Server
- Соберет и запустит 3 микросервиса
- Создаст и инициализирует базы данных с тестовыми данными

### 2. Проверка статуса

Проверить статус всех контейнеров:

```bash
docker-compose ps
```

Просмотр логов:

```bash
# Логи всех сервисов
docker-compose logs -f

# Логи конкретного сервиса
docker-compose logs -f customer-service
docker-compose logs -f order-service
docker-compose logs -f product-service
```

### 3. Доступ к сервисам

После запуска сервисы будут доступны по следующим адресам:

- **Customer Service**: http://localhost:5001/swagger
- **Order Service**: http://localhost:5002/swagger
- **Product Service**: http://localhost:5003/swagger

### 4. Подключение к базам данных

Вы можете подключиться к базам данных используя SQL Server Management Studio (SSMS) или Azure Data Studio:

**Customer Database:**
- Server: localhost,1433
- Database: CustomerDB
- User: sa
- Password: <DB_PASSWORD from .env>

**Order Database:**
- Server: localhost,1434
- Database: OrderDB
- User: sa
- Password: <DB_PASSWORD from .env>

**Product Database:**
- Server: localhost,1435
- Database: ProductDB
- User: sa
- Password: <DB_PASSWORD from .env>

## Команды Docker Compose

### Запуск

```bash
# Запуск в фоновом режиме
docker-compose up -d

# Запуск с выводом логов в консоль
docker-compose up
```

### Остановка

```bash
# Остановка всех сервисов
docker-compose stop

# Остановка и удаление контейнеров
docker-compose down

# Остановка, удаление контейнеров и volumes (удалит все данные БД)
docker-compose down -v
```

### Перезапуск

```bash
# Перезапуск всех сервисов
docker-compose restart

# Перезапуск конкретного сервиса
docker-compose restart customer-service
```

### Пересборка

```bash
# Пересборка и запуск всех сервисов
docker-compose up -d --build

# Пересборка конкретного сервиса
docker-compose build customer-service
docker-compose up -d customer-service
```

## Тестирование API

### Customer Service

Получить список клиентов:
```bash
curl http://localhost:5001/api/customer
```

Добавить клиента:
```bash
curl -X POST http://localhost:5001/api/customer \
  -H "Content-Type: application/json" \
  -d '{
    "name": "John Doe",
    "city": "Moscow"
  }'
```

### Order Service

Получить список заказов:
```bash
curl http://localhost:5002/api/order
```

Добавить заказ:
```bash
curl -X POST http://localhost:5002/api/order \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": 1,
    "productId": 1,
    "orderStatus": 0
  }'
```

### Product Service

Получить список товаров:
```bash
curl http://localhost:5003/api/product
```

Добавить товар:
```bash
curl -X POST http://localhost:5003/api/product \
  -H "Content-Type: application/json" \
  -d '{
    "name": "New Computer",
    "rate": 2000
  }'
```

## Тестовые данные

При первом запуске каждый сервис автоматически создает тестовые данные:

**Customer Service:**
- 1 клиент: Guest из Moscow

**Order Service:**
- 3 заказа со статусом InProgress

**Product Service:**
- 3 компьютера с разными ценами (500, 1000, 1500)

## Устранение неполадок

### Проблема: Контейнеры не запускаются

1. Проверьте, что Docker Desktop запущен
2. Убедитесь, что порты 5001-5003 и 1433-1435 свободны
3. Проверьте логи: `docker-compose logs`

### Проблема: Недостаточно памяти

SQL Server требует минимум 2 GB RAM на инстанс. Увеличьте выделенную память для Docker Desktop в настройках.

### Проблема: База данных не создается

1. Проверьте логи SQL Server: `docker-compose logs customer-db`
2. Убедитесь, что healthcheck проходит: `docker inspect customer-sqlserver`
3. Попробуйте пересоздать volumes: `docker-compose down -v && docker-compose up -d`

### Проблема: Ошибка подключения к базе данных

1. Дождитесь полного запуска SQL Server (healthcheck должен пройти)
2. Проверьте, что сервисы могут достучаться до БД: `docker-compose exec customer-service ping customer-db`
3. Перезапустите сервисы: `docker-compose restart`

## Изменение пароля SQL Server

⚠️ **Важно**: Пароль `<DB_PASSWORD from .env>` используется только для разработки. Для production используйте надежный пароль.

Чтобы изменить пароль:

1. Откройте `docker-compose.yml`
2. Измените значение `SA_PASSWORD` для всех трех баз данных
3. Обновите `ConnectionStrings__DefaultConnection` для всех трех сервисов
4. Также обновите пароли в файлах `appsettings.json` в папках микросервисов

## Мониторинг

Для мониторинга состояния контейнеров используйте:

```bash
# Использование ресурсов
docker stats

# Информация о контейнере
docker inspect customer-service

# Проверка сети
docker network inspect eshopmicroservices_eshop-network
```

## Сеть

Все сервисы работают в одной сети Docker `eshop-network`, что позволяет им взаимодействовать друг с другом по именам контейнеров.

## Volumes

Данные баз данных сохраняются в именованных volumes:
- `customer-db-data`
- `order-db-data`
- `product-db-data`

Это позволяет сохранять данные даже после остановки контейнеров.

## Production

Для production рекомендуется:

1. Использовать secrets для паролей
2. Настроить HTTPS для всех сервисов
3. Добавить API Gateway (например, Ocelot)
4. Настроить централизованное логирование
5. Добавить мониторинг и health checks
6. Использовать отдельные серверы БД вместо контейнеров
7. Настроить автоматическое резервное копирование БД

## Дополнительная информация

- Все микросервисы используют .NET 9.0
- Entity Framework Core используется для работы с БД
- Swagger UI доступен для тестирования API
- Базы данных автоматически создаются при первом запуске

