from pathlib import Path
import json,tarfile,zipfile,hashlib
R=Path(__file__).resolve().parent;O=Path('/Users/bytedance/DoubaoWork/chats/2026-10-19/new-chat');U=R.parents[1]/'Unity6Study';F=U/'Assets/Study/EyeShapeReferenceV12DCCReal/Fix58'
package=O/'冷月Direct58_后侧发缝优化.unitypackage';verified=[]
with tarfile.open(package,'r:gz') as t:
 ms={m.name:m for m in t.getmembers()}
 for n,m in ms.items():
  if not n.endswith('/pathname'):continue
  path=t.extractfile(m).read().decode().strip();asset=n.rsplit('/',1)[0]+'/asset'
  if asset not in ms:continue
  real=U/path
  if not real.is_file() and path.startswith('Packages/'):
   _,pkg,rest=path.split('/',2);dep=json.loads((U/'Packages/manifest.json').read_text())['dependencies'].get(pkg,'')
   if dep.startswith('file:'):real=(U/'Packages'/dep[5:]/rest).resolve()
   else:
    c=list((U/'Library/PackageCache').glob(pkg+'@*/'+rest));assert len(c)==1;real=c[0]
  assert real.is_file(),path;assert hashlib.sha256(t.extractfile(ms[asset]).read()).digest()==hashlib.sha256(real.read_bytes()).digest(),path;verified.append(path)
for required in ['Hair_Direct58.asset','HairStrandLines_Direct58.asset','FaceExpression_Direct57.mat','BodySkin_Direct56.mat']:assert any(required in p for p in verified),required
(R/'package_gate.json').write_text(json.dumps({'pass':True,'verified_payloads':len(verified),'all_payloads_byte_match':True,'latest_brow_and_thumb_materials_included':True},indent=2))
notes='''# 冷月 Direct58 后侧发缝自然收窄

## 使用
导入「冷月Direct58_后侧发缝优化.unitypackage」，使用 Assets/Study/EyeShapeReferenceV12DCCReal/Fix58/Onigawara_Lengyue_Direct58.prefab。
这是继承Direct57的完整最新版，包括深紫眉、拇指修复、Direct55刘海、体态、脸型、鼻尖及耳廓。

## 实际改动
仅修改红圈相邻两束Hair网格（分束84/225）靠缝的边缘，使用连续渐变将中下段轻微加宽。上部弧线和冠部保持，发尖变形衰减至零，不硬封洞、不新增贴片、不移动耳朵、不置顶。同步重新生成绑定发束描边，材质/贴图不变。

## 验证
Blender独立重新打开，检查非目标点、UV、拓扑、骨骼权重、全部ShapeKey。Unity回读最终网格，检查材质、蒙皮权重、Morph及原依赖字节未变。完成0/45/90、120/135/150/180/225度同条件前后渲染；临时灯不保存进Prefab。局部对照以原像素裁剪，未修图。渲染中固定扫描行的露肤宽度测量详见验证/render_gate.json。
仅静态零表情检查，没有逐帧审查所有动作和头发物理状态。原有非本轮范围的头顶接缝、后发高光与发尾层次保持，不宣称全部发型问题已解决。

## Blender源说明
附Direct58.blend是从最新几何源Direct55迭代的真实DCC文件；Direct56拇指与Direct57眉毛是Unity侧贴图绑定，因此最新完整外观以本包Unity Prefab为准，Blend不作为完整外观替代。

## 回退与许可
未覆盖Direct57稳定Prefab和其依赖；回退时直接使用Direct57。原素材继续受LocalStudyOnly限制，本包不新增商业或再分发授权。
'''
(R/'交付说明.md').write_text(notes)
files=[(package,package.name),(R/'交付说明.md','交付说明.md'),(R/'Onigawara_Lengyue_Direct58.blend','Blender几何源/Onigawara_Lengyue_Direct58.blend')]
for n in ['冷月Direct58_后侧发缝真实前后对照.png','冷月Direct58_右后侧头发高清原图.png','冷月Direct58_真实头部三视图.png']:files.append((O/n,'预览/'+n))
for p in sorted((R/'renders/after').glob('*.png')):files.append((p,'真实原图/'+p.name))
for n in ['geometry_gate.json','independent_blend_gate.json','unity_gate.json','render_gate.json','package_gate.json','line_geometry.json']:files.append((R/n,'验证/'+n))
for n in ['build_hair.py','verify_blend.py','make_delivery.py','Hair_new.bin','Hair_new_normals.bin','Hair_target.bin','central_hair_locked.bin']:files.append((R/n,'复现/'+n))
for n in ['Direct58Build.cs','Direct58Lines.cs']:files.append((F/'Editor'/n,'复现/'+n))
z=O/'冷月Direct58_后侧发缝优化真实资产.zip'
with zipfile.ZipFile(z,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as q:
 for p,n in files:q.write(p,n)
with zipfile.ZipFile(z) as q:assert q.testzip() is None;print('ZIP',len(q.namelist()),'verified payloads',len(verified),'size MB',z.stat().st_size/1024**2)
