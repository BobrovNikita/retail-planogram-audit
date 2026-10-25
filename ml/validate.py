import argparse, json
from pathlib import Path
from dataset_check import check, resolved_data

def main():
    p=argparse.ArgumentParser(); p.add_argument('--weights',required=True); p.add_argument('--data',required=True); p.add_argument('--groups',required=True)
    p.add_argument('--classes',default='classes.json'); p.add_argument('--split',choices=['val','test'],default='test'); p.add_argument('--device',default='0'); p.add_argument('--output',default='runs/validation.json')
    a=p.parse_args(); check(a.data,a.classes,a.groups)
    if not Path(a.weights).is_file(): raise ValueError('Supply locally trained weights')
    from ultralytics import YOLO
    model=YOLO(a.weights)
    classes=json.loads(Path(a.classes).read_text(encoding='utf-8'))
    if [model.names[i] for i in range(len(model.names))] != classes: raise ValueError('Weights do not match SKU mapping')
    metrics=model.val(data=resolved_data(a.data,Path(a.output).parent/'resolved-data.yaml'),split=a.split,imgsz=640,device=a.device,nms=False,plots=True)
    output=Path(a.output); output.parent.mkdir(parents=True,exist_ok=True)
    output.write_text(json.dumps({'weights':str(Path(a.weights).resolve()),'split':a.split,'metrics':metrics.results_dict,'speed_ms':metrics.speed},indent=2),encoding='utf-8')

if __name__=='__main__': main()
