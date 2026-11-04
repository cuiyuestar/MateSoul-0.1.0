import bpy,numpy as np,json,hashlib
from pathlib import Path
R=Path(__file__).resolve().parent;S=R.parent/'Fix55';assert Path(bpy.data.filepath).name=='Onigawara_Lengyue_Direct55.blend'
m=bpy.data.objects['Hair'].data;b=np.array([p.co[:] for p in m.vertices]);v=b.copy();groups=json.loads((R.parent/'Fix43/strand_groups.json').read_text());uv=np.load(R.parent/'Fix39/hair_vertex_uv.npy');cs=json.loads((R.parent/'Fix25/hair_components.json').read_text())
assert np.array_equal(b[:,[0,2,1]].astype('<f4'),np.fromfile(S/'Hair_new.bin','<f4').reshape(-1,3))
def sm(x):x=np.clip(x,0,1);return x*x*(3-2*x)
# Measured target borders: rear strand 84 front-facing edge, side strand 225 rear-facing edge.
# Change the neighbouring edges only; zero at crown. Side and cap vertices follow the same field.
config={84:.0065,225:-.004}
for g in groups:
 gid=g['id']
 if gid not in config:continue
 ids=np.array(g['ids']);p=b[ids];shell=np.array([i for c in cs if c['id'] in [gid,gid+1] for i in c['indices']]);rows=[]
 for yy in np.unique(np.round(uv[shell,1],6)):
  row=shell[np.isclose(uv[shell,1],yy,atol=5e-7)];rows.append((float(b[row,2].mean()),float(b[row,1].min()),float(b[row,1].max())))
 rows=np.array(sorted(rows));lo=np.interp(p[:,2],rows[:,0],rows[:,1]);hi=np.interp(p[:,2],rows[:,0],rows[:,2]);across=np.clip((p[:,1]-lo)/np.maximum(hi-lo,1e-6),0,1)
 # Blender local Y is Unity Z. Move only towards the gap, with a smooth edge falloff.
 edge=sm(across if gid==84 else 1-across);height=sm((1.441-p[:,2])/.052)
 # Zero at each original tip: do not widen a fine tip into a blunt cap.
 tip=sm((p[:,2]-float(p[:,2].min()))/.026)
 v[ids,1]+=config[gid]*height*edge*tip
central=np.fromfile(S/'central_hair_locked.bin','<i4');assert np.array_equal(v[central],b[central]);assert np.array_equal(v[:,[0,2]],b[:,[0,2]])
v=v.astype('<f4').astype(float);ids=np.where(np.any(v!=b,axis=1))[0];tri=np.load(R.parent/'Fix39/hair_triangles.npy');a=np.cross(b[tri[:,1]]-b[tri[:,0]],b[tri[:,2]]-b[tri[:,0]]);c=np.cross(v[tri[:,1]]-v[tri[:,0]],v[tri[:,2]]-v[tri[:,0]]);valid=np.linalg.norm(a,axis=1)>1e-11;flip=valid&((a*c).sum(1)<0);deg=valid&(np.linalg.norm(c,axis=1)<1e-12);assert not flip.any();assert not deg.any()
n=np.fromfile(S/'Hair_new_normals.bin','<f4').reshape(-1,3)[:,[0,2,1]].astype(float);aa=np.zeros_like(b);cc=np.zeros_like(b)
for k in range(3):np.add.at(aa,tri[:,k],a);np.add.at(cc,tri[:,k],c)
for i in np.unique(tri[np.isin(tri,ids).any(1)]):
 x=aa[i]/max(np.linalg.norm(aa[i]),1e-12);y=cc[i]/max(np.linalg.norm(cc[i]),1e-12);ax=np.cross(x,y);si=np.linalg.norm(ax);co=np.clip(x@y,-1,1)
 if si>1e-9:ax/=si;n[i]=n[i]*co+np.cross(ax,n[i])*si+ax*(ax@n[i])*(1-co)
for i in ids:m.vertices[int(i)].co=v[i]
m.update();m.normals_split_custom_set_from_vertices(n.tolist());v[:,[0,2,1]].astype('<f4').tofile(R/'Hair_new.bin');n[:,[0,2,1]].astype('<f4').tofile(R/'Hair_new_normals.bin');ids.astype('<i4').tofile(R/'Hair_target.bin');central.astype('<i4').tofile(R/'central_hair_locked.bin')
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(R/'Onigawara_Lengyue_Direct58.blend'))
r={'pass':True,'geometry_source':'Direct55','complete_prefab_source':'Direct57','changed_vertices':len(ids),'hair_groups':list(config),'max_displacement_mm':float(np.linalg.norm(v-b,axis=1).max()*1000),'central_and_all_fringe_exact':True,'all_hair_height_and_x_exact':True,'non_target_hair_exact':True,'non_hair_untouched':True,'triangle_flips':int(flip.sum()),'new_degenerate_triangles':int(deg.sum()),'parameters':config};(R/'geometry_gate.json').write_text(json.dumps(r,indent=2));print(json.dumps(r))
