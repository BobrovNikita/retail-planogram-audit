# Обучение и экспорт SKU-модели

Python нужен локально для обучения и подготовки артефактов. Веб-приложение выполняет ONNX на CPU и Python не запускает. Обученных весов и датасета в репозитории нет. Общие COCO-классы не считаются распознаванием конкретных SKU.

## Окружение

Проверены Python 3.12.14, Ultralytics 8.4.187, ONNX 1.23.2, ONNX Runtime 1.31.0; PyTorch 2.14.1 CPU и 2.14.1+cu130 на RTX 3050. Для другой NVIDIA подберите совместимую сборку PyTorch/CUDA по официальной инструкции PyTorch. requirements.txt фиксирует основные ML-зависимости; полный состав первого GPU-окружения сохранён в отчёте HoloSelecta.

```sh
cd ml
python -m venv .venv
# Активируйте .venv для своей оболочки; установите подходящие torch и torchvision.
python -m pip install -r requirements.txt
```

Ultralytics и исходные веса имеют собственные условия лицензирования (AGPL-3.0 / коммерческая лицензия); они не переоформляются лицензией исходников RetailVision. Проверьте условия выбранного использования: https://www.ultralytics.com/license . Фото и разметка должны иметь разрешённые права использования.

## Датасет

Скопируйте data.example.yaml в data.yaml. Путь path трактуется относительно этого YAML; скрипты сохранят отдельный YAML с абсолютным корнем для согласованной работы Ultralytics. Список names обязан точно совпадать с classes.json по порядку. Индекс массива — индекс класса, строка — SKU каталога.

```text
datasets/retail/
  images/train/*.jpg
  images/val/*.jpg
  images/test/*.jpg
  labels/train/*.txt
  labels/val/*.txt
  labels/test/*.txt
```

Для каждой видимой упаковки разметьте прямоугольник и конкретный SKU. Содержимое YOLO txt: classIndex centerX centerY width height, нормализованное к исходному снимку. Для пустого изображения нужен пустой txt. Включайте реальные условия: блики, частичные перекрытия, соседние SKU и разные расстояния; не используйте один и тот же кадр в нескольких выборках.

Создайте capture-groups.csv по примеру capture-groups.example.csv. image — путь от корня датасета, capture_id — независимая съёмочная сессия, split — train/val/test. Все кадры одной витрины из одной серии, соседние видео-кадры и их производные должны находиться в одном split. Автоматическая проверка находит повторные хэши и пересечение capture_id, но качество группировки задаёт разметчик; похожие кадры с разными именами групп нужно проверять вручную. Test оставляйте для окончательной оценки, не подбирайте по нему параметры.

```sh
python dataset_check.py --data data.yaml --classes classes.json --groups capture-groups.csv
python train.py --data data.yaml --groups capture-groups.csv --weights yolo26n.pt --epochs 100 --batch 8 --device 0 --patience 20
python validate.py --weights runs/train/train/weights/best.pt --data data.yaml --groups capture-groups.csv --split test --device 0
```

train.py использует transfer learning; начальный checkpoint yolo26n.pt при отсутствии может быть загружен библиотекой из официального источника. Результаты и лучшие веса сохраняются Ultralytics в runs; имя очередного каталога уточняйте в журнале. --workers по умолчанию 0 для безопасного запуска в Windows, --name задаёт имя эксперимента. validate.py использует полный квадратный letterbox 640×640/FP32, сохраняет общие и поклассовые метрики и время в JSON. Тяжёлое обучение на VPS не выполняется.

## Экспорт и проверка

```sh
python export_onnx.py --weights runs/train/train/weights/best.pt --classes classes.json --version retail-sku-v1 --output artifacts/retail-sku-v1
python compare_export.py --weights runs/train/train/weights/best.pt --onnx artifacts/retail-sku-v1/model.onnx --images datasets/retail/images/test/example.jpg --output runs/export-parity.json
```

Экспорт требует локальный .pt с правильными именами классов. Фиксируется one-to-one head nms=False, статический FP32 input 1×3×640×640 и output 1×300×6. После экспорта проверяется настоящий граф и пробный запуск. Для другого формата сначала реализуйте отдельный парсер и тесты, не изменяйте только manifest.

compare_export.py подаёт один и тот же подготовленный тензор в PyTorch и ONNX, сопоставляет результаты по классу и координатам независимо от порядка строк. По умолчанию порог 0,1, допуск координат 1 px входа модели и confidence 0,001; они задаются флагами. Скрипт завершается с ошибкой при несовпадении и при отсутствии любых детекций. Это проверка экспорта, не mAP. Используйте несколько репрезентативных изображений с разными пропорциями и плотностью выкладки. Проверяйте C#-инференс дополнительно: его линейная интерполяция SkiaSharp может немного отличаться от OpenCV.

В production копируются только проверенные model.onnx и manifest.json в закрытый каталог; Vision__ManifestPath указывает на manifest. Перезапустите сайт и создайте новый черновик. Не используйте tests/Fixtures/synthetic.onnx: это маленький технический граф, не модель товаров; production его отклоняет.

## Проверки без обучения

```sh
python -m unittest discover -s tests -v
python generate_test_fixture.py
```

