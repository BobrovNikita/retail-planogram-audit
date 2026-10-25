"""Export only the explicitly supported YOLO26 end-to-end contract and inspect the actual graph."""
import argparse, hashlib, json, shutil
from pathlib import Path

def inspect_and_package(path, classes, version, destination):
    import onnx
    import onnxruntime as ort
    import numpy as np
    graph=onnx.load(path)
    onnx.checker.check_model(graph)
    session=ort.InferenceSession(str(path),providers=['CPUExecutionProvider'])
    inputs,outputs=session.get_inputs(),session.get_outputs()
    if len(inputs)!=1 or inputs[0].shape!=[1,3,640,640] or inputs[0].type!='tensor(float)' or len(outputs)!=1 or outputs[0].shape!=[1,300,6] or outputs[0].type!='tensor(float)':
        raise ValueError('Unsupported graph: expected static float32 [1,3,640,640] -> [1,300,6]')
    metadata=session.get_modelmeta().custom_metadata_map
    if metadata.get('end2end')!='True' or metadata.get('task')!='detect': raise ValueError('Graph is not a supported end-to-end detection export')
    observed=session.run(None,{inputs[0].name:np.zeros((1,3,640,640),dtype=np.float32)})[0]
    if observed.shape!=(1,300,6) or not np.isfinite(observed).all(): raise ValueError('Probe inference failed')
    destination=Path(destination); destination.mkdir(parents=True,exist_ok=True)
    model_path=destination/'model.onnx'
    metadata.update({'retailvision.contract':'yolo26-xyxy-v1','retailvision.classes':json.dumps(classes),'retailvision.purpose':'sku-detection'})
    onnx.helper.set_model_props(graph,metadata)
    onnx.save(graph,str(model_path))
    manifest={'schemaVersion':1,'modelFile':'model.onnx','sha256':hashlib.sha256(model_path.read_bytes()).hexdigest(),'version':version,'purpose':'sku-detection','contract':'yolo26-xyxy-v1','preprocessing':'rgb-nchw-letterbox114-scale255','nms':'none','inputName':inputs[0].name,'outputName':outputs[0].name,'inputSize':640,'maxDetections':300,'classes':classes}
    (destination/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
    (destination/'inspection.json').write_text(json.dumps({'input':inputs[0].shape,'output':outputs[0].shape,'metadata':metadata,'opset':[(x.domain,x.version) for x in graph.opset_import]},indent=2),encoding='utf-8')
    return manifest

def main():
    p=argparse.ArgumentParser(); p.add_argument('--weights',required=True); p.add_argument('--classes',default='classes.json'); p.add_argument('--version',required=True); p.add_argument('--output',default='artifacts/model')
    a=p.parse_args()
    if not Path(a.weights).is_file() or Path(a.weights).suffix!='.pt': raise ValueError('Supply a local trained .pt checkpoint')
    from ultralytics import YOLO
    classes=json.loads(Path(a.classes).read_text(encoding='utf-8'))
    model=YOLO(a.weights)
    if model.task!='detect' or [model.names[i] for i in range(len(model.names))]!=classes: raise ValueError('Checkpoint class names must exactly match the SKU list')
    if not getattr(model.model,'end2end',False) and getattr(model.model.model[-1],'one2one_cv2',None) is None: raise ValueError('This checkpoint has no YOLO26 one-to-one head')
    path=model.export(format='onnx',imgsz=640,batch=1,dynamic=False,simplify=False,opset=17,nms=False,device='cpu')
    print(json.dumps(inspect_and_package(path,classes,a.version,a.output),indent=2))

if __name__=='__main__': main()
