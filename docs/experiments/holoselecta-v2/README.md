# HoloSelecta v2 — исправленный выбор checkpoint, 2026-10-26

**Локально подключена экспериментальная YOLO26s для восьми продуктов.** Валидация, экспорт, C#-инференс и сохранение REAL-отчёта проверены. Улучшение на известных снимках существенное, но независимая приёмка на новых полках/локациях ещё не выполнена.

## Что исправлено

При разборе v1 найдено расхождение: [Ultralytics 8.4.187 validator.py](https://github.com/ultralytics/ultralytics/blob/v8.4.187/ultralytics/engine/validator.py) включает end-to-end inference только при `nms is False`. Прежний train.py оставлял None, поэтому ранняя остановка и best.pt выбирались по one-to-many head. Экспорт и сайт потребляют one-to-one. Обучаются обе ветви, но их качество на ранних эпохах различается.

Теперь train.py всегда передаёт nms=False. Регрессионный тест защищает выбор нужной ветви. Дополнительно выбран YOLO26s и отключено горизонтальное отражение упаковок; добавлены повороты до 3° и perspective=0,0001. Это совокупный кандидат, а не эксперимент, который отдельно измеряет вклад каждого изменения. [План и критерий выбора](../holoselecta-v2-plan.json) зафиксированы до измерения нового кандидата на прежнем test. Начатый диагностический small80 с прежним nms=None остановлен, в сравнении не участвовал; small80-end2end обучен заново.

Прежнее объяснение разницы 97,7% val / 60,3% test только слабым переносом исправлено в [отчёте v1](../holoselecta-v1/README.md): это были разные головы модели. Измеренные ONNX/C#-результаты v1 остаются верными.

## Выбор по validation

Данные не менялись: те же 173 train / 41 val / 29 former-test, восемь классов, хэши и целые даты съёмки. Подготовка, происхождение, ограничения GTIN/размеров упаковок и лицензия описаны в [v1](../holoselecta-v1/README.md). Восстановленный кандидат nano-last добавлен до его оценки: исходный best.pt не был корректным способом отбора one-to-one head.

Все кандидаты сравнивались на val с one-to-one, FP32 и полным letterbox 640×640:

| Кандидат | Val mAP@50 | Val mAP@50–95 |
|---|---:|---:|
| Прежний YOLO26n best, эпоха 8 | 59,4% | 40,6% |
| YOLO26n last, эпоха 20 | 92,2% | 63,4% |
| **YOLO26s best, эпоха 18** | **94,9%** | **68,5%** |

Выбран максимальный val mAP50–95. [Протокол выбора и хэши](selection.json), [val выбранной модели](validation.json), [val nano-last](nano-last-validation.json). Показатели former-test при выборе не использовались.

Обучение: максимум 80 эпох, patience=20, batch=8, workers=2, imgsz=640, seed=42, nms=False; pretrained официальный yolo26s.pt. Ранняя остановка на эпохе 38, лучший checkpoint — 18; 217,5 с обучения без установки, подготовки и последующих проверок. RTX 3050 8 GiB; то же [окружение](../holoselecta-v1/gpu-environment.txt), что v1. [Полные аргументы](training-args.json), [эпохи](training.csv), [контрольные суммы](artifact-hashes.json).

## Проверка на прежних 29 снимках

После анализа v1 эти снимки рассматриваются как **известный набор для проверки регрессий**, не как новый независимый test. В файлах данных каталог по-прежнему называется test; в отчётах v2 роль явно помечена `known-regression-benchmark-not-new-independent-test`. Повторное использование не скрывается. Для production-приёмки нужен новый набор с разделением по автоматам/магазинам, а не только датам.

| Измерение на тех же изображениях | v1 | v2 |
|---|---:|---:|
| PyTorch mAP@50, квадратный FP32 | 60,3% | **95,6%** |
| PyTorch mAP@50–95 | 39,9% | **67,8%** |
| C# micro precision, confidence=0,5 / IoU=0,5 | 70,4% | **95,4%** |
| C# micro recall при том же пороге | 33,2% | **80,8%** |
| C# F1 | 45,1% | **87,5%** |
| C# TP / FP / FN | 76 / 32 / 153 | **185 / 9 / 44** |
| Медиана C# DetectAsync на локальном CPU | 81,1 мс | **155,6 мс** |

[Поклассовый mAP](known-benchmark.json), [поклассовые C#-метрики](native-metrics.json). Время включает подготовку тензора внутри DetectAsync, исключает первоначальное чтение/нормализацию файла; нагрузка VPS не измерялась. Цена более крупной модели — почти вдвое больший CPU latency; ONNX занимает 38 130 773 байта.

Оставшийся выраженный недостаток: M&M’s Peanut при confidence=0,5 — recall 35,7% (10/28 упаковок); Twix — 72,4% (21/29). Среднее улучшение не означает улучшения каждого SKU: M&M’s в v1 имел более высокий recall, хотя больше ошибочных срабатываний. Порог оставлен заранее заданным 50%, по этим 29 изображениям не подбирался. Неуверенные кандидаты от 10% сайт сохраняет для ручного рассмотрения.

Экспорт статический FP32 `[1,3,640,640] → [1,300,6]`, opset=17, end-to-end, nms=False. PyTorch/ONNX parity прошла на всех 29 изображениях и одном горизонтальном train-кадре: максимум расхождения 0,0000611 px / 0,00001094 confidence. [Отчёт](export-parity.json). Native probe затем обработал все 29 фото через настоящий C# и SkiaSharp; метрики рассчитаны по исходной разметке, не по предсказаниям Python.

## Воспроизведение и локальный сайт

После подготовки датасета по [ML-инструкции](../../../ml/README.md), из корня репозитория (PowerShell; $runRoot — каталог эксперимента):

```powershell
python ml/train.py --data "$runRoot/prepared/data.yaml" --groups "$runRoot/prepared/capture-groups.csv" --classes "$runRoot/prepared/classes.json" --weights yolo26s.pt --epochs 80 --patience 20 --batch 8 --workers 2 --device 0 --fliplr 0 --degrees 3 --perspective 0.0001 --project "$runRoot/runs-v2" --name small80-end2end
python ml/validate.py --weights "$runRoot/runs-v2/small80-end2end/weights/best.pt" --data "$runRoot/prepared/data.yaml" --groups "$runRoot/prepared/capture-groups.csv" --classes "$runRoot/prepared/classes.json" --split val --device cpu --output "$runRoot/validation-v2.json"
python ml/export_onnx.py --weights "$runRoot/runs-v2/small80-end2end/weights/best.pt" --classes "$runRoot/prepared/classes.json" --version holoselecta-snacks-v2-experimental --output "$runRoot/model-v2"
```

Далее обязательны compare_export.py и RetailVision.ModelProbe/evaluate_native.py по ML-инструкции. После проверки укажите Vision__ManifestPath на новый manifest, сохраните явное Vision__ModelNotice и перезапустите приложение. Каталог и контрольная планограмма v1 совместимы; новый запуск требует нового черновика. Все восемь SKU и их порядок не менялись. Прежние отчёты v1 остаются неизменными.

Локально через защищённые контроллеры выполнены новый upload → калибровка → REAL-анализ → автосохранение отчёта для той же контрольной фотографии/планограммы. В истории доступны обе версии. Настройка ModelNotice прямо говорит, что набор уже использовался и нужны новые независимые снимки. Исходные данные и веса не публикуются в Git; пользователю подготовлен отдельный локальный ZIP с ONNX, checkpoint, каталогом, примером и условиями использования.

Данные и производные изображения: HoloSelecta v1, Klaus Fuchs, Tobias Grundmann, Mirella Haldimann, Elgar Fleisch (2020), DOI 10.17632/gz39ggf35n.1, CC BY 4.0. Изменения данных — отбор восьми классов, VOC→YOLO, уменьшение и вырезание иллюстраций из train. Ultralytics/исходные веса имеют отдельные условия AGPL-3.0 либо коммерческой лицензии. Готовность к работе на произвольном ассортименте, новых магазинах и публичном VPS не заявляется.
