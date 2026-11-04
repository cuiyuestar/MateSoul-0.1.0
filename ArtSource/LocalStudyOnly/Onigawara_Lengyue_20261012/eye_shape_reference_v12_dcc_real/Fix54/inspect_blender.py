import bpy,json,numpy as np
from pathlib import Path
R=Path(__file__).resolve().parent
out={}
cc=json.loads((R.parent/'Fix43/face_components.json').read_text())
for name in ['Face','Hair','Body']:
 m=bpy.data.objects[name].data;v=np.array([p.co[:] for p in m.vertices]);v.astype('<f4').tofile(R/(name+'_blender.bin'))
 out[name]={'n':len(v),'min':v.min(0).tolist(),'max':v.max(0).tolist(),'materials':[x.name for x in m.materials],'keys':len(m.shape_keys.key_blocks) if m.shape_keys else 0}
 if name=='Face':
  ear=[]
  for c in cc:
   if c['id'] not in [22,23,24,25]:continue
   ix=np.array(c['indices']);p=v[ix];s=np.linalg.svd(p-p.mean(0),compute_uv=False);ear.append({'id':c['id'],'n':len(ix),'extent':np.ptp(p,axis=0).tolist(),'min':p.min(0).tolist(),'max':p.max(0).tolist(),'singular':s.tolist(),'materials':sorted(set(t.material_index for t in m.polygons if set(t.vertices).issubset(set(ix))))})
  out['ears']=ear
 if name=='Hair':
  groups=json.loads((R.parent/'Fix43/strand_groups.json').read_text());summ=[]
  for g in groups:
   ix=np.array(g['ids']);p=v[ix];low=p[p[:,2]<p[:,2].min()+.004]
   summ.append({'id':g['id'],'n':len(ix),'min':p.min(0).tolist(),'max':p.max(0).tolist(),'tip':low.mean(0).tolist()})
  out['hair_groups']=summ
(R/'blender_inventory.json').write_text(json.dumps(out,indent=2));print(json.dumps({k:v for k,v in out.items() if k!='hair_groups'},indent=2));print('FRONT HAIR',[(g['id'],g['n'],np.round(g['tip'],4).tolist()) for g in out['hair_groups'] if 1.39<g['tip'][2]<1.43 and g['tip'][1]>.03])
