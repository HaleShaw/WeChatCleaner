using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WeChatCleaner {
 internal static partial class WpfRegression {
  static Identity DemoIdentity(Account account) {
   switch (account.Id) {
    case "wxid_wpf_1": return new Identity { Nickname="资料归档号" };
    case "wxid_wpf_2": return new Identity { Nickname="日常微信" };
    case "75450000": return new Identity { Nickname="运营组",CorpName="示例科技" };
    case "75450001": return new Identity { Nickname="产品组",CorpName="示例设计" };
    case "wxid_4": return new Identity { Nickname="示例账号 A1" };
    case "wxid_demo_b1": return new Identity { Nickname="示例账号 B1" };
    default: return new Identity();
   }
  }
  static readonly byte[] DemoPng=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a9hoAAAAASUVORK5CYII=");
  static string ImageV2(string path,byte[] key,byte? xor=null,int tailLength=12,string name="示例加密图片.dat",byte[] image=null) {
   image=image??DemoPng;
   string attach=Path.Combine(path,@"msg\attach");
   Directory.CreateDirectory(attach); Directory.CreateDirectory(Path.Combine(path,"db_storage"));
   string output=Path.Combine(attach,name);
   using (var aes=Aes.Create()) {
    aes.Key=key; aes.Mode=CipherMode.ECB; aes.Padding=PaddingMode.PKCS7;
    using (var enc=aes.CreateEncryptor()) {
     const int plainLength=32;
     int tail=xor.HasValue ? tailLength : 0;
     var cipher=enc.TransformFinalBlock(image,0,plainLength);
     var bytes=new byte[15+cipher.Length+image.Length-plainLength];
     Buffer.BlockCopy(new byte[]{7,8,0x56,0x32,8,7},0,bytes,0,6);
     Buffer.BlockCopy(BitConverter.GetBytes(plainLength),0,bytes,6,4);
     Buffer.BlockCopy(BitConverter.GetBytes(tail),0,bytes,10,4);
     Buffer.BlockCopy(cipher,0,bytes,15,cipher.Length);
     Buffer.BlockCopy(image,plainLength,bytes,15+cipher.Length,image.Length-plainLength);
     if (xor.HasValue) for (int i=bytes.Length-tail;i<bytes.Length;i++) bytes[i]^=xor.Value;
     File.WriteAllBytes(output,bytes);
    }
   }
   return output;
  }
  static void OpenSettings() { Click(Get<System.Collections.Generic.Dictionary<string,Button>>(main,"navButtons")["settings"]); }
  static string[] SettingsText() { return Visuals<TextBlock>(Get<Grid>(main,"settingsPage")).Where(t=>t.IsVisible).Select(t=>t.Text).ToArray(); }
  static void AddNewSource(string path) {
   QueueDialog("添加数据源",w=> {
    Visuals<ComboBox>(w).Single(c=>AutomationProperties.GetName(c)=="来源类型").SelectedIndex=1;
    Visuals<TextBox>(w).Single(t=>AutomationProperties.GetName(t)=="数据目录").Text=path;
    Click(ButtonAt(w,"识别并添加"));
   });
   Run(main,"AddSource");
  }
  static async Task RemoveNewSource(Account a) {
   var menu=(ContextMenu)Run(main,"BuildAccountMenu",a);
   var remove=menu.Items.OfType<MenuItem>().Single(m=>(string)m.Header=="移除数据源…");
   QueueDialog("移除数据源",w=>Click(ButtonAt(w,"移除数据源")));
   remove.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent,remove));
   await Wait(()=>!Get<bool>(main,"discovering"));
  }
  static PasswordBox KeyBox(Window window,string label) {
   return Visuals<PasswordBox>(window).Single(t=>t.IsVisible && AutomationProperties.GetName(t)==label);
  }
  static void FillKey(Window window,string aes,string xor) {
   KeyBox(window,"AES 密钥").Password=aes; KeyBox(window,"XOR 值").Password=xor;
  }
  static bool HasText(Window window,string text) {
   return Visuals<TextBlock>(window).Any(t=>t.IsVisible && t.Text.Contains(text));
  }
  static void CaptureDemo(Window window,string name) {
   var labels=Visuals<TextBlock>(window).Where(t=>t.IsVisible && t.Text.Contains(root)).Select(t=>new { Element=t,Original=t.Text }).ToList();
   foreach (var item in labels) item.Element.Text=item.Original.Replace(documents,@"D:\演示数据").Replace(root,@"D:\演示数据");
   window.UpdateLayout(); Capture(window,name);
   foreach (var item in labels) item.Element.Text=item.Original;
  }
  static Entry NewImage(string path,Account owner) {
   return new Entry { Path=path,Name=Path.GetFileName(path),OwnerRoot=owner.Path,OwnerKind=SourceKind.New };
  }
  static ImageKeyRecord KeyRecord(Account a) { return AttachmentKeys.Rows(new[]{a}).Single(r=>r.Account==a).Saved; }
  static async Task TestImageKeySettings() {
   var accounts=Get<System.Collections.Generic.List<Account>>(main,"accounts");
   var a=accounts.Single(x=>x.Kind==SourceKind.New);
   await Wait(()=>a.Nickname=="示例账号 A1");
   const string raw="WccTestImageKey1", wrongRaw="WccTestImageKey2";
   AttachmentKey key; string error; bool aesError;
   Check(AttachmentKeys.TryParse(raw,"0xA6",out key,out error,out aesError) && key.Tail==0xA6,"可录入等长 ASCII AES 和带前缀的十六进制 XOR");
   AttachmentKey unused;
   Check(!AttachmentKeys.TryParse(raw,"0x",out unused,out error,out aesError) && !aesError,"不完整的 XOR 值提示在 XOR 字段");
   string samplePath=ImageV2(a.Path,key.Aes,key.Tail);
   Check(AttachmentMedia.VerifyImageKey(a,key,null)==ImageKeyProof.Pair && AttachmentMedia.VerifyImageKey(a,key,samplePath)==ImageKeyProof.Pair,
    "V2 带 XOR 的真实封装同时验证 AES 和 XOR");
   using (var badTail=new AttachmentKey((byte[])key.Aes.Clone(),0xA7)) {
    bool rejected=false;
    try { AttachmentMedia.VerifyImageKey(a,badTail,samplePath); } catch (AttachmentKeyNeeded ex) { rejected=!ex.NeedsAes; }
    Check(rejected,"图片尾部能推导 XOR 时仍拒绝错误的输入值");
   }
   OpenSettings(); await Paint(main);
   QueueDialog("添加图片密钥",w=> {
    FillKey(w,raw,"A6");
    Check(KeyBox(w,"AES 密钥").FontFamily.Source.StartsWith("Consolas")&&KeyBox(w,"XOR 值").FontFamily.Source.StartsWith("Consolas"),"AES 和 XOR 默认遮挡，使用等宽字体");
    Click(ButtonAt(w,"显示密钥")); w.UpdateLayout();
    var fields=Visuals<TextBox>(w).Where(t=>t.IsVisible && new[]{"AES 密钥","XOR 值"}.Contains(AutomationProperties.GetName(t))).ToList();
    Check(fields.Count==2 && fields.All(t=>t.FontFamily.Source.StartsWith("Consolas")) && fields.Any(t=>t.Text==raw),"一次切换显示两个等宽值");
    Click(ButtonAt(w,"隐藏密钥")); w.UpdateLayout();
    Check(KeyBox(w,"AES 密钥").Password==raw && KeyBox(w,"XOR 值").Password=="A6","隐藏后保留两项录入值");
    Click(ButtonAt(w,"验证并保存"));
   });
   Click(ButtonAt(main,"添加密钥对")); await Paint(main);
   var prepared=AttachmentMedia.Prepare(NewImage(samplePath,a),AttachmentKeys.Load(a.Path));
   Check(prepared.KeyProof==ImageKeyProof.Pair && prepared.Extension==".png" && prepared.Data.SequenceEqual(DemoPng),"重新读取 DPAPI 密钥可解码同账号图片");
   string vault=AttachmentKeys.FilePath;
   var original=File.ReadAllBytes(vault);
   Check(!Encoding.UTF8.GetString(original).Contains(raw),"配置不含明文 AES");
   Check(SettingsText().Contains("示例账号 A1") && SettingsText().Contains("验证通过"),"当前账号显示昵称、路径和验证通过");

   QueueDialog("编辑图片密钥",async w=> {
    FillKey(w,raw,"A7"); Click(ButtonAt(w,"验证并保存"));
    await Wait(()=>HasText(w,"XOR 值与图片不匹配"));
    Check(File.ReadAllBytes(vault).SequenceEqual(original),"错误的新 XOR 不会改写已保存的密钥");
    FillKey(w,raw,"A6"); ((ImageKeyDialog)w).Report(null,false); CaptureDemo(w,"key-editor");
    FillKey(w,raw,""); Click(ButtonAt(w,"验证并保存"));
   });
   Click(ButtonAt(main,"编辑")); await Paint(main);
   using (var aesOnly=AttachmentKeys.Load(a.Path))
    Check(aesOnly!=null && !aesOnly.Tail.HasValue && SettingsText().Contains("AES 已验证"),"清空 XOR 后可单独保存已验证的 AES");
   QueueDialog("编辑图片密钥",w=> { FillKey(w,raw,"A6"); Click(ButtonAt(w,"验证并保存")); });
   Click(ButtonAt(main,"编辑"));

   await RemoveNewSource(a);
   string sourceB=Path.Combine(root,"示例数据源 B"), accountB=Path.Combine(sourceB,"wxid_demo_b1");
   Directory.CreateDirectory(Path.Combine(accountB,"db_storage")); Directory.CreateDirectory(Path.Combine(accountB,@"msg\attach"));
   AddNewSource(sourceB); var b=accounts.Single(x=>x.Path==accountB);
   await Wait(()=>b.Nickname=="示例账号 B1");
   OpenSettings(); await Paint(main);
   Check(SettingsText().Contains("示例账号 B1") && SettingsText().Contains("未配置") && SettingsText().Contains("示例账号 A1") &&
    SettingsText().Contains("来源未加载") && SettingsText().Contains("验证通过"),"移除 A 并添加 B 后同时显示历史 A1 与未配置 B1");
   Check(AttachmentKeys.Load(a.Path)!=null && AttachmentKeys.Load(accountB)==null && File.Exists(samplePath),"移除来源保留文件与密钥，B1 不借用 A1 的密钥");
   QueueDialog("历史密钥记录",w=> {
    Check(HasText(w,"上次验证结果：验证通过") && KeyBox(w,"XOR 值").Password=="0xA6","历史记录显示上次验证结果与遮挡的值");
    w.Close();
   });
   Click(ButtonAt(main,"查看记录"));
   Check(ButtonAt(main,"FFmpeg 官方下载").IsVisible && ButtonAt(main,"打开目录 ↗").IsVisible,"媒体页与配置页入口可访问");
   Visuals<ComboBox>(main).Single(c=>c.IsVisible&&AutomationProperties.GetName(c)=="清理方式").SelectedIndex=0;
   Check(!AppSettings.Load().PermanentDelete,"清理方式可以切回回收站并即时保存");
   await Wait(()=>Get<Border>(main,"toastBorder").Visibility==Visibility.Collapsed);
   var display=ConfigPath(); string actual=display.Text;
   display.Text=@"C:\Users\Demo\AppData\Local\WeChatCleaner";
   double height=main.Height;
   try { main.Height=950; await Paint(main); CaptureDemo(main,"settings"); }
   finally { main.Height=height; display.Text=actual; }

   QueueDialog("添加图片密钥",async w=> {
    FillKey(w,raw,""); Click(ButtonAt(w,"验证并保存"));
    await Wait(()=>HasText(w,"暂无可验的 V2 样本"));
    Check(AttachmentKeys.Load(accountB)==null,"无样本不会自动保存未验证密钥");
    Click(ButtonAt(w,"保存为待验证"));
   });
   Click(ButtonAt(main,"配置")); await Paint(main);
   Check(KeyRecord(b).Check==ImageKeyCheck.Pending,"无样本时可以明确保存为待验证");
   Click(ButtonAt(main,"重新验证")); await Paint(main);
   Check(KeyRecord(b).Check==ImageKeyCheck.NoSample && SettingsText().Contains("暂无可验样本"),"重新验证单独显示没有样本");
   var wrong=new AttachmentKey(Encoding.ASCII.GetBytes(wrongRaw),null);
   ImageV2(accountB,wrong.Aes);
   Click(ButtonAt(main,"重新验证")); await Paint(main);
   Check(KeyRecord(b).Check==ImageKeyCheck.Failed && SettingsText().Contains("验证失败") && AttachmentKeys.Load(accountB).Aes.SequenceEqual(key.Aes),
    "重新验证失败显示失败并保留原记录");
   QueueDialog("编辑图片密钥",w=> { FillKey(w,wrongRaw,""); Click(ButtonAt(w,"验证并保存")); });
   Click(ButtonAt(main,"编辑"));
   Check(AttachmentKeys.Load(accountB).Aes.SequenceEqual(wrong.Aes) && AttachmentKeys.Load(a.Path).Aes.SequenceEqual(key.Aes),"编辑 B1 只影响 B1，A1 历史密钥仍可用");

   var namesake=new Account { Kind=SourceKind.New,Id="wxid_namesake",Nickname=a.Nickname,Path=Path.Combine(root,"示例同名账号","wxid_namesake") };
   var namesakeRow=AttachmentKeys.Rows(new[]{namesake}).Single(r=>r.Account==namesake);
   Check(namesakeRow.Saved==null && namesakeRow.Relocation==null,"同昵称但账号标识不同，不复用历史密钥");
   AddNewSource(newer);
   var restored=AttachmentKeys.Rows(accounts).Single(r=>r.Account!=null && r.Account.Path==a.Path);
   Check(restored.CanUnlock && SourceStore.Load().Any(s=>s.Path==newer&&!s.Excluded),"重新添加同一目录立即复用旧密钥");
   await RemoveNewSource(accounts.Single(x=>x.Path==a.Path));
   string movedSource=Path.Combine(root,"示例数据源 A 已迁移"), movedPath=Path.Combine(movedSource,a.Id);
   string movedSample=ImageV2(movedPath,key.Aes,key.Tail);
   AddNewSource(movedSource); OpenSettings(); await Paint(main);
   Check(SettingsText().Contains("待验证") && AttachmentKeys.Load(movedPath)==null,"同账号新路径先标记待验证，不自动接入历史密钥");
   bool foreignSample=false;
   try { AttachmentMedia.VerifyImageKey(accounts.Single(x=>x.Path==movedPath),key,samplePath); } catch (IOException) { foreignSample=true; }
   Check(foreignSample,"密钥验证不能使用其他目录的图片");
   QueueDialog("关联历史密钥",w=>Click(ButtonAt(w,"验证并关联")));
   Click(ButtonAt(main,"关联旧记录"));
   Check(AttachmentKeys.Load(movedPath)!=null && AttachmentKeys.Load(a.Path)!=null && File.Exists(movedSample),"验证新目录后建立关联，并保留旧路径记录");
   Check(AttachmentKeys.Rows(accounts).Count(r=>r.Relocation!=null)==0,"关联后同一条当前记录不再提示关联");
   TestKeyAmbiguity(a.Id,movedPath,key);

   string legacyRoot=Path.Combine(root,"旧记录","wxid_legacy");
   string slot=AttachmentKeys.PathSlot(legacyRoot);
   string encrypted=Convert.ToBase64String(ProtectedData.Protect(key.Aes,AttachmentKeys.Entropy(slot),DataProtectionScope.CurrentUser));
   File.AppendAllText(vault,slot+"\t"+encrypted+"\n",ProfileStorage.Utf8);
   var legacy=AttachmentKeys.Rows(accounts).Single(r=>r.Saved!=null && r.Saved.Slot==slot);
   Check(legacy.CanUnlock && legacy.Title=="旧版密钥记录" && legacy.Saved.Check==ImageKeyCheck.Pending,"旧版无元数据记录可解锁并标为待验证");
   var oldAccount=new Account { Kind=SourceKind.New,Path=legacyRoot,Id="wxid_legacy",Nickname="示例历史账号" };
   AttachmentKeys.Rows(accounts.Concat(new[]{oldAccount}));
   legacy=AttachmentKeys.Rows(accounts).Single(r=>r.Saved!=null && r.Saved.Slot==slot);
   Check(legacy.Account==null && legacy.Title=="示例历史账号","旧记录遇到原目录后补全账号名，退出数据源后仍显示");
   Check(AttachmentKeys.Remove(legacy.Saved) && AttachmentKeys.Load(movedPath)!=null,"移除单个记录保留其他账号密钥");
   await TestImageKeyFileActions();
  }
  static void TestKeyAmbiguity(string id,string moved,AttachmentKey key) {
   string other=Path.Combine(root,"第二份备份",id), third=Path.Combine(root,"第三份备份",id);
   var row=new Account { Kind=SourceKind.New,Id=id,Path=third };
   Check(AttachmentKeys.Remember(other,key,"示例备份",ImageKeyProof.Pair),"保存另一处同 ID 账号目录的独立密钥");
   Check(AttachmentKeys.Rows(new[]{row}).Single(r=>r.Account==row).Relocation==null,"多个同 ID 历史位置时不猜测密钥");
   Check(AttachmentKeys.Rows(new[]{row,new Account { Kind=SourceKind.New,Id=id,Path=moved }}).Single(r=>r.Account==row).Relocation==null,"多个当前同 ID 目录时不自动关联密钥");
   var saved=AttachmentKeys.Rows(new Account[0]).Single(r=>r.Saved.AccountRoot==other);
   Check(AttachmentKeys.Remove(saved.Saved),"移除测试备份的单账号密钥");
  }

  static void TestPaddedImageTails() {
   var account=new Account { Path=Path.Combine(root,"图片尾部填充","wxid_padding"),Kind=SourceKind.New };
   var jpgEncoder=new JpegBitmapEncoder();
   var pixels=new byte[]{0x00,0x90,0xe0,0xb8,0x40,0x10,0xee,0xee,0x00,0x44,0x55,0x66};
   jpgEncoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2,2,96,96,PixelFormats.Bgr24,null,pixels,6)));
   byte[] jpg;
   using (var image=new MemoryStream()) {
    jpgEncoder.Save(image);
    var bytes=image.ToArray();
    // Length encoded APP marker can contain an apparent EOI, e.g. inside a thumbnail.
    jpg=bytes.Take(2).Concat(new byte[]{0xff,0xe2,0,6,0x50,0xff,0xd9,0x51}).Concat(bytes.Skip(2)).ToArray();
   }
   var paddedJpg=jpg.Concat(Enumerable.Repeat((byte)0x4b,48935)).ToArray();
   // The correct EOI is followed by fill including a second FFD9. The last two bytes
   // also look like FFD9 under a different XOR, so inferring from the last bytes is unsafe.
   paddedJpg[jpg.Length]=0xfe; paddedJpg[jpg.Length+1]=0x30;
   paddedJpg[jpg.Length+20]=0xff; paddedJpg[jpg.Length+21]=0xd9;
   paddedJpg[paddedJpg.Length-2]=0xfe; paddedJpg[paddedJpg.Length-1]=0xd8;
   using (var key=new AttachmentKey(Encoding.ASCII.GetBytes("WccTestImageKey4"),0xa6))
   using (var wrong=new AttachmentKey((byte[])key.Aes.Clone(),0xa7))
   using (var aesOnly=new AttachmentKey((byte[])key.Aes.Clone(),null)) {
    var jpgPath=ImageV2(account.Path,key.Aes,key.Tail,paddedJpg.Length-jpg.Length+48,"带填充的 JPEG.dat",paddedJpg);
    var jpeg=AttachmentMedia.Prepare(NewImage(jpgPath,account),key);
    Check(jpeg.Extension==".jpg" && jpeg.KeyProof==ImageKeyProof.Pair && jpeg.Data.SequenceEqual(jpg) &&
     AttachmentMedia.VerifyImageKey(account,key,jpgPath)==ImageKeyProof.Pair,"JPEG 以有效扫描的结束标记截去填充，忽略段内和填充里的假标记");
    bool rejected=false;
    try { AttachmentMedia.Prepare(NewImage(jpgPath,account),wrong); } catch (AttachmentKeyNeeded ex) { rejected=!ex.NeedsAes; }
    Check(rejected,"带填充的 JPEG 不接受只在末端产生结束标记的错误 XOR");
    bool needsTail=false;
    try { AttachmentMedia.Prepare(NewImage(jpgPath,account),aesOnly); } catch (AttachmentKeyNeeded ex) { needsTail=ex.NeedsTail; }
    Check(needsTail,"填充导致无法从末端确认 XOR 时请求用户输入");
    var paddedPng=DemoPng.Concat(Enumerable.Repeat((byte)0x4b,9)).Concat(DemoPng.Skip(DemoPng.Length-12)).ToArray();
    var pngPath=ImageV2(account.Path,key.Aes,key.Tail,paddedPng.Length-DemoPng.Length+12,"带填充的 PNG.dat",paddedPng);
    var png=AttachmentMedia.Prepare(NewImage(pngPath,account),key);
    Check(png.Extension==".png" && png.KeyProof==ImageKeyProof.Pair && png.Data.SequenceEqual(DemoPng),"PNG 按区块识别真正的 IEND 并截去其后内容");
    var paddingOnly=ImageV2(account.Path,key.Aes,key.Tail,paddedPng.Length-DemoPng.Length,"只加密填充的 PNG.dat",paddedPng);
    var beforeTail=AttachmentMedia.Prepare(NewImage(paddingOnly,account),aesOnly);
    Check(beforeTail.KeyProof==ImageKeyProof.Aes && beforeTail.Data.SequenceEqual(DemoPng),"逻辑图片在 XOR 段之前已结束时，仅验证 AES");

    var twoImages=jpg.Concat(jpg).ToArray();
    var videoIndex=Mp4Box("moov",Mp4Box("mvhd",new byte[24]));
    var motion=twoImages.Concat(new byte[16]).Concat(Mp4Box("ftyp",Encoding.ASCII.GetBytes("isom\0\0\0\0")))
     .Concat(Mp4Box("free",new byte[8])).Concat(Mp4Box("mdat",new byte[64])).Concat(videoIndex).ToArray();
    var motionWithPadding=motion.Concat(Enumerable.Repeat((byte)0x4b,96)).Concat(new byte[]{0xff,0xd9,0x51}).ToArray();
    var motionPath=ImageV2(account.Path,key.Aes,key.Tail,videoIndex.Length+48+motionWithPadding.Length-motion.Length,"带视频的多图 JPEG.dat",motionWithPadding);
    var moving=AttachmentMedia.Prepare(NewImage(motionPath,account),key);
    Check(moving.Extension==".jpg" && moving.KeyProof==ImageKeyProof.Pair && moving.Data.SequenceEqual(motion) &&
     AttachmentMedia.VerifyImageKey(account,key,motionPath)==ImageKeyProof.Pair,"保留两张 JPEG 和完整附带视频，只截掉视频后的填充");
    rejected=false;
    try { AttachmentMedia.Prepare(NewImage(motionPath,account),wrong); } catch (AttachmentKeyNeeded ex) { rejected=ex.NeedsTail; }
    Check(rejected,"附带视频的后半段未解开时，不会把第一张 JPEG 当成完整文件");
    needsTail=false;
    try { AttachmentMedia.Prepare(NewImage(motionPath,account),aesOnly); } catch (AttachmentKeyNeeded ex) { needsTail=ex.NeedsTail; }
    Check(needsTail,"只提供 AES 时不会截掉仍需 XOR 的附带视频");

    var twoPath=ImageV2(account.Path,key.Aes,key.Tail,32,"不带填充的多图 JPEG.dat",twoImages);
    var inferred=AttachmentMedia.Prepare(NewImage(twoPath,account),aesOnly);
    Check(inferred.KeyProof==ImageKeyProof.Aes && inferred.Data.SequenceEqual(twoImages),"不带填充的多图 JPEG 仍可从确切结尾推断 XOR");
   }
  }

  static byte[] Mp4Box(string name,byte[] payload) {
   var sizes=BitConverter.GetBytes(payload.Length+8);
   if (BitConverter.IsLittleEndian) Array.Reverse(sizes);
   return sizes.Concat(Encoding.ASCII.GetBytes(name)).Concat(payload).ToArray();
  }

  static async Task TestImageKeyFileActions() {
   const string initial="WccTestImageKey1", changed="WccTestImageKey3";
   var a=new Account { Path=Path.Combine(root,"示例数据源 C","wxid_demo_c1"),Id="wxid_demo_c1",Kind=SourceKind.New,Nickname="示例账号 C1" };
   string sample=ImageV2(a.Path,Encoding.ASCII.GetBytes(initial),0xA6,1,"聊天图片.dat");
   var entry=NewImage(sample,a); var ciphertext=File.ReadAllBytes(sample);
   string export=Path.Combine(root,"另存示例"); Directory.CreateDirectory(export);
   string raw=Path.Combine(export,entry.Name), decoded=Path.Combine(export,"聊天图片.png");
   QueueDialog("验证密钥以查看图片",async w=> {
    FillKey(w,initial,"A7"); Click(ButtonAt(w,"验证并查看"));
    await Wait(()=>HasText(w,"XOR 值与图片不匹配"));
    Check(AttachmentKeys.Load(a.Path)==null,"文件操作验证失败后原位置没有记录");
    CaptureDemo(w,"key-original");
    Click(ButtonAt(w,"保存加密原件"));
   });
   await FileActions.Open(main,entry,accountName:a.DisplayName,targets:new FileActionTargets(d=>{
    Check(d.Title=="保存加密原件" && d.FileName==entry.Name && d.DefaultExt=="dat","原件选项使用原文件名和扩展名");
    d.FileName=raw; return true;
   }));
   Check(File.ReadAllBytes(raw).SequenceEqual(ciphertext) && AttachmentKeys.Load(a.Path)==null,"错误密钥仍能写出原件，原件逐字节一致且不记住密钥");
   QueueDialog("验证密钥并另存图片",w=> { FillKey(w,initial,"A6"); Click(ButtonAt(w,"验证并另存")); });
   await FileActions.Save(main,entry,null,accountName:a.DisplayName,targets:new FileActionTargets(_=>false));
   Check(AttachmentKeys.Load(a.Path)==null && !File.Exists(decoded),"验证通过后取消另存不写文件或记住密钥");
   QueueDialog("验证密钥并另存图片",w=> { FillKey(w,initial,"A6"); Click(ButtonAt(w,"验证并另存")); });
   bool rejected=false;
   try {
    await FileActions.Save(main,entry,null,accountName:a.DisplayName,targets:new FileActionTargets(d=> { d.FileName=sample; return true; }));
   } catch (IOException) { rejected=true; }
   Check(rejected && AttachmentKeys.Load(a.Path)==null && File.ReadAllBytes(sample).SequenceEqual(ciphertext),
    "拒绝覆盖原件，写出失败时也不记住密钥");
   QueueDialog("验证密钥并另存图片",w=> { FillKey(w,initial,"A6"); Click(ButtonAt(w,"验证并另存")); });
   await FileActions.Save(main,entry,null,accountName:a.DisplayName,targets:new FileActionTargets(d=> {
    Check(d.FileName=="聊天图片.png" && AttachmentKeys.Load(a.Path)==null,"选择保存路径时尚未记住密钥");
    d.FileName=decoded; return true;
   }));
   Check(File.ReadAllBytes(decoded).SequenceEqual(DemoPng) && KeyRecord(a).Proof==ImageKeyProof.Pair,"解码另存成功才自动记住密钥对");
   bool opened=false;
   var viewer=new FileActionTargets(null,path=> {
    opened=true; Check(Path.GetExtension(path)==".png" && File.ReadAllBytes(path).SequenceEqual(DemoPng),"复用已保存的值并传给图片查看器");
   });
   await FileActions.Open(main,entry,accountName:a.DisplayName,targets:viewer);
   Check(opened,"密钥已保存时无需打开输入弹窗");

   string after=ImageV2(a.Path,Encoding.ASCII.GetBytes(changed),name:"只需 AES 的图片.dat");
   QueueDialog("验证密钥以查看图片",w=> {
    Check(KeyBox(w,"XOR 值").Password=="0xA6","当前账号的旧值在重试时正确预填");
    FillKey(w,changed,""); Click(ButtonAt(w,"验证并查看"));
   });
   await FileActions.Open(main,NewImage(after,a),accountName:a.DisplayName,targets:viewer);
   using (var stored=AttachmentKeys.Load(a.Path))
    Check(stored.Aes.SequenceEqual(Encoding.ASCII.GetBytes(changed)) && stored.Tail==0xA6 && KeyRecord(a).Proof==ImageKeyProof.Aes,
     "查看成功后自动记住新 AES，保留旧 XOR 并标为待验证");
   Check(File.ReadAllBytes(sample).SequenceEqual(ciphertext),"所有文件查看和另存都保留微信原件");
   Check(AttachmentKeys.Remove(KeyRecord(a)),"清除文件流程测试的合成密钥记录");
  }

  static TextBox ConfigPath() { return Visuals<TextBox>(main).Single(t=>t.IsVisible&&AutomationProperties.GetName(t)=="配置目录路径"); }
  static void SelectProfileMode(string label) { Visuals<RadioButton>(main).Single(r=>r.IsVisible&&(string)r.Content==label).IsChecked=true; }
  static void ChangeProfile(string decision) {
   QueueDialog("切换配置目录",w=>Run(ButtonAt(w,decision),"OnClick"));
   Click(ButtonAt(main,"应用配置目录"));
  }
  static async Task TestProfileSettings() {
   OpenSettings(); await Paint(main);
   var store=ProfileStorage.App;
   string original=store.Current.Path;
   string custom=Path.Combine(root,"自定义配置");
   var stored=File.ReadAllBytes(AttachmentKeys.FilePath);
   int count=SourceStore.Load().Count;
   SelectProfileMode("自定义目录"); ConfigPath().Text=custom;
   Check(!ConfigPath().IsReadOnly && ButtonAt(main,"浏览…").IsEnabled,"自定义路径可输入并可浏览目录");
   ChangeProfile("取消");
   Check(store.Current.Path==original && !Directory.Exists(custom),"取消切换不创建目标或变动当前配置");
   ChangeProfile("复制并启用");
   await Wait(()=>!Get<bool>(main,"discovering")); await Paint(main);
   Check(store.Current.Mode==ProfileMode.Custom && store.Current.Path==custom && SourceStore.Load().Count==count &&
    AppSettings.Load().Days==73 && File.ReadAllBytes(AttachmentKeys.FilePath).SequenceEqual(stored),"自定义目录即时启用，完整复制设置、来源和加密密钥");
   Check(Directory.Exists(original) && new ProfileStorage(Path.GetDirectoryName(store.Propose(ProfileMode.Portable,null).Path),original).Resolve(null).Path==custom,
    "原配置保留；重新定位程序时记住同一个自定义目录");
   string overridePath=Path.Combine(root,"外部隔离配置");
   Check(store.Resolve(overridePath).Path==overridePath && store.Resolve(overridePath).Mode==ProfileMode.Environment,"启动环境变量优先覆盖当前配置目录");
   string error;
   var forbidden=store.Propose(ProfileMode.Custom,Path.Combine(account,"配置"));
   Check(!store.TrySwitch(store.Current,forbidden,true,new[]{account},out error) && store.Current.Path==custom && !Directory.Exists(forbidden.Path),"禁止迁移到微信数据目录内，失败后原配置不变");

   SelectProfileMode("随程序携带"); Check(ConfigPath().IsReadOnly&&ConfigPath().Text.EndsWith("\\config"),"便携模式展示程序旁的 config 目录");
   ChangeProfile("复制并启用");
   await Wait(()=>!Get<bool>(main,"discovering")); await Paint(main);
   Check(store.Current.Mode==ProfileMode.Portable && File.Exists(store.Marker) && File.ReadAllBytes(AttachmentKeys.FilePath).SequenceEqual(stored),"便携模式迁移并写入随配置携带的标记");
   string replica=Path.Combine(root,"移动后的程序"), replicaConfig=Path.Combine(replica,"config"); Directory.CreateDirectory(replicaConfig);
   foreach (var name in new[]{Path.GetFileName(store.Marker),ProfileStorage.SettingsFile,ProfileStorage.SourcesFile,ProfileStorage.KeysFile})
    File.Copy(Path.Combine(store.Current.Path,name),Path.Combine(replicaConfig,name));
   var moved=new ProfileStorage(replica,Path.Combine(root,"另一份 Windows 配置"));
   Check(moved.Resolve(null).Path==replicaConfig && moved.Resolve(null).Mode==ProfileMode.Portable && !File.Exists(moved.Locator),"移动程序和 config 后，不靠旧电脑定位记录也能加载");

   var keep=Visuals<ComboBox>(main).Single(c=>c.IsVisible && AutomationProperties.GetName(c)=="默认保留时间");
   Check(keep.IsEditable,"默认保留时间允许选择预设和输入天数");
   keep.SelectedItem="自定义…";
   var keepBox=(TextBox)keep.Template.FindName("PART_EditableTextBox",keep); keepBox.Text="147";
   var enter=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(keep),0,System.Windows.Input.Key.Enter);
   enter.RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent; keep.RaiseEvent(enter);
   Check(AppSettings.Load().Days==147,"自定义保留天数用回车应用并保存");
   SelectProfileMode("Windows 用户目录");
   ChangeProfile("备份后复制");
   await Wait(()=>!Get<bool>(main,"discovering")); await Paint(main);
   string backup=Directory.GetDirectories(original,"配置备份-*").Single();
   Check(store.Current.Mode==ProfileMode.WindowsUser && AppSettings.Load().Days==147 && AppSettings.LoadFrom(Path.Combine(backup,"settings.txt")).Days==73,
    "替换现有配置前保留备份，可恢复原保留天数");
   var destinationSettings=AppSettings.LoadFrom(Path.Combine(custom,ProfileStorage.SettingsFile));
   destinationSettings.MinBytes=100L*1024*1024; destinationSettings.UseCreated=true;
   if (!destinationSettings.Save()) throw new IOException("无法写入测试配置");
   SelectProfileMode("自定义目录"); ConfigPath().Text=custom; ChangeProfile("使用目标配置");
   await Wait(()=>!Get<bool>(main,"discovering")); await Paint(main);
   Check(store.Current.Path==custom && AppSettings.Load().Days==73 && AppSettings.LoadFrom(Path.Combine(original,"settings.txt")).Days==147,
    "可启用目标原有配置，分别保留各目录的设置");
   Check(Get<ComboBox>(main,"ageBox").Text=="超过 73 天" && Get<ComboBox>(main,"sizeBox").SelectedIndex==2 &&
    Get<ComboBox>(main,"dateBox").SelectedIndex==1,"配置切换后，账号页的时间、大小和日期字段同步加载");

   var locator=File.ReadAllText(store.Locator);
   string missing=Path.Combine(root,"未挂载配置");
   try {
    ProfileStorage.WriteText(store.Locator,"custom\n"+missing);
    OpenSettings(); await Paint(main);
    Check(store.Current.Problem!=null && !AppSettings.Load().Save() && !Directory.Exists(missing) && SettingsText().Any(t=>t.Contains("配置目录不可用")),
     "配置目录断开时显示错误，禁止写入或创建空配置");
    ProfileStorage.WriteText(store.Locator,locator);
    Click(ButtonAt(main,"重试")); await Wait(()=>!Get<bool>(main,"discovering"));
    Check(store.Current.Path==custom && AppSettings.Load().Days==73 && Get<System.Collections.Generic.List<Account>>(main,"accounts").Any(a=>a.Kind==SourceKind.New),
     "目录恢复后重读原设置与账号");
   } finally { ProfileStorage.WriteText(store.Locator,locator); }
  }
 }
}
