import bpy,numpy as np,json
from pathlib import Path
R=Path(__file__).resolve().parent;cur={}
for o in bpy.data.objects:
 if o.type=='MESH':m=o.data;cur[o.name]={'v':np.array([p.co[:] for p in m.vertices]),'uv':np.array([p.uv[:] for p in m.uv_layers.active.data]) if m.uv_layers.active else None,'poly':[(tuple(p.vertices),p.material_index) for p in m.polygons],'groups':[(p.index,[(g.group,g.weight) for g in p.groups]) for p in m.vertices],'mats':[x.name for x in m.materials],'keys':{k.name:np.array([p.co[:] for p in k.data]) for k in m.shape_keys.key_blocks} if m.shape_keys else {}}
bpy.ops.wm.open_mainfile(filepath=str(R.parent/'Fix54/Onigawara_Lengyue_Direct54.blend'));checks={}
for name,c in cur.items():
 m=bpy.data.objects[name].data;b=np.array([p.co[:] for p in m.vertices]);assert c['poly']==[(tuple(p.vertices),p.material_index) for p in m.polygons];assert c['groups']==[(p.index,[(g.group,g.weight) for g in p.groups]) for p in m.vertices];assert c['mats']==[x.name for x in m.materials]
 if c['uv'] is not None:assert np.array_equal(c['uv'],np.array([p.uv[:] for p in m.uv_layers.active.data]))
 ids=np.fromfile(R/'Hair_target.bin','<i4') if name=='Hair' else np.array([],int);non=np.setdiff1d(np.arange(len(b)),ids);assert np.array_equal(b[non],c['v'][non]),name
 if name=='Hair':assert np.allclose(c['v'][:,[0,2,1]],np.fromfile(R/'Hair_new.bin','<f4').reshape(-1,3),atol=1e-7,rtol=0)
 err=0
 if c['keys']:
  base={k.name:np.array([p.co[:] for p in k.data]) for k in m.shape_keys.key_blocks}
  for k in base:err=max(err,float(abs((base[k]-base['Basis'])-(c['keys'][k]-c['keys']['Basis'])).max()))
  assert err<2e-7
 checks[name]={'protected':True,'uv_topology_weights_materials':True,'morph_delta_error':err}
(R/'independent_blend_gate.json').write_text(json.dumps({'pass':True,'checks':checks},indent=2));print('DIRECT55_BLEND_OK')
