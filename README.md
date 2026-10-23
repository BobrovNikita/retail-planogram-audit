# RetailVision

Система контроля выкладки товаров (Planogram Compliance), ASP.NET Core MVC / .NET 10.

Разработка ведётся поэтапно: [план](docs/development-plan.md). Первый этап — запускаемый сайт с SQLite и явно обозначенной демонстрацией, без обученных весов нейросети.

## Локальный запуск

Требуется .NET SDK 10.0.400.

```sh
dotnet restore
dotnet build
dotnet test
dotnet run --project src/RetailVision.Web --no-launch-profile --urls http://localhost:5180
```

Откройте http://localhost:5180. Подробности будут дополнены вместе с реализацией.

## GitHub

Создание публичного `retail-planogram-audit` ожидает авторизации GitHub CLI. Ссылка и CI badge будут добавлены только после успешной публикации.
