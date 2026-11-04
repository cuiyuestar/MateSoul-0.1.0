import bpy,numpy as np,json
from pathlib import Path
R=Path(__file__).resolve().parent;assert 'Direct53' in bpy.data.filepath
report={};original={};final={}
def sm(x):x=np.clip(x,0,1);return x*x*(3-2*x)
def load(n):return np.fromfile(R/(n+'_unity.bin'),'<f4').reshape(-1,3).astype(float)
def apply(name,v):
 o=bpy.data.objects[name];m=o.data;orig=np.array([p.co[:] for p in m.vertices]);q=v.copy()
 if name=='Hair':q=q[:,[0,2,1]]
 if name=='Body':q=q[:,[0,2,1]]*[-1,-1,1]
 delta=q-orig;ids=np.where(np.linalg.norm(delta,axis=1)>1e-9)[0]
 if m.shape_keys:
  for key in m.shape_keys.key_blocks:
   for i in ids:key.data[int(i)].co=np.array(key.data[int(i)].co)+delta[i]
 for i in ids:m.vertices[int(i)].co=q[i]
 m.update();m.calc_loop_triangles();tri=np.array([t.vertices[:] for t in m.loop_triangles]);a=np.cross(orig[tri[:,1]]-orig[tri[:,0]],orig[tri[:,2]]-orig[tri[:,0]]);b=np.cross(q[tri[:,1]]-q[tri[:,0]],q[tri[:,2]]-q[tri[:,0]]);valid=np.linalg.norm(a,axis=1)>1e-11;flip=valid&((a*b).sum(1)<0);deg=valid&(np.linalg.norm(b,axis=1)<1e-12)
 assert not flip.any(),(name,'flips',np.where(flip)[0].tolist());assert not deg.any(),(name,'degenerate')
 # Transport artist normals with the geometric tangent change, retaining untouched custom normals.
 nn=np.fromfile(R/(name+'_normals.bin'),'<f4').reshape(-1,3).astype(float)
 if name=='Hair':nn=nn[:,[0,2,1]]
 if name=='Body':nn=nn[:,[0,2,1]]*[-1,-1,1]
 ag=np.zeros_like(orig);bg=np.zeros_like(orig)
 for k in range(3):np.add.at(ag,tri[:,k],a);np.add.at(bg,tri[:,k],b)
 affected=np.unique(tri[np.isin(tri,ids).any(1)])
 for i in affected:
  u=ag[i]/max(np.linalg.norm(ag[i]),1e-12);w=bg[i]/max(np.linalg.norm(bg[i]),1e-12);axis=np.cross(u,w);sn=np.linalg.norm(axis);co=np.clip(u@w,-1,1)
  if sn>1e-9:axis/=sn;n=nn[i];nn[i]=n*co+np.cross(axis,n)*sn+axis*(axis@n)*(1-co)
 m.normals_split_custom_set_from_vertices(nn.tolist())
 if name=='Hair':nn=nn[:,[0,2,1]]
 if name=='Body':nn=(nn*[-1,-1,1])[:,[0,2,1]]
 v.astype('<f4').tofile(R/(name+'_new.bin'));nn.astype('<f4').tofile(R/(name+'_new_normals.bin'));ids.astype('<i4').tofile(R/(name+'_target.bin'))
 report[name]={'changed':len(ids),'max_mm':float(np.linalg.norm(v-load(name),axis=1).max()*1000),'triangle_flips':int(flip.sum()),'new_degenerate':int(deg.sum()),'keys':len(m.shape_keys.key_blocks) if m.shape_keys else 0}
 original[name]=orig;final[name]=q
# Body: work in the actual Unity skinned world pose, then invert each vertex skin matrix.
v=load('Body');w=np.fromfile(R/'Body_world.bin','<f4').reshape(-1,3).astype(float);dst=w.copy();bones=json.loads((R/'coordinate_check.json').read_text())['bones'];bp=lambda n:np.array(list(bones[n].values()))
x=w[:,0];y=w[:,1]
# Neck-only envelope, zero at head/face seam and shoulder connection.
f=.04*sm((y-1.265)/.024)*(1-sm((y-1.305)/.022))*sm((.065-np.abs(x))/.025)
# Preserve the existing face-neck seam and fade deformation within 12mm of it.
fw=np.fromfile(R/'Face_world.bin','<f4').reshape(-1,3);near=np.where((y>1.26)&(y<1.39)&(abs(x)<.09))[0]
for ii in near:
 distance=np.linalg.norm(fw-w[ii],axis=1).min();f[ii]*=sm((distance-.0006)/.012)
