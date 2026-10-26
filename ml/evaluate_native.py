"""Evaluate the actual C# pipeline at fixed confidence/IoU, without tuning on test."""
import argparse
import collections
import json
from pathlib import Path
import statistics


def iou(a, b):
    intersection = max(0, min(a[2], b[2])-max(a[0], b[0])) * max(0, min(a[3], b[3])-max(a[1], b[1]))
    union = (a[2]-a[0])*(a[3]-a[1]) + (b[2]-b[0])*(b[3]-b[1]) - intersection
    return intersection / union if union > 0 else 0


def match(predictions, truth, confidence=.5, overlap=.5):
    remaining = set(range(len(truth)))
    counts = collections.defaultdict(lambda: collections.Counter(tp=0, fp=0, fn=0))
    for prediction in sorted(predictions, key=lambda p: p['confidence'], reverse=True):
        if prediction['confidence'] < confidence:
            continue
        sku = prediction['sku']
        box = (prediction['x'], prediction['y'], prediction['x']+prediction['width'], prediction['y']+prediction['height'])
        candidates = [(iou(box, truth[i][1]), i) for i in remaining if truth[i][0] == sku]
        best = max(candidates, default=(0, -1))
        if best[0] >= overlap:
            remaining.remove(best[1]); counts[sku]['tp'] += 1
        else:
            counts[sku]['fp'] += 1
    for i in remaining:
        counts[truth[i][0]]['fn'] += 1
    return counts


def rates(counts):
    tp, fp, fn = (counts[k] for k in ('tp', 'fp', 'fn'))
    return dict(counts, precision=tp/(tp+fp) if tp+fp else 0, recall=tp/(tp+fn) if tp+fn else 0,
                f1=2*tp/(2*tp+fp+fn) if 2*tp+fp+fn else 0)


def evaluate(report_path, dataset, output, confidence=.5, overlap=.5):
    report = json.loads(Path(report_path).read_text(encoding='utf-8'))
    dataset = Path(dataset)
    classes = json.loads((dataset/'classes.json').read_text(encoding='utf-8'))
    expected = {p.name for p in (dataset/'images/test').glob('*.jpg')}
    names = [r['image'] for r in report['results']]
    if len(names) != len(set(names)) or set(names) != expected:
        raise ValueError('Native report must cover every test image exactly once')
    total = {sku: collections.Counter(tp=0, fp=0, fn=0) for sku in classes}
    for row in report['results']:
        truth = []
        for line in (dataset/'labels/test'/Path(row['image']).with_suffix('.txt')).read_text().splitlines():
            if not line.strip(): continue
            cls, x, y, w, h = line.split(); x, y, w, h = map(float, (x, y, w, h))
            truth.append((classes[int(cls)], (x-w/2, y-h/2, x+w/2, y+h/2)))
        for sku, counts in match(row['detection']['products'], truth, confidence, overlap).items():
            total[sku].update(counts)
    micro = collections.Counter(tp=0, fp=0, fn=0)
    for counts in total.values(): micro.update(counts)
    result = {'kind': 'native-test-fixed-threshold', 'model': report['model'], 'sha256': report['sha256'],
              'images': len(names), 'confidence': confidence, 'iou': overlap, 'micro': rates(micro),
              'perClass': {sku: rates(c) for sku, c in total.items()},
              'inferenceMedianMs': statistics.median(r['inferenceMs'] for r in report['results']),
              'inferenceMaxMs': max(r['inferenceMs'] for r in report['results'])}
    Path(output).write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--report', required=True); parser.add_argument('--dataset', required=True)
    parser.add_argument('--output', required=True)
    parser.add_argument('--confidence', type=float, default=.5); parser.add_argument('--iou', type=float, default=.5)
    args = parser.parse_args()
    if not 0 < args.confidence <= 1 or not 0 < args.iou <= 1: parser.error('Thresholds must be in (0, 1]')
    evaluate(args.report, args.dataset, args.output, args.confidence, args.iou)
