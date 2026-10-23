# Технические источники

Проверены при реализации первого этапа:

- Microsoft: [SQLite provider](https://learn.microsoft.com/en-us/ef/core/providers/sqlite) и [совместимость EF providers](https://learn.microsoft.com/en-us/ef/core/providers/). Выбран официальный provider EF Core 10 для .NET 10.
- Microsoft: [Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0), [CSRF](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), [integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0).
- GitHub: [официальная установка CLI на Windows](https://github.com/cli/cli/blob/trunk/docs/install_windows.md). Portable ZIP загружен из официального cli/cli release и проверен по SHA-256, возвращённому GitHub API.
- NuGet: пакетные версии проверены через официальный v3 feed. Использованы фиксированные Microsoft-пакеты 10.0.11 и lock-файлы.
- GitHub Actions: официальные [checkout v7.0.1](https://github.com/actions/checkout/releases/tag/v7.0.1), [setup-dotnet v6.0.0](https://github.com/actions/setup-dotnet/releases/tag/v6.0.0), [upload-artifact v7.0.2](https://github.com/actions/upload-artifact/releases/tag/v7.0.2). Runtime node24 проверен в action.yml; workflow закреплён по SHA тегов.

Совместимость дополнительно проверена фактическим restore/build/test. Документация будущего ONNX/YOLO должна быть заново сверена при реализации этапа 4; универсальный формат выхода сейчас не предполагается.