Первое проверяет разбиение датасета и разметку. Второе воспроизводит маленький input-dependent ONNX-граф для .NET-тестов с явной меткой integration-test. Не заменяйте его реальными весами и не подключайте к обычному сайту.

Контракт, ограничения, результаты исследования архитектуры и настройка сайта: [docs/ml-pipeline.md](../docs/ml-pipeline.md).

## Профиль HoloSelecta

Первый реальный эксперимент: [model card, результаты и ограничения](../docs/experiments/holoselecta-v1/README.md). Профиль содержит восемь упаковок и фиксированные даты train/val/test. Каталог DEMO в classes.json не заменяется: во все команды нужно передать отдельный --classes.

Пример PowerShell из корня репозитория с активированным Python 3.12 venv. Каталог эксперимента выбирайте вне Git; prepared должен быть новым. Архив около 2 ГБ, установке CUDA-сборки также требуется несколько ГБ.

```powershell
python -m pip install torch==2.14.1+cu130 torchvision==0.29.1+cu130 --index-url https://download.pytorch.org/whl/cu130
python -m pip install -r ml/requirements.txt
$runRoot = Join-Path (Resolve-Path ..).Path 'holoselecta-experiment'
New-Item -ItemType Directory -Force $runRoot | Out-Null
$profile = Get-Content ml/profiles/holoselecta-snacks.json -Raw | ConvertFrom-Json
curl.exe --fail --location --retry 3 --output "$runRoot/source.zip" $profile.downloadUrl
if ($LASTEXITCODE -ne 0) { throw 'Dataset download failed' }
python ml/prepare_holoselecta.py --archive "$runRoot/source.zip" --output "$runRoot/prepared"
if ($LASTEXITCODE -ne 0) { throw 'Dataset preparation failed' }
$env:YOLO_CONFIG_DIR = "$runRoot/yolo-settings"
python -c "from ultralytics import settings; settings.update({k:False for k in ('sync','clearml','comet','dvc','mlflow','raytune','wandb')})"
python ml/train.py --data "$runRoot/prepared/data.yaml" --groups "$runRoot/prepared/capture-groups.csv" --classes "$runRoot/prepared/classes.json" --epochs 50 --patience 12 --batch 8 --workers 2 --device 0 --project "$runRoot/runs" --name baseline50
python ml/validate.py --weights "$runRoot/runs/baseline50/weights/best.pt" --data "$runRoot/prepared/data.yaml" --groups "$runRoot/prepared/capture-groups.csv" --classes "$runRoot/prepared/classes.json" --split test --output "$runRoot/test.json"
python ml/export_onnx.py --weights "$runRoot/runs/baseline50/weights/best.pt" --classes "$runRoot/prepared/classes.json" --version holoselecta-snacks-v1-baseline --output "$runRoot/model"
$parityImages = @(Get-ChildItem "$runRoot/prepared/images/test/*.jpg" | ForEach-Object FullName)
python ml/compare_export.py --weights "$runRoot/runs/baseline50/weights/best.pt" --onnx "$runRoot/model/model.onnx" --images $parityImages --output "$runRoot/parity.json"
dotnet run --project tools/RetailVision.ModelProbe -c Release -- "$runRoot/model/manifest.json" "$runRoot/prepared/images/test" "$runRoot/native.json"
python ml/evaluate_native.py --report "$runRoot/native.json" --dataset "$runRoot/prepared" --output "$runRoot/native-metrics.json"
```

Подготовка проверяет SHA256 исходного ZIP, безопасные имена его записей, реальные размеры/ориентацию фото и геометрию рамок; Python/pickle из архива не запускаются. Выход включает data.yaml, classes.json, capture-groups.csv, иллюстрации каталога из train и preparation-report.json с хэшами, исключениями и количеством объектов каждого класса. Повторная запись в существующий каталог запрещена. Зависимость Pillow закреплена для воспроизводимой обработки изображений.

Остановитесь при ошибке любой команды; не подключайте непроверенный артефакт. Probe использует настоящие PhotoPreprocessor и OnnxProductDetector, отклоняет TEST-модели и не перезаписывает существующий JSON. evaluate_native.py требует все test-фото ровно по одному разу и считает micro P/R/F1 при фиксированных confidence=0,5/IoU=0,5. Это дополнительная проверка рабочего пути, она не заменяет mAP и независимый набор целевых полок.

Чтобы включить экспериментальную модель на сайте, создайте восемь товаров с точными SKU из профиля, загрузите `prepared/catalog/<SKU>.png`, импортируйте контрольную планограмму из model card и задайте:

```powershell
$env:Vision__ManifestPath = "$runRoot/model/manifest.json"
$env:Vision__ModelNotice = 'Экспериментальная модель HoloSelecta. Качество недостаточно для автоматической приёмки; требуется ручная проверка.'
dotnet run --project src/RetailVision.Web -c Release --no-build
```

ModelNotice выводится как обычный экранированный текст рядом с версией модели. Копирование новых весов требует перезапуска и нового черновика. REAL не означает высокую точность; фактические ограничения первой модели перечислены в model card. На новом окружении отсутствие обученного файла по-прежнему оставляет распознавание выключенным.
