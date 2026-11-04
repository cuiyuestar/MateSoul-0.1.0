import bpy,json,numpy as np
from pathlib import Path
R=Path(__file__).resolve().parent;m=bpy.data.objects['Face'].data;v=np.array([p.co[:] for p in m.vertices]);uv=np.zeros((len(v),2))
for l in m.loops:uv[l.vertex_index]=m.uv_layers.active.data[l.index].uv[:]
cs=json.loads((R.parent/'Fix43/face_components.json').read_text());out=[]
for c in cs:
 if c['id'] not in [13,14]:continue
 ids=np.array(c['indices']);out.append({'id':c['id'],'indices':ids.tolist(),'coords':v[ids].tolist(),'uv':uv[ids].tolist(),'material_indices':sorted(set(p.material_index for p in m.polygons if set(p.vertices).issubset(set(ids))))})
(R/'brow_inventory.json').write_text(json.dumps(out,indent=2));print(json.dumps(out))
