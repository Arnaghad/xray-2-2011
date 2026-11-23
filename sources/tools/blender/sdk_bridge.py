"""SDK B6 host worker: editable source -> installed extension -> staged assets.

Run with Blender --background --offline-mode --disable-autoexec --factory-startup.
The SDK owns publication; this worker only writes into its staging directory.
"""
import argparse
import hashlib
import importlib
import json
from pathlib import Path
import sys

import addon_utils
import bpy


def load_extension(request):
    profile=Path(request['profile']).resolve()
    repo_dir=profile/'repo';repo_dir.mkdir(parents=True,exist_ok=True)
    module_name='bl_ext.xray_sdk.xray_blender'
    repo=bpy.context.preferences.extensions.repos.new(name='X-Ray SDK',module='xray_sdk',custom_directory=str(repo_dir))
    archive=Path(request['extension']).resolve(strict=True)
    stamp=profile/'archive.sha256';checksum=hashlib.sha256(archive.read_bytes()).hexdigest()
    if not (repo_dir/'xray_blender/blender_manifest.toml').is_file() or not stamp.is_file() or stamp.read_text()!=checksum:
        if bpy.ops.extensions.package_install_files(filepath=str(archive),repo=repo.module,enable_on_install=True,overwrite=True)!={'FINISHED'}:
            raise RuntimeError('Extension installation failed')
        if not (repo_dir/'xray_blender/blender_manifest.toml').is_file():
            raise RuntimeError('Blender installer did not extract the extension; retry Reimport')
        stamp.write_text(checksum)
    else:
        bpy.ops.extensions.repo_refresh_all(use_active_only=False)
        addon_utils.enable(module_name,default_set=True)
    module=importlib.import_module(module_name)
    if not module.__addon_enabled__ or not Path(module.__file__).resolve().is_relative_to(profile):
        raise RuntimeError('SDK requires the installed extension')
    ex=importlib.import_module(module_name+'.exporters')
    imp=importlib.import_module(module_name+'.importers')
    return ex,imp


def run(request):
    output=Path(request['output']).resolve()
    source=Path(request['source']).resolve(strict=True)
    if source==output or output.is_relative_to(source) or source.is_relative_to(output):
        raise ValueError('Source and staging directory must be separate')
    output.mkdir(parents=True,exist_ok=True)
    ex,imp=load_extension(request)
    if source.suffix.lower()=='.blend':
        bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False,use_scripts=False)
    elif source.suffix.lower() in {'.ma','.model','.skinned_model','.clip','.skeleton','.physics'}:
        bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
        imp.import_asset(bpy.context,source,skeleton_path=request.get('skeleton') or None)
    elif source.suffix.lower()=='.mb':
        raise ValueError('Binary Maya source requires the maintainer migration route; select its migrated .blend or use Maya')
    else:
        raise ValueError('Unsupported DCC source')
    kind=request['kind']
    named=request.get('object','')
    if named:
        root=bpy.data.objects.get(named)
        if root is None: raise ValueError('Source object not found: '+named)
    else:
        candidates=[o for o in bpy.data.objects if
            (o.type=='ARMATURE' and kind in {'animation','skeleton','skinned_model'}) or
            (o.xray_kind=='STATIC_ROOT' and kind=='static_model') or
            (o.xray_kind=='BODY' and o.parent is None and kind=='physics')]
        if len(candidates)!=1: raise ValueError('Choose a source object; candidates: '+', '.join(o.name for o in candidates))
        root=candidates[0]
    response=dict(schema='xray-sdk-dcc-result/v1',kind=kind,object=root.name,source=str(source))
    if kind=='animation':
        action=bpy.data.actions.get(request.get('action','')) if request.get('action') else (root.animation_data.action if root.animation_data else None)
        if action is None: raise ValueError('Choose an animation Action')
        result=ex.animation(bpy.context,root,'asset',action=action)
        response.update(hierarchy=ex.skeleton(bpy.context,root,'skeleton').manifest['root'],fps=result.manifest['fps'],frame_count=result.manifest['frame_count'],action=action.name)
    elif kind=='static_model': result=ex.model(bpy.context,root,'asset')
    elif kind=='skinned_model':
        result=ex.model(bpy.context,root,'asset',rig=root,skeleton_name=request.get('skeleton_name') or 'animations/skeletons/asset')
        ex.export_and_cook(ex.skeleton(bpy.context,root,'skeleton'),output/'skeleton',Path(request['cli']))
    elif kind=='skeleton': result=ex.skeleton(bpy.context,root,'asset')
    elif kind=='physics': result=ex.physics(bpy.context,root,'asset')
    else: raise ValueError('Unsupported asset kind: '+kind)
    ex.export_and_cook(result,output,Path(request['cli']))
    response['raw']=str(output/'raw')
    # Adoption/import creates an editable Blender master; reimport never saves
    # over an artist's source while evaluating it.
    if request.get('save_blend'):
        destination=Path(request['save_blend']).resolve()
        if not destination.is_relative_to(output): raise ValueError('Imported Blender master must be inside staging')
        bpy.context.preferences.filepaths.save_version=0
        bpy.ops.wm.save_as_mainfile(filepath=str(destination),check_existing=False,compress=True)
    (output/'result.json').write_text(json.dumps(response,indent=2)+'\n',encoding='utf-8')


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--request',type=Path,required=True)
    args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    run(json.loads(args.request.read_text(encoding='utf-8')))