cy=np.clip((y-1.2813)/.0734,0,1);cz=-.032323+cy*.009285
dst[:,0]-=f*x;dst[:,2]-=f*(w[:,2]-cz)
for side in ['L','R']:
 # piecewise nearest radial distance around skeleton; preserve length and hands/feet.
 for part,start,mid,end in [('arm','UpperArm','LowerArm','Hand'),('leg','UpperLeg','LowerLeg','Foot')]:
  a=bp('J_Bip_'+side+'_'+start);b=bp('J_Bip_'+side+'_'+mid);c=bp('J_Bip_'+side+'_'+end)
  centers=[];ds=[]
  for aa,bb in [(a,b),(b,c)]:
   t=np.clip(((w-aa)@(bb-aa))/np.sum((bb-aa)**2),0,1);p=aa+t[:,None]*(bb-aa);centers.append(p);ds.append(np.linalg.norm(w-p,axis=1))
  sel=np.array(ds).argmin(0);center=np.where(sel[:,None]==0,centers[0],centers[1]);rad=w-center;dist=np.linalg.norm(rad,axis=1)
  if part=='arm':mask=sm((abs(x)-.22)/.065)*(1-sm((abs(x)-.47)/.055))*(1-sm((dist-.052)/.02))
  else:mask=sm((y-.13)/.07)*(1-sm((y-.74)/.06))*(1-sm((dist-.066)/.02))
  mask*=sm((x*np.sign(a[0]))/.025);dst-=.04*mask[:,None]*rad
skin=np.fromfile(R/'Body_skin.bin','<f4').reshape(-1,4,4).transpose(0,2,1).astype(float)
delta=np.linalg.solve(skin[:,:3,:3],(dst-w)[...,None])[...,0];v+=delta;apply('Body',v)
# Bangs: keep central two complete strands and centre gap exactly unchanged.
v=load('Hair');groups=json.loads((R.parent/'Fix43/strand_groups.json').read_text());uv=np.load(R.parent/'Fix39/hair_vertex_uv.npy');cs=json.loads((R.parent/'Fix25/hair_components.json').read_text())
config={4:(.0010,1.10,.0005),8:(.0018,1.12,-.0007),12:(.0005,1.18,.0005),20:(.0016,1.16,-.0002),28:(-.0010,1.12,.0008)}
for g in groups:
 gid=g['id']
 if gid not in config:continue
 ids=np.array(g['ids']);p=v[ids].copy();shift,scale,lift=config[gid];wt=sm((1.533-p[:,1])/.095);v[ids,0]+=shift*wt;v[ids,1]+=lift*sm((1.45-p[:,1])/.025)
 # width about smoothly interpolated row centre; all layers use same transform.
 for yy in np.unique(np.round(uv[ids,1],5)):
  row=ids[np.isclose(uv[ids,1],yy,atol=5.1e-6)];ctr=load('Hair')[row,0].mean();ww=sm((1.523-load('Hair')[row,1])/.08);v[row,0]+=(load('Hair')[row,0]-ctr)*(scale-1)*ww
 # keep rounded upper arc; slight coherent depth adjustment follows the forehead.
 v[ids,2]+=.0006*sm((1.52-p[:,1])/.06)*(1-sm((1.445-p[:,1])/.03))
