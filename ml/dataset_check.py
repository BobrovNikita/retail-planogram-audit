"""Validate labels and require capture-group separation before training/evaluation."""
import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
import yaml

def resolved_data(data_path, output):
    data_path=Path(data_path).resolve()
    data=yaml.safe_load(data_path.read_text(encoding='utf-8'))
    data['path']=str((data_path.parent/data['path']).resolve())
    output=Path(output).resolve();output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(yaml.safe_dump(data,sort_keys=False),encoding='utf-8')
    return str(output)

def check(data_path, classes_path, groups_path):
    data_path = Path(data_path).resolve()
    data = yaml.safe_load(data_path.read_text(encoding='utf-8'))
    classes = json.loads(Path(classes_path).read_text(encoding='utf-8'))
    names = data['names']
    if isinstance(names, dict):
        if sorted(names) != list(range(len(names))):
            raise ValueError('Dataset class indices must be contiguous from zero')
        names = [names[i] for i in range(len(names))]
    if names != classes or not classes or len(set(classes)) != len(classes):
        raise ValueError('data.yaml names must exactly match classes.json in class-index order')
    root = (data_path.parent / data['path']).resolve()
    with Path(groups_path).open(encoding='utf-8', newline='') as f:
        records = list(csv.DictReader(f))
    captures, declared = {}, {}
    for row in records:
        relative = row['image'].replace('\\', '/')
        group, split = row['capture_id'].strip(), row['split']
        if not group or split not in ('train','val','test') or relative in declared:
            raise ValueError('Invalid or duplicate capture-group row')
        if group in captures and captures[group] != split:
            raise ValueError('Capture session crosses splits: ' + group)
        captures[group] = split
        declared[relative] = split
    hashes, seen, counts = {}, set(), {}
    for split in ('train','val','test'):
        folder = (root / data[split]).resolve()
        if not folder.is_relative_to(root) or not folder.is_dir():
            raise ValueError('Split must be a directory inside dataset root: '+split)
        images = sorted(p for p in folder.rglob('*') if p.suffix.lower() in ('.jpg','.jpeg','.png','.webp'))
        if not images: raise ValueError('Empty split: '+split)
        counts[split] = len(images)
        for image in images:
            relative = image.relative_to(root).as_posix()
            if declared.get(relative) != split: raise ValueError('Missing/wrong capture group: '+relative)
            seen.add(relative)
            digest = hashlib.sha256(image.read_bytes()).hexdigest()
            if digest in hashes and hashes[digest] != split: raise ValueError('Identical image across splits: '+relative)
            hashes[digest] = split
            label = root / 'labels' / split / image.relative_to(folder).with_suffix('.txt')
            if not label.is_file(): raise ValueError('Missing annotation (empty file required for negatives): '+str(label))
            for line in label.read_text(encoding='utf-8').splitlines():
                if not line.strip(): continue
                values = line.split()
                if len(values) != 5: raise ValueError('Expected class x_center y_center width height: '+str(label))
                cls = int(values[0]); x,y,w,h = map(float,values[1:])
                if cls < 0 or cls >= len(classes) or not all(math.isfinite(v) for v in (x,y,w,h)) or w<=0 or h<=0 or min(x-w/2,y-h/2)<-1e-6 or max(x+w/2,y+h/2)>1+1e-6:
                    raise ValueError('Invalid YOLO box: '+str(label))
    if seen != set(declared): raise ValueError('Capture CSV contains files not present in dataset splits')
    return counts

if __name__ == '__main__':
    p=argparse.ArgumentParser(); p.add_argument('--data',required=True); p.add_argument('--classes',default='classes.json'); p.add_argument('--groups',required=True)
    a=p.parse_args(); print(json.dumps(check(a.data,a.classes,a.groups),indent=2))
