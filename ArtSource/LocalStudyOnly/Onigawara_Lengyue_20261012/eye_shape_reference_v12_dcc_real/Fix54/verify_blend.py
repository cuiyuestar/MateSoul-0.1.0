import bpy,numpy as np,json
from pathlib import Path
R=Path(__file__).resolve().parent;candidate={}
for o in bpy.data.objects:
 if o.type!='MESH':continue
 m=o.data;candidate[o.name]={'v':np.array([v.co[:] for v in m.vertices]),'polys':[(tuple(p.vertices),p.material_index) for p in m.polygons],'uv':np.array([p.uv[:] for p in m.uv_layers.active.data]) if m.uv_layers.active else None,'groups':[(v.index,[(g.group,g.weight) for g in v.groups]) for v in m.vertices],'keys':{k.name:np.array([p.co[:] for p in k.data]) for k in m.shape_keys.key_blocks} if m.shape_keys else {},'mat':[x.name for x in m.materials]}
bpy.ops.wm.open_mainfile(filepath=str(R.parent/'Fix53/Onigawara_Lengyue_Direct53_SoftFace.blend'));checks={}
for name,c in candidate.items():
 m=bpy.data.objects[name].data;b=np.array([v.co[:] for v in m.vertices]);assert c['polys']==[(tuple(p.vertices),p.material_index) for p in m.polygons];assert c['mat']==[x.name for x in m.materials];assert c['groups']==[(v.index,[(g.group,g.weight) for g in v.groups]) for v in m.vertices]
 if c['uv'] is not None:assert np.array_equal(c['uv'],np.array([p.uv[:] for p in m.uv_layers.active.data]))
 ids=np.fromfile(R/(name+'_target.bin'),'<i4') if name in ['Face','Hair','Body'] else np.array([],int);non=np.setdiff1d(np.arange(len(b)),ids);assert np.array_equal(b[non],c['v'][non]),name+' non target'
 if name in ['Face','Hair','Body']:
  a=np.fromfile(R/(name+'_new.bin'),'<f4').reshape(-1,3)
  if name=='Hair':a=a[:,[0,2,1]]
  if name=='Body':a=a[:,[0,2,1]]*[-1,-1,1]
  assert np.allclose(a,c['v'],atol=1e-7,rtol=0),name+' binary'
 err=0
 if m.shape_keys:
  keys={k.name:np.array([p.co[:] for p in k.data]) for k in m.shape_keys.key_blocks};assert set(keys)==set(c['keys'])
  for k in keys:err=max(err,float(np.max(abs((keys[k]-keys['Basis'])-(c['keys'][k]-c['keys']['Basis'])))))
  assert err<2e-7,name+' morph'
 checks[name]={'protected_vertices':True,'topology_uv_weights_materials':True,'morph_delta_error':err}
(R/'independent_blend_gate.json').write_text(json.dumps({'pass':True,'checks':checks},indent=2));print('INDEPENDENT_BLEND_OK')
