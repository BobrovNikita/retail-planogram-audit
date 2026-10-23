# RetailVision

Система контроля выкладки товаров (Planogram Compliance), ASP.NET Core MVC / .NET 10.

[![CI](https://github.com/BobrovNikita/retail-planogram-audit/actions/workflows/ci.yml/badge.svg)](https://github.com/BobrovNikita/retail-planogram-audit/actions/workflows/ci.yml)

**Публичный репозиторий:** https://github.com/BobrovNikita/retail-planogram-audit

Разработка ведётся поэтапно: [план](docs/development-plan.md). Этап 1 реализован: запускаемый сайт с SQLite и явно обозначенной демонстрацией, без обученных весов нейросети.

## Что работает

- Русский адаптивный MVC-интерфейс: дашборд, каталог с поиском, просмотр планограмм, DEMO-аудит.
- Шесть учебных SKU, две полки, восемь фейсингов; авторские SVG-упаковки без чужих фотографий.
- Три воспроизводимых примера: правильная выкладка (100%), пропуск (87,5%), перестановка (87,5%).
- Настоящий LCS-анализатор, SVG-рамки и переход от нарушения к объекту или ожидаемой позиции.
- SQLite, EF Core migration, идемпотентное заполнение, отдельное соответствие классов модели и SKU.
- ASP.NET Core Identity; защищённые настройки; CSRF, rate limiting, CSP и безопасная обработка ошибок.
- 14 unit-тестов и 18 интеграционных тестов. CI на Ubuntu при push/PR.

**DEMO не использует нейросеть и не принимает пользовательские фотографии.** Публичные посетители не изменяют постоянные данные. Дашборд читает только сохранённые REAL-аудиты и сейчас корректно показывает пустое состояние.

![Дашборд RetailVision](docs/images/dashboard.png)

## Локальный запуск

Требуется .NET SDK 10.0.400.

```sh
dotnet restore --locked-mode
dotnet build --configuration Release
dotnet test --configuration Release --no-build
dotnet run --project src/RetailVision.Web --configuration Release --no-build
```

Откройте http://localhost:5180. Профиль запуска включает Development и привязку к localhost. База создаётся миграцией при первом запуске в `src/RetailVision.Web/App_Data/retailvision.db`. Повторный запуск не удаляет данные. `/health` проверяет доступность БД.

Путь к данным можно изменить через `Storage__DataDirectory` (предпочтительно абсолютный путь вне webroot). Не запускайте несколько экземпляров приложения с одной SQLite-базой на этом этапе.

## Администратор

Без конфигурации административная учётная запись **не создаётся**. Для первого запуска задайте `Admin__Email` и `Admin__Password` через окружение или .NET User Secrets. Пароль должен содержать не менее 12 символов, верхний и нижний регистры, цифру и специальный символ. Не храните пароль в Git, README или командной истории; используйте защищённый ввод/secret manager.

При первом запуске создаётся пользователь с ролью `Administrator`. Уже существующий пароль не сбрасывается. После создания удалите bootstrap-пароль из окружения. Публичной регистрации и стандартного пароля нет. Учётные записи интеграционных тестов создаются со случайным паролем только во временной БД.

## Структура

```text
src/RetailVision.Core             домен и сравнение последовательностей
src/RetailVision.Infrastructure   EF Core, SQLite, Identity, миграции и seed
src/RetailVision.Vision           интерфейсная реализация fixture-детектора
src/RetailVision.Web              MVC, Razor, CSS, JavaScript, DI и безопасность
tests/                           unit- и интеграционные тесты
docs/                            архитектура, алгоритм, база, запуск и ограничения
ml/                              границы будущего ML-пайплайна
deploy/                          статус подготовки production
```

## Миграции и CI

```sh
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/RetailVision.Infrastructure --startup-project src/RetailVision.Web
```

NuGet-версии зафиксированы, `packages.lock.json` включены в Git. Microsoft EF/Identity/Testing 10.0.11 соответствуют выбранной ветке .NET 10. При обновлении сначала меняйте версии и lock-файлы, затем запускайте проверки. Workflow имеет только `contents: read`, выполняет locked restore, Release build и tests, сохраняет TRX.

## Следующие этапы и ограничения

Этапы 2–6 ещё не завершены: CRUD и визуальный редактор, JSON import/export, полный набор метрик и нарушений, ONNX/обучение, загрузки/калибровка, сохранение и коррекция аудитов, отчёты, Docker и VPS. Недоступные разделы явно помечены «В разработке».

Метрика DEMO оценивает сохранение порядка с учётом пропусков, а не точную геометрическую позицию. При обмене двух соседних SKU LCS фиксирует одно минимальное перемещение. Неизвестные объекты требуют проверки; «не обнаружен» не доказывает физического отсутствия товара. Точность AI не измерялась: обученной модели нет.

Docker отсутствует в текущей среде; Linux-контейнер не собран и не проверен. VPS не развёрнут. Публичный GitHub содержит исходники, а не размещённый сайт.

## Документация

- [Архитектура](docs/architecture.md)
- [База и ER-диаграмма](docs/database.md)
- [Алгоритм и ограничения](docs/planogram-algorithm.md)
- [Тестирование](docs/testing.md)
- [Запуск, резервное копирование и публикация](docs/deployment.md)
- [ML-пайплайн: текущее состояние](docs/ml-pipeline.md)
- [История изменений](CHANGELOG.md)
- [Официальные технические источники](docs/sources.md)