central=np.concatenate([np.array(g['ids']) for g in groups if g['id'] in [0,16,24,32]]);assert np.array_equal(v[central],load('Hair')[central]);central.astype('<i4').tofile(R/'central_hair_locked.bin');apply('Hair',v)
# Ear relief on native shells; inner concha inset, helix raised; root joins stay on head.
v=load('Face');cs=json.loads((R.parent/'Fix43/face_components.json').read_text());ea=json.loads((R/'ear_attachment.json').read_text());old=v.copy();all_ear=[]
for c in cs:
 if c['id'] not in [22,23,24,25]:continue
 ids=np.array(c['indices']);all_ear.extend(ids.tolist());p=old[ids];sg=np.sign(p[:,0].mean());u=(p[:,1]-1.400)/.030;t=(np.abs(p[:,0])-.063)/.039;root=sm((np.abs(p[:,0])-.071)/.015)
 # Avoid planar treatment: physically sculpt the anterior shell across the ear surface.
 if c['id'] in [24,25]:
  bowl=np.exp(-((u+.02)/.54)**2-((t-.53)/.34)**2)
  ridge=np.exp(-((t-.84)/.13)**2)*(1-sm((abs(u)-.67)/.34))
  depth=(-.0025*bowl+.0012*ridge)*root
  v[ids,0]+=sg*depth*.72;v[ids,2]+=depth*.69
 # Slightly more compact round ear tips; attachment region unchanged.
 v[ids,1]-=.035*(p[:,1]-1.40)*root
for i,j,d in ea['near_pairs']:v[i]=old[j]
# Preserve shared UV-split rim positions and close the entire front root to the native head boundary.
ed=json.loads((R/'ear_boundaries.json').read_text());pairs=[]
for front,back in [(24,23),(25,22)]:
 fi=np.array(ed[str(front)]['indices']);bi=np.array(ed[str(back)]['indices']);head=np.array(ed['21']['indices']);dist=np.linalg.norm(old[fi,None]-old[bi],axis=2);near=dist.argmin(1);outer=[]
 for i,j,dd in zip(fi,near,dist.min(1)):
  if dd<1e-6:
   avg=(v[i]+v[bi[j]])*.5;v[i]=avg;v[bi[j]]=avg;outer.append(int(i));pairs.append((int(i),int(bi[j])))
 for i in fi:
  if int(i) in outer:continue
  he=np.array(ed['21']['edges']);aa=old[he[:,0]];bb=old[he[:,1]];ab=bb-aa;tt=np.clip(((old[i]-aa)*ab).sum(1)/np.maximum((ab*ab).sum(1),1e-14),0,1);proj=aa+tt[:,None]*ab;ds=np.linalg.norm(proj-old[i],axis=1);kk=ds.argmin()
  if ds[kk]<.0048:
   # Project onto the continuous head edge rather than snapping several points to one vertex.
   v[i]=proj[kk];j=int(he[kk,0] if tt[kk]<.5 else he[kk,1]);pairs.append((int(i),j))
# Root pair duplicates from front/back agree; avoid an artificial dark normal seam.
for i,j,dd in ea['near_pairs']:v[i]=old[j]
for i,j in pairs:
 if j in all_ear and np.linalg.norm(old[i]-old[j])<1e-6:v[i]=v[j]
(R/'ear_seam_pairs.json').write_text(json.dumps(pairs))
all_ear=np.array(all_ear);non=np.setdiff1d(np.arange(len(v)),all_ear);assert np.array_equal(v[non],old[non]);apply('Face',v)
# Normalize normals across UV-split seams without changing UV islands or vertex indexing.
fn=np.fromfile(R/'Face_new_normals.bin','<f4').reshape(-1,3).copy()
for i,j in pairs:
 if j in non:fn[i]=fn[j]
 else:
  n=fn[i]+fn[j];n/=max(np.linalg.norm(n),1e-8);fn[i]=n;fn[j]=n
fn.tofile(R/'Face_new_normals.bin');bpy.data.objects['Face'].data.normals_split_custom_set_from_vertices(fn.tolist())
report['pass']=True;report['central_gap_exact']=True;report['face_outside_ears_exact']=True;report['body_radial_scale']=.96;report['ear_root_pairs_snapped']=len(ea['near_pairs']);report['ear_explanation']='Existing 540-vertex nonplanar ear shells. Recessed concha and raised helix; root position aligned, UV islands preserved. No topological weld or forced draw-on-top.'
bpy.context.preferences.filepaths.save_version=0;bpy.ops.wm.save_as_mainfile(filepath=str(R/'Onigawara_Lengyue_Direct54.blend'));(R/'geometry_gate.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
