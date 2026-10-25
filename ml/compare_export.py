"""Compare PyTorch and ONNX on identical, full-letterbox inputs. This is export parity, not accuracy."""
import argparse, json
from pathlib import Path
import numpy as np
import cv2
from PIL import Image, ImageOps

def tensor(path):
    with Image.open(path) as source:
        image=np.array(ImageOps.exif_transpose(source).convert('RGB'))
    h,w=image.shape[:2]; scale=min(640/w,640/h); rw,rh=max(1,round(w*scale)),max(1,round(h*scale))
    output=np.full((640,640,3),114,dtype=np.uint8); left,top=(640-rw)//2,(640-rh)//2
    output[top:top+rh,left:left+rw]=cv2.resize(image,(rw,rh),interpolation=cv2.INTER_LINEAR)
    return np.ascontiguousarray(output.transpose(2,0,1)[None],dtype=np.float32)/255

def compare(pt,onnx,paths,confidence=.1,box_tolerance=1,score_tolerance=.001):
    import torch
    import onnxruntime as ort
    from ultralytics import YOLO
    if not Path(pt).is_file() or not Path(onnx).is_file(): raise ValueError('Supply local PyTorch and ONNX files')
    model=YOLO(pt).model.float().cpu().eval(); model.end2end=True
    session=ort.InferenceSession(str(onnx),providers=['CPUExecutionProvider'])
    if session.get_inputs()[0].shape != [1,3,640,640] or session.get_outputs()[0].shape != [1,300,6]: raise ValueError('Unsupported ONNX shapes')
    metadata=session.get_modelmeta().custom_metadata_map
    if metadata.get('end2end')!='True': raise ValueError('Expected end-to-end ONNX')
    if 'retailvision.classes' in metadata and json.loads(metadata['retailvision.classes']) != [model.names[i] for i in range(len(model.names))]: raise ValueError('PyTorch and ONNX class mappings differ')
    reports=[]
    for path in paths:
        x=tensor(path)
        with torch.no_grad(): prediction=model(torch.from_numpy(x))
        if isinstance(prediction,tuple): prediction=prediction[0]
        a=prediction.detach().numpy()[0]; b=session.run(None,{session.get_inputs()[0].name:x})[0][0]
        a=a[a[:,4]>=confidence]; b=b[b[:,4]>=confidence]
        free=set(range(len(b))); max_box=max_score=0.; passed=len(a)==len(b)
        for row in a:
            candidates=[i for i in free if b[i,5]==row[5]]
            if not candidates: passed=False; continue
            i=min(candidates,key=lambda j:float(np.max(np.abs(row[:4]-b[j,:4])))); free.remove(i)
            delta=float(np.max(np.abs(row[:4]-b[i,:4]))); score=float(abs(row[4]-b[i,4])); max_box=max(max_box,delta); max_score=max(max_score,score)
            passed=passed and delta<=box_tolerance and score<=score_tolerance
        reports.append({'image':str(path),'pt_count':len(a),'onnx_count':len(b),'max_box_delta_px':max_box,'max_score_delta':max_score,'passed':bool(passed and not free)})
    return reports

def main():
    p=argparse.ArgumentParser();p.add_argument('--weights',required=True);p.add_argument('--onnx',required=True);p.add_argument('--images',nargs='+',required=True)
    p.add_argument('--confidence',type=float,default=.1);p.add_argument('--box-tolerance',type=float,default=1);p.add_argument('--score-tolerance',type=float,default=.001);p.add_argument('--output',default='runs/export-parity.json')
    a=p.parse_args(); reports=compare(a.weights,a.onnx,[Path(x) for x in a.images],a.confidence,a.box_tolerance,a.score_tolerance)
    output=Path(a.output);output.parent.mkdir(parents=True,exist_ok=True);output.write_text(json.dumps({'kind':'export-parity-not-accuracy','results':reports},indent=2),encoding='utf-8')
    if not all(x['passed'] for x in reports): raise SystemExit('Export parity failed; see '+str(output))
    if not any(x['pt_count'] for x in reports): raise SystemExit('No detections above threshold: parity is inconclusive, use representative images')
    print('Export parity passed. This does not measure SKU detection accuracy.')

if __name__=='__main__': main()
