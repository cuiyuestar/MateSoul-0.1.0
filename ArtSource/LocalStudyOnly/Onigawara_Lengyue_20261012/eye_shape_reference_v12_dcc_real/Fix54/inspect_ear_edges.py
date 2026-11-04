import bpy,json,numpy as np
from pathlib import Path
R=Path(__file__).resolve().parent;m=bpy.data.objects['Face'].data;v=np.array([x.co[:] for x in m.vertices]);cs=json.loads((R.parent/'Fix43/face_components.json').read_text());out={}
for c in cs:
 if c['id'] not in [21,22,23,24,25]:continue
 ix=set(c['indices']);ed={}
 for p in m.polygons:
  if not set(p.vertices).issubset(ix):continue
  for a,b in p.edge_keys:ed[(a,b)]=ed.get((a,b),0)+1
 edges=[e for e,n in ed.items() if n==1];bd=sorted(set(i for e in edges for i in e));out[c['id']]={'edges':edges,'indices':bd,'coords':v[bd].tolist()}
(R/'ear_boundaries.json').write_text(json.dumps(out,indent=2))
for a,b in [(24,23),(25,22),(24,21),(25,21),(22,21),(23,21)]:
 ia=np.array(out[a]['indices']);ib=np.array(out[b]['indices']);d=np.linalg.norm(v[ia,None]-v[ib],axis=2);print(a,b,'closest',[(int(i),int(ib[j]),round(float(dd)*1000,2)) for i,j,dd in zip(ia,d.argmin(1),d.min(1))])
