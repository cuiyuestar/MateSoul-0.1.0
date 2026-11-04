from pathlib import Path
import json,tarfile,zipfile,hashlib
R=Path(__file__).resolve().parent;O=Path('/Users/bytedance/DoubaoWork/chats/2026-10-19/new-chat');U=R.parents[1]/'Unity6Study';F=U/'Assets/Study/EyeShapeReferenceV12DCCReal/Fix59'
package=O/'冷月Direct59_侧耳发缝闭合.unitypackage';verified=[]
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
for required in ['Hair_Direct59.asset','HairStrandLines_Direct59.asset','FaceExpression_Direct57.mat','BodySkin_Direct56.mat']:assert any(required in p for p in verified),required
(R/'package_gate.json').write_text(json.dumps({'pass':True,'verified_payloads':len(verified),'all_payloads_byte_match':True,'latest_brow_and_thumb_materials_included':True},indent=2))
notes='''# 冷月 Direct59 侧耳发缝自然闭合

## 使用
导入「冷月Direct59_侧耳发缝闭合.unitypackage」，使用 Assets/Study/EyeShapeReferenceV12DCCReal/Fix59/Onigawara_Lengyue_Direct59.prefab。
继承Direct58完整角色，包含深紫眉、拇指修复及已确认刘海、脸型、鼻尖、体态。

## 改动
按本轮“完全闭合”要求，将原有后侧发束84靠缝边缘拓展到侧发225下面，保留正常深度和自然叠层；调整局部曲面避开耳部穿透。仅改这一束Hair的79个顶点，其他头发、Face耳朵和身体网格不改；无新增补片，不置顶，不删耳。同步生成发束描边。材质、纹理、UV、骨骼权重及Morph保持原样。

## 验证
独立重开Blend逐点检查，Unity校验最终写入网格、原依赖字节及保护部位；0/45/90/120/135/150/180/225度同条件真实渲染回读。侧面和右后侧红圈核心ROI内可辨肤色像素为0，详见验证/render_gate.json。渲染前后相机和灯光一致，临时灯光不存入Prefab。静态零表情验证，不等同于所有动态物理与动作逐帧通过。
本轮闭合用户点名的右侧露肤缝；其他发束末端自然分叉、左侧耳部自然遮挡、原有头顶接缝等非本轮范围保持。

## 源文件与回退
Blend为从Direct58几何源迭代的Direct59真实文件；Direct56/57最新贴图绑定仍以Unity Prefab为完整外观权威源，不能用Blend旧贴图替代。Direct58稳定文件未覆盖，需回退可直接使用旧Prefab。
原素材继续受LocalStudyOnly限制，本包不新增商业或再分发授权。
'''
(R/'交付说明.md').write_text(notes)
files=[(package,package.name),(R/'交付说明.md','交付说明.md'),(R/'Onigawara_Lengyue_Direct59.blend','Blender几何源/Onigawara_Lengyue_Direct59.blend')]
for n in ['冷月Direct59_侧耳闭缝真实前后对照.png','冷月Direct59_右后侧头发高清原图.png','冷月Direct59_真实头部三视图.png']:files.append((O/n,'预览/'+n))
for p in sorted((R/'renders/after').glob('*.png')):files.append((p,'真实原图/'+p.name))
for n in ['geometry_gate.json','independent_blend_gate.json','unity_gate.json','render_gate.json','package_gate.json','line_geometry.json']:files.append((R/n,'验证/'+n))
for n in ['build_hair.py','verify_blend.py','make_delivery.py','Hair_new.bin','Hair_new_normals.bin','Hair_target.bin','central_hair_locked.bin']:files.append((R/n,'复现/'+n))
for n in ['Direct59Build.cs','Direct59Lines.cs']:files.append((F/'Editor'/n,'复现/'+n))
z=O/'冷月Direct59_侧耳发缝闭合真实资产.zip'
with zipfile.ZipFile(z,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as q:
 for p,n in files:q.write(p,n)
with zipfile.ZipFile(z) as q:assert q.testzip() is None;print('ZIP',len(q.namelist()),'verified payloads',len(verified))
