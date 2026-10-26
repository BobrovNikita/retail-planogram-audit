"""Prepare the pinned public HoloSelecta archive without executing its Python/pickle files."""
import argparse
import collections
import csv
import hashlib
import io
import json
import math
import re
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path, PurePosixPath

import yaml
from dataset_check import check


def sha256(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def date_splits(profile):
    result = {}
    for split in ('train', 'val', 'test'):
        for date in profile['dates'][split]:
            if not re.fullmatch(r'20\d{6}', date) or date in result:
                raise ValueError('Invalid or overlapping capture date: ' + date)
            result[date] = split
    return result


def yolo_box(box, width, height):
    """VOC inclusive one-based minima -> zero-based edge coordinates; no silent clipping."""
    xmin, ymin, xmax, ymax = box
    if not all(math.isfinite(v) for v in box) or not (0 <= xmin < xmax <= width and 0 <= ymin < ymax <= height):
        raise ValueError('Invalid VOC box')
    xmin, ymin = max(0, xmin - 1), max(0, ymin - 1)
    return ((xmin + xmax) / (2 * width), (ymin + ymax) / (2 * height),
            (xmax - xmin) / width, (ymax - ymin) / height)


def safe_members(archive):
    result = {}
    for info in archive.infolist():
        path = PurePosixPath(info.filename)
        if (len(path.parts) != 2 or path.parts[0] != 'FinalDataset' or
                '\\' in info.filename or ':' in info.filename or '..' in path.parts or
                (info.external_attr >> 16) & 0o170000 == 0o120000):
            raise ValueError('Unexpected archive entry: ' + info.filename)
        if path.name in result:
            raise ValueError('Duplicate archive entry: ' + path.name)
        result[path.name] = info
    return result


def prepare(archive_path, profile_path, output):
    from PIL import Image
    archive_path, output = Path(archive_path), Path(output).resolve()
    profile = json.loads(Path(profile_path).read_text(encoding='utf-8'))
    if sha256(archive_path) != profile['archiveSha256']:
        raise ValueError('Archive SHA256 differs from the pinned source')
    splits = date_splits(profile)
    products = profile['products']
    classes = [p['sku'] for p in products]
    labels = {p['label']: i for i, p in enumerate(products)}
    if len(set(classes)) != len(classes) or len(labels) != len(classes) or not classes:
        raise ValueError('Duplicate/empty product mapping')
    if output.exists():
        raise ValueError('Output must be a new directory; refusing to mix dataset versions')
    output.mkdir(parents=True)
    counts = {s: collections.Counter() for s in ('train', 'val', 'test')}
    records, excluded, groups, crop_candidates = [], [], [], {}
    with zipfile.ZipFile(archive_path) as archive:
        members = safe_members(archive)
        for xml_name in sorted(n for n in members if n.endswith('.xml')):
            if members[xml_name].file_size > 1024 * 1024:
                raise ValueError('Oversize annotation')
            xml = archive.read(members[xml_name])
            if b'<!DOCTYPE' in xml.upper() or b'<!ENTITY' in xml.upper():
                raise ValueError('XML entities are not allowed')
            annotation = ET.fromstring(xml)
            name = annotation.findtext('filename') or ''
            reason = profile.get('exclude', {}).get(name)
            date_match = re.search(r'(20\d{6})_', name)
            date = date_match.group(1) if date_match else None
            if name not in members:
                reason = 'Missing image in published archive'
            elif date not in splits:
                reason = 'Unknown/unassigned capture date; potential derivative'
            if reason:
                excluded.append({'annotation': xml_name, 'image': name, 'reason': reason})
                continue
            if members[name].file_size > 100 * 1024 * 1024:
                raise ValueError('Oversize image')
            source = archive.read(members[name])
            with Image.open(io.BytesIO(source)) as decoded:
                width, height = decoded.size
                declared = tuple(int(annotation.findtext('size/' + k)) for k in ('width', 'height'))
                if declared != decoded.size or decoded.getexif().get(274) not in (None, 1):
                    excluded.append({'annotation': xml_name, 'image': name, 'reason': 'Image/XML size or orientation mismatch'})
                    continue
                image = decoded.convert('RGB')
            split = splits[date]
            boxes = []
            for obj in annotation.findall('object'):
                box = [float(obj.findtext('bndbox/' + k)) for k in ('xmin', 'ymin', 'xmax', 'ymax')]
                normalized = yolo_box(box, width, height)
                cls = labels.get(obj.findtext('name'))
                if cls is not None:
                    boxes.append((cls, normalized))
                    counts[split][classes[cls]] += 1
                    # Catalog illustrations come exclusively from train; preserve source provenance.
                    area = (box[2] - box[0]) * (box[3] - box[1])
                    if split == 'train' and area > crop_candidates.get(cls, {}).get('area', 0):
                        crop = image.crop((max(0, box[0]-1), max(0, box[1]-1), box[2], box[3]))
                        crop.thumbnail((320, 320))
                        directory = output / 'catalog'; directory.mkdir(exist_ok=True)
                        crop.save(directory / (classes[cls] + '.png'))
                        crop_candidates[cls] = {'area': area, 'sourceImage': name, 'sourceBox': box}
            image.thumbnail((profile['maxImageSide'], profile['maxImageSide']), Image.Resampling.LANCZOS)
            relative = f'images/{split}/{Path(name).stem}.jpg'
            destination = output / relative
            destination.parent.mkdir(parents=True, exist_ok=True)
            image.save(destination, quality=95, subsampling=0)
            label_path = output / 'labels' / split / (Path(name).stem + '.txt')
            label_path.parent.mkdir(parents=True, exist_ok=True)
            label_path.write_text('\n'.join(str(cls) + ' ' + ' '.join(f'{v:.9f}' for v in box) for cls, box in boxes) + '\n', encoding='utf-8')
            capture = 'date-' + date
            groups.append([relative, capture, split])
            records.append({'image': relative, 'sourceImage': name, 'captureId': capture, 'split': split,
                            'sourceSha256': hashlib.sha256(source).hexdigest(), 'imageSha256': sha256(destination),
                            'labelSha256': sha256(label_path), 'sourceSize': [width, height],
                            'preparedSize': list(image.size), 'boxes': len(boxes)})
    for split, class_counts in counts.items():
        if any(class_counts[c] == 0 for c in classes):
            raise ValueError('Selected class absent from ' + split)
    (output / 'classes.json').write_text(json.dumps(classes, indent=2), encoding='utf-8')
    (output / 'profile.json').write_text(json.dumps(profile, indent=2, ensure_ascii=False), encoding='utf-8')
    data = {'path': '.', 'train': 'images/train', 'val': 'images/val', 'test': 'images/test', 'names': classes}
    (output / 'data.yaml').write_text(yaml.safe_dump(data, sort_keys=False), encoding='utf-8')
    with (output / 'capture-groups.csv').open('w', newline='', encoding='utf-8') as stream:
        writer = csv.writer(stream); writer.writerow(['image', 'capture_id', 'split']); writer.writerows(groups)
    report = {'profile': profile['name'], 'source': profile['sourceUrl'], 'archiveSha256': profile['archiveSha256'],
              'license': profile['license'], 'attribution': profile['attribution'],
              'splitPolicy': 'Entire capture dates; same machines may recur across dates. Not an unseen-store benchmark.',
              'labelPolicy': 'Only the eight selected labels are targets; other products are background. No generic product detection.',
              'boxPolicy': 'VOC one-based inclusive minima converted to zero-based edges; normalized before aspect-preserving resize.',
              'counts': check(output/'data.yaml', output/'classes.json', output/'capture-groups.csv'),
              'boxesByClass': counts, 'excluded': excluded, 'images': records,
              'catalogImages': {classes[k]: v for k, v in crop_candidates.items()}}
    (output / 'preparation-report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8')
    print(json.dumps({'counts': report['counts'], 'boxesByClass': counts, 'excluded': len(excluded)}, indent=2))
    return report


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', required=True)
    parser.add_argument('--profile', default=str(Path(__file__).parent/'profiles/holoselecta-snacks.json'))
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    prepare(args.archive, args.profile, args.output)
