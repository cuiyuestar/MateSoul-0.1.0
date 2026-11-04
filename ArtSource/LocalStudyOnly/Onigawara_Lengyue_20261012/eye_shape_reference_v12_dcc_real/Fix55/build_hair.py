import bpy,numpy as np,json
from pathlib import Path
R=Path(__file__).resolve().parent;S=R.parent/'Fix54';assert 'Direct54' in bpy.data.filepath
m=bpy.data.objects['Hair'].data;b=np.array([p.co[:] for p in m.vertices]);v=b.copy();groups=json.loads((R.parent/'Fix43/strand_groups.json').read_text());uv=np.load(R.parent/'Fix39/hair_vertex_uv.npy');cs=json.loads((R.parent/'Fix25/hair_components.json').read_text())
def sm(x):x=np.clip(x,0,1);return x*x*(3-2*x)
# Local narrowing of lower side edges only, preserve existing crown arc, thickness and tip heights.
# Tuple: horizontal offset, lower width ratio, transition height.
config={4:(.00025,.94,1.466),8:(-.00010,.95,1.474),12:(-.00015,.88,1.475),20:(-.00010,.90,1.475),28:(.00010,.94,1.466)}
for g in groups:
 gid=g['id']
 if gid not in config:continue
 ids=np.array(g['ids']);p=b[ids];shift,scale,top=config[gid];shell=np.array([i for c in cs if c['id'] in [gid,gid+1] for i in c['indices']]);rows=[]
 # Use full front/back-shell row centres, not cap/shell averages, and transport caps coherently.
 for yy in np.unique(np.round(uv[shell,1],6)):
  row=shell[np.isclose(uv[shell,1],yy,atol=5e-7)];height=float(b[row,2].mean());ctr=float(b[row,0].mean());rows.append((height,ctr))
 rows=np.array(sorted(rows));ctr=np.interp(p[:,2],rows[:,0],rows[:,1]);wt=sm((top-p[:,2])/.043)
 v[ids,0]=p[:,0]+wt*((p[:,0]-ctr)*(scale-1)+shift)
central=np.fromfile(S/'central_hair_locked.bin','<i4');assert np.array_equal(v[central],b[central]);assert np.array_equal(v[:,1:],b[:,1:]);ids=np.where(np.linalg.norm(v-b,axis=1)>1e-9)[0]
tri=np.load(R.parent/'Fix39/hair_triangles.npy');a=np.cross(b[tri[:,1]]-b[tri[:,0]],b[tri[:,2]]-b[tri[:,0]]);c=np.cross(v[tri[:,1]]-v[tri[:,0]],v[tri[:,2]]-v[tri[:,0]]);valid=np.linalg.norm(a,axis=1)>1e-11;flip=valid&((a*c).sum(1)<0);deg=valid&(np.linalg.norm(c,axis=1)<1e-12);assert not flip.any();assert not deg.any()
# Preserve original authored shading and transport only normals affected by a local side-edge change.
n=np.fromfile(S/'Hair_new_normals.bin','<f4').reshape(-1,3)[:,[0,2,1]].astype(float);aa=np.zeros_like(b);cc=np.zeros_like(b)
for k in range(3):np.add.at(aa,tri[:,k],a);np.add.at(cc,tri[:,k],c)
affected=np.unique(tri[np.isin(tri,ids).any(1)])
for i in affected:
 x=aa[i]/max(np.linalg.norm(aa[i]),1e-12);y=cc[i]/max(np.linalg.norm(cc[i]),1e-12);ax=np.cross(x,y);si=np.linalg.norm(ax);co=np.clip(x@y,-1,1)
 if si>1e-9:ax/=si;n[i]=n[i]*co+np.cross(ax,n[i])*si+ax*(ax@n[i])*(1-co)
for i in ids:m.vertices[int(i)].co=v[i]
m.update();m.normals_split_custom_set_from_vertices(n.tolist());v[:,[0,2,1]].astype('<f4').tofile(R/'Hair_new.bin');n[:,[0,2,1]].astype('<f4').tofile(R/'Hair_new_normals.bin');ids.astype('<i4').tofile(R/'Hair_target.bin');central.astype('<i4').tofile(R/'central_hair_locked.bin')
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(R/'Onigawara_Lengyue_Direct55.blend'))
r={'pass':True,'source':'Direct54','changed_vertices':len(ids),'hair_groups':list(config),'max_displacement_mm':float(np.linalg.norm(v-b,axis=1).max()*1000),'central_four_groups_exact':True,'all_hair_height_and_depth_exact':True,'non_target_hair_exact':True,'non_hair_untouched':True,'triangle_flips':int(flip.sum()),'new_degenerate_triangles':int(deg.sum()),'parameters':config};(R/'geometry_gate.json').write_text(json.dumps(r,indent=2));print(json.dumps(r))
