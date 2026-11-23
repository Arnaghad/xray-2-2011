"""SDK B6 mayapy frontend. Sources are evaluated in an isolated working copy."""
import argparse
import json
from pathlib import Path
import shutil
import sys

HERE=Path(__file__).resolve().parent
sys.path.insert(0,str(HERE))
sys.path.insert(0,str(HERE.parent.parent))


def run(request):
    import maya.standalone
    maya.standalone.initialize(name='python')
    import maya.cmds as cmds
    cmds.loadPlugin(str(HERE/'xray_maya_plugin.py'))
    import static_extractor as static
    import skeletal_extractor as skeletal
    import physics_extractor as physics
    from dcc_common.cook_bridge import run_cook_cli
    output=Path(request['output']).resolve();source=Path(request['source']).resolve(strict=True)
    if source.suffix.lower() not in {'.ma','.mb'}: raise ValueError('Maya requires .ma or .mb source')
    if source==output or source.is_relative_to(output) or output.is_relative_to(source): raise ValueError('Source and staging must be separate')
    output.mkdir(parents=True,exist_ok=True)
    working=output/('evaluation'+source.suffix);shutil.copyfile(source,working)
    cmds.file(str(working),open=True,force=True,ignoreVersion=True,executeScriptNodes=False,loadReferenceDepth='none')
    origin=Path(request.get('origin_directory') or source.parent)
    for node in cmds.ls(type='reference') or []:
        if node=='sharedReferenceNode': continue
        reference=Path(cmds.referenceQuery(node,filename=True,withoutCopyNumber=True))
        if not reference.is_absolute(): reference=origin/reference
        if not reference.is_file(): raise ValueError('Missing Maya reference: '+str(reference))
        cmds.file(str(reference),loadReference=node,executeScriptNodes=False)
    exchange=output/'exchange';root=request.get('object') or None;kind=request['kind']
    response=dict(schema='xray-sdk-dcc-result/v1',kind=kind,source=str(source),object=root or '')
    if kind=='static_model': static.extract_static_scene_to_exchange(root or '',exchange,logical_name='asset',allow_direct_root=True)
    elif kind=='animation':
        skeletal.extract_animation_to_exchange(exchange,logical_name='asset',root_name=root)
        skeletal.extract_skeleton_to_exchange(output/'hierarchy',logical_name='skeleton',root_name=root)
        manifest=json.loads((exchange/'manifest.json').read_text())
        response.update(hierarchy=json.loads((output/'hierarchy/manifest.json').read_text())['root'],fps=manifest['fps'],frame_count=manifest['frame_count'])
    elif kind=='skeleton': skeletal.extract_skeleton_to_exchange(exchange,logical_name='asset',root_name=root)
    elif kind=='skinned_model': skeletal.extract_skinned_scene_to_exchange(root or '',request.get('skeleton_name') or 'animations/skeletons/asset',exchange,logical_name='asset')
    elif kind=='physics': physics.extract_physics_scene_to_exchange(exchange,root_name=root,logical_name='asset')
    else: raise ValueError('Unsupported Maya asset kind: '+kind)
    run_cook_cli(exchange,output/'raw',converted_dir=output/'converted',cli_path=request['cli'])
    response['raw']=str(output/'raw')
    (output/'result.json').write_text(json.dumps(response,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--request',type=Path,required=True)
    args=parser.parse_args();run(json.loads(args.request.read_text(encoding='utf-8')))
