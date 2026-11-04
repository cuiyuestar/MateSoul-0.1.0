import bpy,numpy as np,json
from pathlib import Path
R=Path(__file__).resolve().parent;S=R.parent/'Fix58';assert Path(bpy.data.filepath).name=='Onigawara_Lengyue_Direct58.blend'
m=bpy.data.objects['Hair'].data;b=np.array([p.co[:] for p in m.vertices]);v=b.copy();groups=json.loads((R.parent/'Fix43/strand_groups.json').read_text());uv=np.load(R.parent/'Fix39/hair_vertex_uv.npy');cs=json.loads((R.parent/'Fix25/hair_components.json').read_text())
assert np.array_equal(b[:,[0,2,1]].astype('<f4'),np.fromfile(S/'Hair_new.bin','<f4').reshape(-1,3))
def sm(x):x=np.clip(x,0,1);return x*x*(3-2*x)
def rows(gid):
 shell=np.array([i for c in cs if c['id']==gid for i in c['indices']]);out=[]
 for yy in np.unique(np.round(uv[shell,1],6)):
  row=shell[np.isclose(uv[shell,1],yy,atol=5e-7)];p=b[row];front=p[np.argmax(p[:,1])];back=p[np.argmin(p[:,1])];out.append((p[:,2].mean(),front[0],front[1],back[0],back[1]))
 return np.array(sorted(out))
# Extend the existing rear strand edge beneath the side strand. Preserve actual 3D layering:
# the side strand remains outside; rear strand gets a 6mm depth overlap and 3.5mm inward offset.
a=rows(84);q=rows(225);ids=np.array(next(g['ids'] for g in groups if g['id']==84));p=b[ids];h=p[:,2]
lo=np.interp(h,a[:,0],a[:,4]);hi=np.interp(h,a[:,0],a[:,2]);across=np.clip((p[:,1]-lo)/np.maximum(hi-lo,1e-6),0,1)
edge=sm(across);fade=sm((1.474-h)/.043)*sm((h-1.315)/.025)
front_x=np.interp(h,a[:,0],a[:,1]);back_x=np.interp(h,q[:,0],q[:,3]);back_z=np.interp(h,q[:,0],q[:,4])
dz=np.maximum(0,back_z+.006-hi);dx=back_x-.0035-front_x
v[ids,1]+=dz*edge*fade;v[ids,0]+=dx*edge*fade
# Smooth convex clearance over the ear, still below the outside edge of the neighbouring strand.
clearance=.007*sm((1.435-h)/.019)*sm((h-1.358)/.025)*np.sin(np.pi*across)
v[ids,0]+=clearance
central=np.fromfile(S/'central_hair_locked.bin','<i4');assert np.array_equal(v[central],b[central]);assert np.array_equal(v[:,2],b[:,2])
v=v.astype('<f4').astype(float);ids=np.where(np.any(v!=b,axis=1))[0];tri=np.load(R.parent/'Fix39/hair_triangles.npy');aa0=np.cross(b[tri[:,1]]-b[tri[:,0]],b[tri[:,2]]-b[tri[:,0]]);cc0=np.cross(v[tri[:,1]]-v[tri[:,0]],v[tri[:,2]]-v[tri[:,0]]);valid=np.linalg.norm(aa0,axis=1)>1e-11;flip=valid&((aa0*cc0).sum(1)<0);deg=valid&(np.linalg.norm(cc0,axis=1)<1e-12);assert not flip.any(),int(flip.sum());assert not deg.any()
n=np.fromfile(S/'Hair_new_normals.bin','<f4').reshape(-1,3)[:,[0,2,1]].astype(float);aa=np.zeros_like(b);cc=np.zeros_like(b)
for k in range(3):np.add.at(aa,tri[:,k],aa0);np.add.at(cc,tri[:,k],cc0)
for i in np.unique(tri[np.isin(tri,ids).any(1)]):
 x=aa[i]/max(np.linalg.norm(aa[i]),1e-12);y=cc[i]/max(np.linalg.norm(cc[i]),1e-12);ax=np.cross(x,y);si=np.linalg.norm(ax);co=np.clip(x@y,-1,1)
 if si>1e-9:ax/=si;n[i]=n[i]*co+np.cross(ax,n[i])*si+ax*(ax@n[i])*(1-co)
for i in ids:m.vertices[int(i)].co=v[i]
m.update();m.normals_split_custom_set_from_vertices(n.tolist());v[:,[0,2,1]].astype('<f4').tofile(R/'Hair_new.bin');n[:,[0,2,1]].astype('<f4').tofile(R/'Hair_new_normals.bin');ids.astype('<i4').tofile(R/'Hair_target.bin');central.astype('<i4').tofile(R/'central_hair_locked.bin')
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(R/'Onigawara_Lengyue_Direct59.blend'))
r={'pass':True,'geometry_source':'Direct58','complete_prefab_source':'Direct58','changed_vertices':len(ids),'hair_groups':[84],'max_displacement_mm':float(np.linalg.norm(v-b,axis=1).max()*1000),'central_and_all_fringe_exact':True,'all_hair_height_exact':True,'non_target_hair_exact':True,'non_hair_untouched':True,'triangle_flips':int(flip.sum()),'new_degenerate_triangles':int(deg.sum()),'layering':'extend existing rear strand underneath side strand; no added patch; ear untouched'};(R/'geometry_gate.json').write_text(json.dumps(r,indent=2));print(json.dumps(r))
