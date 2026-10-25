# Обучение и экспорт SKU-модели

Python нужен локально для обучения и подготовки артефактов. Веб-приложение выполняет ONNX на CPU и Python не запускает. Обученных весов и датасета в репозитории нет. Общие COCO-классы не считаются распознаванием конкретных SKU.

## Окружение

Проверены Python 3.12.14, Ultralytics 8.4.187, ONNX 1.23.2, ONNX Runtime 1.31.0; для проверки графа использован PyTorch 2.14.1 CPU. Для обучения установите сборку PyTorch/CUDA для своей NVIDIA по официальной инструкции PyTorch. requirements.txt фиксирует основные ML-зависимости, но не является полным lock-файлом окружения GPU.

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

train.py использует transfer learning; начальный checkpoint yolo26n.pt при отсутствии может быть загружен библиотекой из официального источника. Результаты и лучшие веса сохраняются Ultralytics в runs; имя очередного каталога уточняйте в журнале. validate.py сохраняет фактически измеренные метрики и время в JSON. Тяжёлое обучение на VPS не выполняется.

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
