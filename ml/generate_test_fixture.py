"""Tiny input-dependent ONNX graph for .NET integration tests. Not a learned detector."""
from pathlib import Path
import hashlib,json
import numpy as np
import onnx
from onnx import helper,TensorProto,numpy_helper

def generate(destination):
    destination=Path(destination);destination.mkdir(parents=True,exist_ok=True)
    rows=np.zeros((1,300,6),dtype=np.float32);rows[0,0]=[64,200,128,360,.9,0]
    mask=np.zeros_like(rows);mask[:,:,4]=.01
    nodes=[helper.make_node('ReduceMean',['images'],['mean'],keepdims=0),helper.make_node('Mul',['mean','mask'],['delta']),helper.make_node('Add',['base','delta'],['output0'])]
    graph=helper.make_graph(nodes,'RetailVision synthetic contract test',[helper.make_tensor_value_info('images',TensorProto.FLOAT,[1,3,640,640])],[helper.make_tensor_value_info('output0',TensorProto.FLOAT,[1,300,6])],[numpy_helper.from_array(rows,'base'),numpy_helper.from_array(mask,'mask')])
    model=helper.make_model(graph,opset_imports=[helper.make_opsetid('',17)],ir_version=9)
    classes=['RV-COLA','RV-LIME','RV-ORANGE','RV-WATER','RV-TEA','RV-JUICE']
    helper.set_model_props(model,{'task':'detect','end2end':'True','retailvision.contract':'yolo26-xyxy-v1','retailvision.purpose':'integration-test','retailvision.classes':json.dumps(classes)})
    onnx.checker.check_model(model); path=destination/'synthetic.onnx';onnx.save(model,path)
    manifest={'modelFile':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'version':'synthetic-contract-test','purpose':'integration-test','classes':classes}
    (destination/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')

if __name__=='__main__': generate(Path(__file__).resolve().parents[1]/'tests'/'Fixtures')
