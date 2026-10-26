import argparse
from pathlib import Path
from dataset_check import check, resolved_data

def main():
    p=argparse.ArgumentParser(description='Local SKU training; no web-server dependency.')
    p.add_argument('--data',required=True); p.add_argument('--groups',required=True); p.add_argument('--classes',default='classes.json')
    p.add_argument('--weights',default='yolo26n.pt'); p.add_argument('--epochs',type=int,default=100); p.add_argument('--batch',type=int,default=8)
    p.add_argument('--device',default='0'); p.add_argument('--patience',type=int,default=20); p.add_argument('--project',default='runs/train')
    p.add_argument('--workers',type=int,default=0); p.add_argument('--name',default='train')
    p.add_argument('--fliplr',type=float,default=.5,help='Horizontal flip probability; use 0 for readable packaging')
    p.add_argument('--degrees',type=float,default=0); p.add_argument('--perspective',type=float,default=0)
    a=p.parse_args(); check(a.data,a.classes,a.groups)
    from ultralytics import YOLO
    model=YOLO(a.weights)
    # Ultralytics' nms=None validates the one-to-many head. Select checkpoints using
    # the one-to-one head that export_onnx.py and the C# runtime actually consume.
    model.train(data=resolved_data(a.data,Path(a.project)/'resolved-data.yaml'),imgsz=640,epochs=a.epochs,batch=a.batch,device=a.device,patience=a.patience,project=str(Path(a.project).resolve()),seed=42,workers=a.workers,name=a.name,nms=False,fliplr=a.fliplr,degrees=a.degrees,perspective=a.perspective)

if __name__=='__main__': main()
