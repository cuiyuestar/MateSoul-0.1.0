from pathlib import Path
import json,numpy as np,itertools
R=Path(__file__).resolve().parent
b=np.fromfile(R/'Body_blender.bin','<f4').reshape(-1,3);u=np.fromfile(R/'Body_unity.bin','<f4').reshape(-1,3)
print('BODY BEST MAPPINGS',sorted([(float(abs(b[:,p]*s-u).max()),p,s) for p in itertools.permutations(range(3)) for s in itertools.product([-1,1],repeat=3)])[:2])
gs=json.loads((R.parent/'Fix43/strand_groups.json').read_text());v=np.fromfile(R/'Hair_unity.bin','<f4').reshape(-1,3);data=[]
for g in gs[:9]:
 p=v[g['ids']];tip=p[p[:,1]<1.426];data.append({'id':g['id'],'bounds':[p.min(0).tolist(),p.max(0).tolist()],'tip_bounds':[tip.min(0).tolist(),tip.max(0).tolist()]})
print('BANGS',json.dumps(data))
v=np.fromfile(R/'Face_unity.bin','<f4').reshape(-1,3);cs=json.loads((R.parent/'Fix43/face_components.json').read_text());detail=[]
for c in cs:
 if c['id'] not in [22,23,24,25]:continue
 p=v[c['indices']];ctr=p.mean(0);_,s,vt=np.linalg.svd(p-ctr,full_matrices=False);detail.append({'id':c['id'],'center':ctr.tolist(),'pca':vt.tolist(),'singular':s.tolist()})
print('EARS',json.dumps(detail));(R/'shape_summary.json').write_text(json.dumps({'bangs':data,'ears':detail},indent=2))
