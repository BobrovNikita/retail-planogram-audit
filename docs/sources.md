# Технические источники

Проверены при реализации первого этапа:

- Microsoft: [SQLite provider](https://learn.microsoft.com/en-us/ef/core/providers/sqlite) и [совместимость EF providers](https://learn.microsoft.com/en-us/ef/core/providers/). Выбран официальный provider EF Core 10 для .NET 10.
- Microsoft: [Identity](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity?view=aspnetcore-10.0), [CSRF](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0), [integration tests](https://learn.microsoft.com/en-us/aspnet/core/test/integration-tests?view=aspnetcore-10.0).
- GitHub: [официальная установка CLI на Windows](https://github.com/cli/cli/blob/trunk/docs/install_windows.md). Portable ZIP загружен из официального cli/cli release и проверен по SHA-256, возвращённому GitHub API.
- NuGet: пакетные версии проверены через официальный v3 feed. Использованы фиксированные Microsoft-пакеты 10.0.11 и lock-файлы.
- GitHub Actions: официальные [checkout v7.0.1](https://github.com/actions/checkout/releases/tag/v7.0.1), [setup-dotnet v6.0.0](https://github.com/actions/setup-dotnet/releases/tag/v6.0.0), [upload-artifact v7.0.2](https://github.com/actions/upload-artifact/releases/tag/v7.0.2). Runtime node24 проверен в action.yml; workflow закреплён по SHA тегов.

Дополнительно для этапа 2:

- [SkiaSharp 4.153.1](https://github.com/mono/SkiaSharp/releases/tag/v4.153.1), [лицензия MIT](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md). Версия и Linux native assets закреплены в пакетах; реальное декодирование проверяется тестами.
- Microsoft: [EF Core concurrency tokens](https://learn.microsoft.com/en-us/ef/core/saving/concurrency). Используются управляемые приложением версии, совместимые с SQLite.

Дополнительно для этапа 4 (2026-10-25):

- Ultralytics: [YOLO26](https://docs.ultralytics.com/models/yolo26/), [export](https://docs.ultralytics.com/modes/export/), [исходник end-to-end guide](https://github.com/ultralytics/ultralytics/blob/main/docs/en/guides/end2end-detection.md). nms=False выбран явно; форма проверена фактическим экспортом, а не только документацией.
- Microsoft: [C# inference](https://onnxruntime.ai/docs/tutorials/csharp/basic_csharp.html), [RunOptions.Terminate](https://onnxruntime.ai/docs/api/csharp/api/Microsoft.ML.OnnxRuntime.RunOptions.html), [NuGet OnnxRuntime 1.31.0](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime/1.31.0).
- Официальный PyPI: [Ultralytics](https://pypi.org/project/ultralytics/), [ONNX](https://pypi.org/project/onnx/). Версии закреплены в ml/requirements.txt, окружение и результаты пробы описаны в ml-pipeline.md.

Совместимость дополнительно проверена фактическим restore/build/test и native ONNX inference. При смене версии экспортёра контракт нужно перепроверить.

Дополнительно для этапа 6 (2026-10-26):

- Microsoft: [Forwarded Headers и доверенные proxy](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0), [.NET container images](https://learn.microsoft.com/en-us/dotnet/core/docker/container-images). SDK 10.0.400-noble и ASP.NET 10.0.11-noble проверены через MCR, их manifest digests закреплены в Dockerfile. Noble включает ICU для русской культуры.
- Docker: [Compose secrets](https://docs.docker.com/compose/how-tos/use-secrets/). Bootstrap использует файл /run/secrets, права исходного файла должны позволять чтение пользователю app.
- Nginx: [proxy_set_header](https://nginx.org/en/docs/http/ngx_http_proxy_module.html#proxy_set_header). Список [официальных Docker-образов](https://github.com/docker-library/official-images/blob/master/library/nginx) подтвердил stable tag 1.30.6-alpine; proxy перезаписывает X-Forwarded-For/Proto и сохраняет исходный Host с портом.
