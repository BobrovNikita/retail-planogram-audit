# Сверка с исходным ТЗ

Дата: 27.10.2026. Основание: пользовательское задание «Разработка RetailVision», разделы 1–21; код приложения 0.8, существующие тесты и протоколы. Это проверка реализации по исходникам и сохранённым результатам, а не новая независимая экспертиза качества распознавания или повторный браузерный прогон всех экранов.

Предметная область — контроль соответствия фактической выкладки эталону (планограмме). Цель проекта — пройти путь от фотографии и SKU до объяснимых нарушений, метрик и сохраняемого отчёта. Основные роли: публичный посетитель DEMO и администратор, работающий с каталогом, планограммами и собственными фотоаудитами.

Статусы: **выполнено** — функция реализована и имеет указанные свидетельства; **частично подтверждено** — механизм есть, но заявлять требуемое качество/переносимость во всех условиях нельзя; **ожидает внешних условий** — нужны новые данные или целевой сервер. Процент «готовности всего проекта» не вычисляется: пункты имеют разный объём и значимость.

## Функциональные требования

| Раздел ТЗ / требование | Статус | Подтверждение и границы |
|---|---|---|
| 1, 12. Фото → детекции → сравнение → отчёт | Выполнено | [PhotoAuditController](../../src/RetailVision.Web/Controllers/PhotoAuditController.cs), [REAL-демонстрация](../real-demo.md); на восьми SKU, с ручной калибровкой |
| 2, 4. C#, MVC, EF Core, SQLite, Identity, DI; четыре слоя | Выполнено | [Архитектура](../architecture.md), [решение](../../RetailVision.sln); Core отделён от MVC и ONNX |
| 2. Razor, JavaScript, Fetch, SVG, без тяжёлого SPA | Выполнено | [Views](../../src/RetailVision.Web/Views), [JavaScript](../../src/RetailVision.Web/wwwroot/js) |
| 3. Публичный GitHub, Conventional Commits, push, CI | Выполнено | [Репозиторий](https://github.com/BobrovNikita/retail-planogram-audit), [CI workflow](../../.github/workflows/ci.yml); история развития сохранена |
| 5. Русский адаптивный интерфейс и семь разделов | Выполнено | [Layout](../../src/RetailVision.Web/Views/Shared/_Layout.cshtml), [протоколы desktop/mobile](../testing.md) |
| 5. Реальные счётчики, график и последние проверки | Выполнено | [HomeController](../../src/RetailVision.Web/Controllers/HomeController.cs); дашборд считает собственные активные REAL-аудиты, без фиктивных цифр |
| 6. Поля товара, CRUD, поиск, категории, изображения, активность | Выполнено | [CatalogController](../../src/RetailVision.Web/Controllers/CatalogController.cs), [руководство](../catalog-and-editor.md); связанные товары отключаются вместо опасного удаления |
| 6. Явное соответствие класса модели и SKU | Выполнено | `ModelClassMapping` в [сущностях](../../src/RetailVision.Core/Entities.cs); ProductId не равен индексу класса |
| 6. Учебный ассортимент и изображения | Выполнено | Шесть нейтральных DEMO SKU; отдельный установщик добавляет восемь HoloSelecta SKU с атрибуцией источника |
| 7. Визуальный редактор, порядок, фейсинги, полки | Выполнено | [Редактор](../../src/RetailVision.Web/Views/Planograms/Edit.cshtml), [PlanogramStore](../../src/RetailVision.Infrastructure/PlanogramStore.cs); мышь и кнопки |
| 7. Сохранение, повторное открытие, копирование, JSON | Выполнено | [PlanogramsController](../../src/RetailVision.Web/Controllers/PlanogramsController.cs); версии, валидация, импорт как новая копия |
| 8. IProductDetector, ONNX и fixture-реализации | Выполнено | [Интерфейс](../../src/RetailVision.Core/IProductDetector.cs), [OnnxProductDetector](../../src/RetailVision.Vision/OnnxProductDetector.cs), [FixtureProductDetector](../../src/RetailVision.Vision/FixtureProductDetector.cs) |
| 8. CPU, предобработка, тензоры, координаты, confidence | Выполнено | [PhotoPreprocessor](../../src/RetailVision.Vision/PhotoPreprocessor.cs), [контракт](../ml-pipeline.md); статический FP32 ONNX, а не любой произвольный экспорт YOLO |
| 9. Python train/validate/export, transfer learning | Выполнено | [ML-пайплайн](../../ml/README.md), [аргументы и результаты v2](../experiments/holoselecta-v2/README.md); обучение на RTX 3050 |
| 9. Разделение выборок без утечек сходных кадров | Частично подтверждено | Проверяются дубли и группы съёмки, split по целым датам; те же автоматы могут встречаться в разных split. Нужен новый набор по независимым локациям |
| 9. Соответствие классов и parity PyTorch/ONNX | Выполнено | [Parity на 30 фото](../experiments/holoselecta-v2/export-parity.json), проверки manifest/SHA/классов и страницы настроек |
| 10. Рамки, центры, опорные точки, полки, порядок | Выполнено | [ComplianceAnalyzer](../../src/RetailVision.Core/ComplianceAnalyzer.cs); назначение по нижней опорной точке, сортировка слева направо |
| 10. Ручная нормализованная калибровка | Выполнено | [Фотоаудит](../../src/RetailVision.Web/Views/PhotoAudit/Edit.cshtml); полосы сохраняются с аудитом, автоматическое выделение полок не является текущим требованием |
| 11. Позиционный и улучшенный алгоритмы | Выполнено | [Методика](../planogram-algorithm.md): LCS, повторяющиеся SKU, вставки/пропуски, одно использование каждой детекции |
| 11. Шесть типов нарушений и ручная проверка | Выполнено | `MissingProduct`, `WrongProduct`, `WrongPosition`, `ExtraProduct`, `UnknownProduct`, `WrongShelf`; неопределённость отделена от подтверждённых по детекциям отклонений |
| 11. Отдельные метрики, формулы и unit-тесты | Выполнено | [Формулы и примеры](../planogram-algorithm.md), [Core.Tests](../../tests/RetailVision.Core.Tests); позиционная метрика не выдаётся за отраслевой стандарт |
| 12. Индикаторы выполнения, цветные рамки, переход к нарушению | Выполнено | [Результат DEMO](../../src/RetailVision.Web/Views/Audit/Result.cshtml), [отчёт](../../src/RetailVision.Web/Views/History/_Report.cshtml); для пропуска подсвечивается ожидание, рамка не выдумывается |
| 12, 13. Ручная коррекция и пересчёт | Выполнено | [AuditSnapshot / AuditReview](../../src/RetailVision.Core/AuditSnapshot.cs), [история](../history-and-reports.md); исходная confidence не подменяется |
| 13. Снимок эталона, модель, фото, метрики, журнал | Выполнено | [AuditStore](../../src/RetailVision.Infrastructure/AuditStore.cs), [схема БД](../database.md); изменения каталога не переписывают старые отчёты |
| 13. Сохранение всех проверок | Выполнено с разграничением ролей | Успешный REAL сохраняется автоматически; DEMO сохраняет администратор отдельной кнопкой. Анонимные DEMO не пишут постоянные данные в соответствии с разделом 16 |
| 13. Фильтры, сравнение, JSON, печать / PDF | Выполнено | [HistoryController](../../src/RetailVision.Web/Controllers/HistoryController.cs); PDF сохраняется через печать браузера, серверный renderer не заявляется |
| 14. Минимум три воспроизводимых DEMO | Выполнено | Семь [сценариев](../planogram-algorithm.md); тот же анализатор, явная метка DEMO |
| 15. Все перечисленные сущности, миграции, seed | Выполнено | [Entities](../../src/RetailVision.Core/Entities.cs), [четыре миграции](../../src/RetailVision.Infrastructure/Migrations); старые аудиты сохраняются |
| 18. Предметная область, цели, требования, диаграммы, методики | Выполнено | Этот документ, [архитектура](../architecture.md), [ER-диаграмма](../database.md), [ML](../ml-pipeline.md), [тестирование](../testing.md), [развёртывание](../deployment.md) |
| 20–21. Этапы 1–6, запущенное приложение, история разработки | Выполнено | [План развития](../development-plan.md), [CHANGELOG](../../CHANGELOG.md), CI; этап 6 — подготовка к публикации, не наличие публичного VPS |

## Нефункциональные требования и эксплуатация

| Раздел ТЗ / требование | Статус | Подтверждение и границы |
|---|---|---|
| 16. Identity, CSRF, права администратора и владельца | Выполнено | [Program](../../src/RetailVision.Web/Program.cs), контроллеры и интеграционные тесты; отчёты изолированы по владельцу |
| 16. Ограничение формата, байтов, пикселей и имён файлов | Выполнено | [PhotoPreprocessor](../../src/RetailVision.Vision/PhotoPreprocessor.cs), [ProductImageStore](../../src/RetailVision.Infrastructure/ProductImageStore.cs); приватное хранение вне wwwroot |
| 16. Rate limiting, безопасные ошибки, секреты вне Git | Выполнено | [Program](../../src/RetailVision.Web/Program.cs), [.gitignore](../../.gitignore), [запуск](../deployment.md); это не отчёт внешнего pentest |
| 17. Linux, CPU без Python/GPU, ограничение AI-очереди | Частично подтверждено | [PhotoVisionService](../../src/RetailVision.Web/PhotoVisionService.cs); Linux native/контейнер проверены с тестовым графом, обученная модель — локально на Windows CPU. Нужен прогон на целевом VPS |
| 17. Docker, Compose, Nginx, Production, persistent data, health | Выполнено | [Compose](../../compose.yaml), [deploy](../../deploy), [успешный выпуск](https://github.com/BobrovNikita/retail-planogram-audit/actions/runs/40397732705) |
| 17. Backup, restore, обновление без потери БД | Выполнено | [Инструкция](../deployment.md), container CI: перезапуск, восстановление в пустой том, отказ перезаписывать непустой |
| 17. Основа ручного deployment workflow | Выполнено | [Prepare deployment](../../.github/workflows/deploy.yml) создаёт проверенный образ и исходники; подключения к VPS нет |
| 17. Публичный VPS, домен, настоящий HTTPS | Ожидает внешних условий | Нужны хост, доступ и домен; сертификат CI временный и не подтверждает публичную публикацию |
| 19. Разделение ответственности, валидация, тесты | Выполнено | [Архитектура](../architecture.md), 105 unit + 101 integration и 15 Python; Release build без ошибок/предупреждений на проверенном коммите |
| Ограниченные ресурсы VPS и одновременные пользователи | Частично подтверждено | Очередь и лимиты есть; реальная пропускная способность, задержки и память на выбранном VPS не измерены |
| Дополнение 0.8: корзина, восстановление, очистка | Выполнено | [AuditRetention](../../src/RetailVision.Infrastructure/AuditRetention.cs), [обслуживание](../operations.md); 30 дней по умолчанию, автоочистка выключена |
| Дополнение 0.8: расписание backup и контроль хоста | Частично подтверждено | [systemd-шаблоны](../../deploy/systemd), [check-host](../../deploy/scripts/check-host.sh) проверены в CI; на реальном хосте не установлены, канал уведомлений не выбран |

## Что нужно для окончательной внешней приёмки

1. Новый размеченный набор целевых полок, отделённый по магазинам/автоматам; заранее зафиксированные критерии качества и поклассовая оценка. [Протокол](../model-acceptance.md).
2. Улучшение слабых SKU по новым данным. На знакомых 29 фото v2 имеет precision 95,4%, recall 80,8%, но recall M&M’s — 35,7%; среднее значение не скрывает эту проблему.
3. На выбранном VPS: запуск обученной модели, нагрузочная проверка, действующий сертификат и продление, backup/restore, таймеры и уведомления.

Эти пункты не мешают учебному показу реализованного приложения. Они мешают утверждать, что система уже принята для автономной работы в произвольном магазине.
