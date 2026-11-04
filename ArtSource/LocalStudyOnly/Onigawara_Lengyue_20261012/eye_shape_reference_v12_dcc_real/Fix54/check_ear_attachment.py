import bpy,json,numpy as np
from pathlib import Path
R=Path(__file__).resolve().parent;m=bpy.data.objects['Face'].data;v=np.array([x.co[:] for x in m.vertices]);cs=json.loads((R.parent/'Fix43/face_components.json').read_text());allowed=set(i for c in cs if c['id'] in [22,23,24,25] for i in c['indices']);edges={}
for t in m.polygons:
 if not set(t.vertices).issubset(allowed):continue
 for a,b in t.edge_keys:edges[(a,b)]=edges.get((a,b),0)+1
boundary=np.unique([i for e,n in edges.items() if n==1 for i in e]);other=np.array([i for i in range(len(v)) if i not in allowed]);dist=np.linalg.norm(v[boundary,None]-v[other],axis=2);ix=dist.argmin(1)
print('ear boundary',len(boundary),'near head',int((dist.min(1)<.001).sum()));out={'boundary':boundary.tolist(),'near_head':boundary[dist.min(1)<.001].tolist(),'near_pairs':[(int(a),int(other[b]),float(d)) for a,b,d in zip(boundary,ix,dist.min(1)) if d<.001]};(R/'ear_attachment.json').write_text(json.dumps(out,indent=2));print(out['near_pairs'])
