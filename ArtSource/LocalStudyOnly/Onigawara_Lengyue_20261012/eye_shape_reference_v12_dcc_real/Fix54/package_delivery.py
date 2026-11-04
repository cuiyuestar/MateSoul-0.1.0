from pathlib import Path
import json,tarfile,hashlib,zipfile
R=Path(__file__).resolve().parent;O=Path('/Users/bytedance/DoubaoWork/chats/2026-10-19/new-chat');U=R.parents[1]/'Unity6Study';package=O/'冷月Direct54_体态刘海耳廓.unitypackage'
with tarfile.open(package,'r:gz') as t:
 members={x.name:x for x in t.getmembers()};verified=[]
 for n,m in members.items():
  if not n.endswith('/pathname'):continue
  path=t.extractfile(m).read().decode('utf-8').strip();asset=n.rsplit('/',1)[0]+'/asset'
  if asset not in members:continue
  real=U/path
  if not real.is_file() and path.startswith('Packages/'):
   _,pkg,rest=path.split('/',2);dependency=json.loads((U/'Packages/manifest.json').read_text())['dependencies'].get(pkg,'')
   if dependency.startswith('file:'):real=(U/'Packages'/dependency[5:]/rest).resolve()
   else:
    candidates=list((U/'Library/PackageCache').glob(pkg+'@*/'+rest));assert len(candidates)==1,(path,candidates);real=candidates[0]
  assert real.is_file(),path;data=t.extractfile(members[asset]).read();assert hashlib.sha256(data).digest()==hashlib.sha256(real.read_bytes()).digest(),path;verified.append(path)
 assert any(x.endswith('Face_Direct54.asset') for x in verified);assert any(x.endswith('Body_Direct54.asset') for x in verified);assert any(x.endswith('Hair_Direct54.asset') for x in verified)
(R/'package_gate.json').write_text(json.dumps({'pass':True,'verified_assets':len(verified),'all_payload_bytes_match_project':True,'paths':verified},indent=2));print('PACKAGE VERIFIED',len(verified))
notes='''# 冷月 Direct54 交付说明

## 修改范围
- 脖子、手臂及腿沿局部中心线小幅收细，目标横截面最多缩小4%，关节、袖口、手腕、脚踝及脸颈接口平滑衰减。未缩短骨骼或改变身高。
- 参考本轮预期图调整五组外围刘海宽度、位置和圆弧。中央两束、细辅助束与中央发缝逐点保持Direct53，未复制参考中的长中央裂缝。同步重建真实发束描边。
- 耳朵原本已有540顶点非平面耳壳和真实耳轮，不是仅有两张贴图。贴片感来自耳根黑缝、曲面与法线过渡及浅色卡通材质。此次重塑耳窝/耳轮，配对前后壳边界，贴合头部边界并连续处理法线。
- 耳朵仍保留原UV分岛、顶点索引与表情系统。采用几何位置接合，不是重新焊成单一拓扑；不置顶、不禁深度、不改变发束正常遮挡。
- 鼻尖回缩、脸型、眼睛、睫毛、下唇颜色与范围均未改动。原材质、贴图及骨骼未改。

## 使用
Unity：导入“冷月Direct54_体态刘海耳廓.unitypackage”，使用 Assets/Study/EyeShapeReferenceV12DCCReal/Fix54/Onigawara_Lengyue_Direct54.prefab。
Blender：打开 Onigawara_Lengyue_Direct54.blend。Shape Key已同步；沿用原项目DCC坐标约定，Unity为实际显示验收环境。
旧Direct53保持原样，可直接回退。不要将新Prefab覆盖旧版。

## 验证
独立重开Blend检查二进制坐标、UV、拓扑、权重、材质和Morph增量；Unity构建检查真实最新Mesh、原始依赖字节、非目标点、中央刘海及Morph。
同条件渲染正面、45度、右侧面及左侧面；全身正面和左右侧面。耳朵灰模为临时诊断材质与临时隐藏头发，只用于展示真实结构，未保存到Prefab。
最终UnityPackage逐项对照当前项目资产字节；ZIP CRC验证。
限制：本轮静态T姿态及零表情验收；未逐项测试所有动作、表情、物理碰撞和LOD。参考图是2D美术预期，视觉相似度未给出虚构百分比。
历史左侧头顶发缝和现有服装侧面空隙本轮未调整，不属于本轮修复范围。

## 许可
原素材继续受LocalStudyOnly限制，本包不增加商业使用或再分发授权。
'''
(R/'交付说明.md').write_text(notes)
images=[Path(p) for p in json.loads((R/'delivery_images.json').read_text())]
files=[(R/'Onigawara_Lengyue_Direct54.blend','Onigawara_Lengyue_Direct54.blend'),(package,package.name),(R/'交付说明.md','交付说明.md')]
for p in images:files.append((p,'预览/'+p.name))
for n in ['geometry_gate.json','unity_gate.json','independent_blend_gate.json','package_gate.json']:files.append((R/n,'验证/'+n))
for n in ['build_model.py','verify_blend.py','ear_boundaries.json','ear_attachment.json','coordinate_check.json']:files.append((R/n,'复现/'+n))
for n in ['Face','Hair','Body']:
 for suffix in ['_unity.bin','_normals.bin','_new.bin','_new_normals.bin','_target.bin']:files.append((R/(n+suffix),'复现/'+n+suffix))
for n in ['Body_world.bin','Face_world.bin','Body_skin.bin','central_hair_locked.bin','ear_seam_pairs.json']:files.append((R/n,'复现/'+n))
for p in (U/'Assets/Study/EyeShapeReferenceV12DCCReal/Fix54/Editor').glob('*.cs'):files.append((p,'复现/Unity/'+p.name))
# Include the precise historical source data referenced by build_model.py in their relative positions.
for fix,n in [('Fix43','strand_groups.json'),('Fix43','face_components.json'),('Fix39','hair_vertex_uv.npy'),('Fix25','hair_components.json')]:files.append((R.parent/fix/n,'复现依赖/'+fix+'/'+n))
manifest=[{'path':n,'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p,n in files];(R/'delivery_manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2));files.append((R/'delivery_manifest.json','delivery_manifest.json'))
z=O/'冷月Direct54_体态刘海耳廓真实资产.zip'
with zipfile.ZipFile(z,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as t:
 for p,n in files:t.write(p,n)
with zipfile.ZipFile(z) as t:assert t.testzip() is None;print('ZIP',len(t.namelist()),z.stat().st_size)
