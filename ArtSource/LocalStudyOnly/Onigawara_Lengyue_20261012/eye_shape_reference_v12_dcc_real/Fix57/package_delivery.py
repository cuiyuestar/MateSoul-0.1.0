from pathlib import Path
from PIL import Image,ImageDraw,ImageFont
import json,tarfile,zipfile,hashlib
R=Path(__file__).resolve().parent;O=Path('/Users/bytedance/DoubaoWork/chats/2026-10-19/new-chat');U=R.parents[1]/'Unity6Study';F=U/'Assets/Study/EyeShapeReferenceV12DCCReal/Fix57';font='/System/Library/Fonts/Hiragino Sans GB.ttc';bg=(15,18,27)
def f(n):return ImageFont.truetype(font,n)
out=Image.new('RGB',(1800,505),bg);d=ImageDraw.Draw(out);d.text((22,16),'Direct56 → Direct57 · 柔和深紫眉 · 真实Unity对照',font=f(34),fill='white')
for i,ver in enumerate(['before','after']):
 im=Image.open(R/'renders'/ver/'brow_visible_0.png').convert('RGB').crop((465,765,1135,1055));im=im.resize((900,390),Image.Resampling.LANCZOS);out.paste(im,(i*900,115));d.text((i*900+22,75),'修改前：黑色弧眉' if i==0 else '修改后：轻缓深紫弧眉',font=f(26),fill='white')
p1=O/'冷月Direct57_柔和深紫眉真实前后对照.png';out.save(p1)
out=Image.new('RGB',(2700,1015),bg);d=ImageDraw.Draw(out);d.text((22,16),'Direct57 · 正常刘海遮挡 · 真实Unity三视图',font=f(34),fill='white')
for i,(a,label) in enumerate([(0,'正面'),(45,'45°'),(90,'右侧面')]):
 im=Image.open(R/'renders/after'/f'normal_{a}.png').convert('RGB').resize((900,900),Image.Resampling.LANCZOS);out.paste(im,(i*900,115));d.text((i*900+22,75),label,font=f(28),fill='white')
p2=O/'冷月Direct57_深紫眉真实三视图.png';out.save(p2)
package=O/'冷月Direct57_柔和深紫眉.unitypackage';verified=[]
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
assert any('FaceExpression_Direct57.mat' in p for p in verified)
(R/'package_gate.json').write_text(json.dumps({'pass':True,'verified_payloads':len(verified),'all_payloads_byte_match':True},indent=2))
notes='''# 冷月 Direct57 柔和深紫眉

真实修改：在实际眉毛使用的Face表情材质槽2中，更新底色与阴影贴图的两处眉毛UV区域。颜色为深紫色，弧度更轻缓，眉峰弱化、粗细略收、末端渐细。不是AI效果图。

保护：所有网格、UV、骨骼、Morph、正常刘海遮挡、眼睛睫毛、鼻尖、下唇、脸型、皮肤、肢体、拇指修复均未改。眉毛区域外贴图像素保持不变。原Direct56未覆盖。

对照图临时隐藏Hair及发束描边，仅为展示完整眉形；最终Prefab与三视图保留原遮挡，不强行把眉毛显示在刘海外侧。

Unity使用：导入冷月Direct57_柔和深紫眉.unitypackage，使用Assets/Study/EyeShapeReferenceV12DCCReal/Fix57/Onigawara_Lengyue_Direct57.prefab。无需更换Blend网格。本轮为真实贴图/材质编辑，未生成新的Blend文件。

验证：同相机同光照真实渲染；差异像素仅集中于眉毛区域；网格引用、骨骼与原依赖字节保持不变；所有临时灯与隐藏状态清理。UnityPackage逐项字节核验，ZIP CRC核验。
限制：完成静态零表情验证；Morph未变，但未逐项审查所有动态表情。
原素材继续受LocalStudyOnly限制，不新增商业使用或再分发授权。
'''
(R/'交付说明.md').write_text(notes)
files=[(p1,'预览/'+p1.name),(p2,'预览/'+p2.name),(package,package.name),(R/'交付说明.md','交付说明.md')]
for n in ['texture_gate.json','unity_gate.json','render_difference.json','package_gate.json']:files.append((R/n,'验证/'+n))
for n in ['Face_BaseColor_Direct57.png','Face_Shade_Direct57.png']:files.append((F/'Textures'/n,'修改贴图/'+n))
files.extend([(R/'build_brow_texture.py','复现/build_brow_texture.py'),(F/'Editor/Direct57Build.cs','复现/Direct57Build.cs')])
z=O/'冷月Direct57_柔和深紫眉真实资产.zip'
with zipfile.ZipFile(z,'w',zipfile.ZIP_DEFLATED,compresslevel=5) as q:
 for p,n in files:q.write(p,n)
with zipfile.ZipFile(z) as q:assert q.testzip() is None;print('ZIP',len(q.namelist()),'verified payloads',len(verified))
