"""Open the editable master with the same installed extension used by Reimport."""
import argparse
import json
from pathlib import Path
import sys
import bpy
sys.path.insert(0,str(Path(__file__).resolve().parent))
from sdk_bridge import load_extension
parser=argparse.ArgumentParser()
parser.add_argument('--request',type=Path,required=True)
args=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
request=json.loads(args.request.read_text(encoding='utf-8'))
load_extension(request)
bpy.ops.wm.open_mainfile(filepath=request['source'],use_scripts=False)
