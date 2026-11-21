using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Xml;

namespace WeChatCleaner {
 internal static class SelfTest {
  sealed class Test {
   readonly string root;
   readonly DateTime today=DateTime.Now;
   readonly List<string> results=new List<string>();
   const long MB=1024*1024;
   internal Test(string root) { this.root=root; }
   void Check(bool condition,string name) { if (!condition) throw new InvalidOperationException(name); results.Add("PASS " + name); }
   string Fixture(string dir,string name,long size,int days,bool copy=false) {
    Directory.CreateDirectory(dir);
    string file=Path.Combine(dir,name);
    using (var fs=new FileStream(file,FileMode.CreateNew,FileAccess.Write)) { fs.WriteByte(copy ? (byte)167 : (byte)33); fs.SetLength(size); }
    File.SetCreationTime(file,today.AddDays(-days-3)); File.SetLastWriteTime(file,today.AddDays(-days)); return file;
   }
   internal void Run() {
    try { RunCore(); }
    catch (Exception ex) { results.Add("FAIL " + ex); throw; }
    finally { File.WriteAllLines(Path.Combine(root,"result.txt"),results,Encoding.UTF8); }
   }
   void RunCore() {
    results.Add("Windows " + Environment.OSVersion.Version + " · " + (Environment.Is64BitProcess ? "x64" : "x86") + " process");
    string old=Path.Combine(root,"旧版 微信"), newer=Path.Combine(root,"新版 微信"), work=Path.Combine(root,"企业微信");
    string a=Path.Combine(old,"wxid_fixture_a"), b=Path.Combine(old,"wxid_fixture_b"), n=Path.Combine(newer,"wxid_fixture_4x"), w=Path.Combine(work,"12345000");
    string df=Path.Combine(a,@"FileStorage\File"), protectedDir=Path.Combine(a,"Msg");
    string small=Fixture(df,"旧附件.txt",3*MB,65), large=Fixture(df,"大附件.bin",21*MB,30);
    File.SetLastWriteTime(large,today.AddDays(-63)); File.SetCreationTime(large,today.AddDays(-1));
    string protect=Fixture(protectedDir,"保护.db",2*MB,80);
    Directory.CreateDirectory(Path.Combine(b,"FileStorage"));
    Directory.CreateDirectory(Path.Combine(n,"db_storage"));
    string d1=Fixture(Path.Combine(n,@"msg\file"),"副本 A.bin",2*MB,10,true);
    string d2=Fixture(Path.Combine(a,@"FileStorage\Video"),"副本 B.bin",2*MB,12,true);
    Fixture(Path.Combine(a,@"FileStorage\Video"),"不同内容.bin",2*MB,12);
    Directory.CreateDirectory(Path.Combine(w,"Data"));
    Directory.CreateDirectory(Path.Combine(work,@"12345001\Data"));
    Directory.CreateDirectory(Path.Combine(work,@"not-an-account\Data"));
    string copy=Fixture(Path.Combine(b,@"FileStorage\Cache"),"跨账号.bin",2*MB,9,true);
    var accounts=Engine.Discover(old+"/",SourceKind.Old);
    Check(accounts.Count==2 && Engine.Discover(a+"/",SourceKind.Old).Count==1,"旧版多账号、自定义目录和账号目录直输");
    Check(Engine.Discover(newer,SourceKind.New).Count==1 && Engine.Discover(work,SourceKind.Work).Count==2,"新版微信与企业微信多账号发现");
    Check(Engine.Discover(Path.Combine(root,"missing"),SourceKind.New).Count==0,"不可用路径");
    VerifyDiscovery(old,a);
    using (var process=System.Diagnostics.Process.GetCurrentProcess())
     Check(ClientProcesses.Images(process.ProcessName).Any(p=>String.Equals(p,Assembly.GetExecutingAssembly().Location,StringComparison.OrdinalIgnoreCase)),
      "使用当前位数对应的 Windows API 读取进程路径");
    Check(NameResolver.Resolve(accounts[0]).Nickname==null && accounts[0].DisplayName==accounts[0].Id,"缺少可信资料时使用目录标识");
    VerifyIdentity(accounts[0],newer,work);
    var result=Engine.Scan(accounts[0],()=>false,i=>{});
    var files=result.Categories.First(c=>c.Name=="接收的文件");
    Check(result.Errors.Count==0 && files.Files.Count==2 && files.Total==24*MB && result.TotalBytes==30*MB,"按类别统计原始字节，包含受保护文件");
    int changes=0;
    files.PropertyChanged+=delegate(object sender,System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName=="MatchedText") changes++; };
    files.Selected=true;
    Engine.Filter(result,30,0,false);
    Check(files.Matched==24*MB && files.MatchedCount==2,"修改时间命中 24 MB");
    Engine.Filter(result,30,0,true);
    Check(files.Matched==3*MB && files.MatchedCount==1 && files.Total==24*MB && changes==2,"创建时间命中 3 MB，绑定通知及稳定总量");
    var one=files.Files.Single(f=>f.Path==small); one.Excluded=true;
    Check(files.Matched==3*MB && result.Pending.Count==0,"排除只影响待清理文件");
    one.Excluded=false;
    Engine.Filter(result,0,3*MB,false);
    Check(result.Pending.Count==2 && result.Pending.Sum(f=>f.Size)==24*MB,"精确大小临界值与清单一致");
    Engine.Filter(result,36500,0,true);
    Check(result.MatchedCount==0 && result.Pending.Count==0,"无匹配与大整数天数");
    Check(Engine.Scan(accounts[0],()=>true,i=>{}).Cancelled && Engine.Scan(accounts[1],()=>false,i=>{}).TotalCount==1,"取消及单账号扫描隔离");
    var all=new List<Entry>();
    var r4=Engine.Scan(Engine.Discover(newer,SourceKind.New)[0],()=>false,i=>{});
    var rB=Engine.Scan(accounts[1],()=>false,i=>{});
    all.AddRange(result.Categories.Where(c=>!c.Protected).SelectMany(c=>c.Files));
    all.AddRange(r4.Categories.SelectMany(c=>c.Files)); all.AddRange(rB.Categories.SelectMany(c=>c.Files));
    var dup=DuplicateScanner.Scan(all,()=>false,i=>{},MB);
    Check(dup.Groups.Count==1 && dup.Groups[0].Files.Count==3 && dup.WastedTotal==4*MB && dup.Errors.Count==0,"同内容异名跨三账号，区分相同大小不同内容");
    Check(DuplicateScanner.Scan(new[]{one},()=>false,i=>{},1).Groups.Count==0,"查重无结果");
    var protectedEntry=result.Categories.SelectMany(c=>c.Files).First(f=>f.Path==protect);
    var sink=new RecycleShell.Sink(one,a);
    Check(sink.PreDeleteItem(0,IntPtr.Zero)<0 && File.Exists(one.Path),"拒绝非回收的 Windows 清理请求");
    var reject=Engine.DeleteFiles(new List<Entry>{protectedEntry,one},b,true);
    Check(reject.Skipped==2 && File.Exists(protect) && File.Exists(small),"保护与越界服务校验");
    reject=Engine.DeleteFiles(new List<Entry>{protectedEntry},a,true);
    Check(reject.Skipped==1&&File.Exists(protect),"保护分类即使传入正确账号也不能永久删除");
    File.AppendAllText(small,"changed");
    reject=Engine.DeleteFiles(new List<Entry>{one},a,false);
    Check(reject.Skipped==1&&File.Exists(small),"复核文件变化后跳过");

    string options=Path.Combine(root,"settings.txt");
    new AppSettings { OverridePath=options,Days=73,PageSize=65,PermanentDelete=true,LastAccount="0|"+a }.Save();
    var saved=AppSettings.LoadFrom(options);
    Check(saved.Days==73&&saved.PageSize==65&&saved.PermanentDelete&&saved.LastAccount=="0|"+a,"配置重启读写及独立配置路径");
    Check(!AppSettings.LoadFrom(Path.Combine(root,"missing.txt")).PermanentDelete,"未设置时默认回收站");
    VerifyExport(small);

    // Every destructive call is restricted to files created in this unique fixture.
    var batch = new List<string>();
    batch.Add(Fixture(df,"回收站核验 1.bin",13271,1));
    batch.Add(Fixture(df,"回收站核验 2.bin",45082,2));
    string locked=Fixture(df,"正在使用.bin",6031,3), purge=Fixture(df,"永久删除.bin",9216,3),
     legacy=Fixture(df,"Win7 回收入口.bin",8363,1);
    var scan=Engine.Scan(accounts[0],()=>false,i=>{}).Categories.SelectMany(c=>c.Files).ToList();
    var recycle=scan.Where(f=>batch.Contains(f.Path)).ToList();
    var checksums=recycle.ToDictionary(f=>f.Path,f=>Hash(f.Path));
    using (var lockFile=new FileStream(locked,FileMode.Open,FileAccess.Read,FileShare.Read)) {
     recycle.Insert(1,scan.Single(f=>f.Path==locked));
     var report=Engine.DeleteToRecycleBin(recycle,a);
     results.Add(report.Summary);
     results.AddRange(report.Errors);
     Check(report.Recycled==2 && report.Unverified==0 && report.Failed==1 && File.Exists(locked),"回收站逐项核验；遇到占用仍处理其他文件");
    }
    foreach (var file in recycle.Where(f=>batch.Contains(f.Path))) {
     Check(!File.Exists(file.Path),"回收后原路径不存在");
     Restore(file,checksums[file.Path]);
     Check(File.Exists(file.Path)&&Hash(file.Path)==checksums[file.Path],"回收数据按路径和大小找到，恢复后 SHA-256 相同");
    }
    var legacyFile=scan.Single(f=>f.Path==legacy);
    var legacyHash=Hash(legacy);
    Check(new RecycleShell().MoveLegacy(legacyFile,a) && !File.Exists(legacy),"Win7 兼容 API 将生成的文件移入回收站");
    Restore(legacyFile,legacyHash);
    Check(File.Exists(legacy) && Hash(legacy)==legacyHash,"旧版回收 API 的 $I/$R 记录可核对，恢复内容一致");
    var permanent=Engine.DeletePermanently(scan.Where(f=>f.Path==purge).ToList(),a);
    Check(permanent.Purged==1 && permanent.Unverified==0 && !File.Exists(purge),"隔离测试的永久删除分支");
   }
   void VerifyDiscovery(string savedRoot,string savedAccount) {
    string docs=Path.Combine(root,"重定向的文档"), roam=Path.Combine(root,"roaming"), local=Path.Combine(root,"local");
    string docOld=MakeAccount(docs,@"WeChat Files\wxid_doc_old","FileStorage");
    string docNew=MakeAccount(docs,@"xwechat_files\wxid_doc_new","db_storage","msg");
    string docWork1=MakeAccount(docs,@"WXWork\12345601","Data");
    string docWork2=MakeAccount(docs,@"WXWork\12345602","Data");
    MakeAccount(docs,@"WXWork\尚未登录","Data");
    MakeAccount(docs,@"xwechat_files\empty_lookalike","db_storage");
    string custom=Path.Combine(root,"安装版自定数据位置");
    string installed=MakeAccount(custom,@"WeChat Files\wxid_installed","FileStorage");
    string upgraded=MakeAccount(custom,@"xwechat_files\wxid_upgrade","db_storage","msg");
    string fromConfig=MakeAccount(root,@"不在文档中的新路径\wxid_config","db_storage","msg");
    string cfg=Path.Combine(roam,@"Tencent\xwechat\config"); Directory.CreateDirectory(cfg);
    File.WriteAllText(Path.Combine(cfg,"51a1fffea11325a1e4104c6b3de47af7.ini"),Path.GetDirectoryName(fromConfig)+"\r\n",Encoding.Unicode);
    File.WriteAllText(Path.Combine(cfg,"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.ini"),"MyDocument:");
    string portable=Path.Combine(root,"PortableApps"), pDir=Path.Combine(portable,"WXWorkPortable");
    Directory.CreateDirectory(pDir); File.WriteAllBytes(Path.Combine(pDir,"WXWorkPortable.exe"),new byte[0]);
    string fromPortable=MakeAccount(pDir,@"Data\WXWork\12345603","Data");
    string shortcutDir=Path.Combine(root,"快捷方式"), clientDir=Path.Combine(root,"绿色微信任意文件夹");
    Directory.CreateDirectory(shortcutDir); Directory.CreateDirectory(clientDir);
    string exe=Path.Combine(clientDir,"Weixin.exe"); File.WriteAllBytes(exe,new byte[0]);
    string fromShortcut=MakeAccount(root,@"任意数据位置\wxid_shortcut","db_storage","msg");
    File.WriteAllText(Path.Combine(clientDir,"config.ini"),"[Storage]\r\nFileSavePath=..\\任意数据位置\r\n",Encoding.UTF8);
    SaveFixtureShortcut(Path.Combine(shortcutDir,"微信绿色版.lnk"),exe,clientDir);
    var saved = new[]{ new SourceEntry{Kind=SourceKind.Old,Path=savedRoot},
     new SourceEntry{Kind=SourceKind.Old,Path=savedAccount+"/"}, new SourceEntry{Kind=SourceKind.Work,Path=Path.Combine(root,"missing")} };
    var input = new DiscoveryLocations { Documents=docs, Roaming=roam, Local=local,
     InstallParents=new[]{portable}, ShortcutFolders=new[]{shortcutDir},
     RegisteredPaths=delegate(CancellationToken token){ return new[]{new SourceEntry{Kind=SourceKind.Old,Path=custom},
      new SourceEntry{Kind=SourceKind.Old,Path="MyDocument:"}, new SourceEntry{Kind=SourceKind.New,Path="*invalid*"}}; } };
    var found=Task.Run(delegate { return SourceDiscovery.Find(saved,input,CancellationToken.None); }).GetAwaiter().GetResult();
    var paths=new HashSet<string>(found.Select(a=>a.Path),StringComparer.OrdinalIgnoreCase);
    Check(found[0].Path==savedAccount && paths.Count==found.Count && paths.Contains(savedAccount),"合并已保存和自动来源，按账号目录去重");
    Check(paths.Contains(docOld)&&paths.Contains(docNew)&&paths.Contains(docWork1)&&paths.Contains(docWork2)&&
      !found.Any(a=>a.Id=="尚未登录" || a.Id=="empty_lookalike"),"重定向文档、新旧微信并存与企业微信多账号；拒绝缺少结构的目录");
    Check(paths.Contains(installed)&&paths.Contains(upgraded),"使用注册表路径线索发现自定义目录，支持升级后的目录结构");
    Check(paths.Contains(fromConfig),"新版 Unicode 路径配置可定位文档之外的账号");
    Check(paths.Contains(fromPortable),"企业微信绿色版未注册时可按常见便携目录发现");
    Check(paths.Contains(fromShortcut),"不运行快捷方式目标，读取绿色版相对配置发现自定义路径");
    Check(SourceDiscovery.Find(new SourceEntry[0],new DiscoveryLocations(),CancellationToken.None).Count==0,
     "没有有效线索时保持空列表，交给手动添加");
    using (var stopped=new CancellationTokenSource()) {
     stopped.Cancel(); Check(SourceDiscovery.Find(saved,input,stopped.Token).Count==0,"已取消的启动查找不继续读取目录");
    }
   }
   static string MakeAccount(string root,string relative,params string[] markers) {
    string path=Path.Combine(root,relative);
    foreach (var marker in markers) Directory.CreateDirectory(Path.Combine(path,marker));
    return path;
   }
   static void SaveFixtureShortcut(string path,string exe,string working) {
    object host=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell",true)), shortcut=null;
    try {
     shortcut=host.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,host,new object[]{path});
     var t=shortcut.GetType();
     t.InvokeMember("TargetPath",BindingFlags.SetProperty,null,shortcut,new object[]{exe});
     t.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,shortcut,new object[]{working});
     t.InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);
    } finally { if (shortcut!=null) Marshal.ReleaseComObject(shortcut); Marshal.ReleaseComObject(host); }
   }
   static byte[] Varint(long n) {
    var b=new List<byte>(); while (n>=128) { b.Add((byte)(n|128)); n>>=7; } b.Add((byte)n); return b.ToArray();
   }
   static byte[] Field(int key, byte[] value) { return Varint(key*8+2).Concat(Varint(value.Length)).Concat(value).ToArray(); }
   void VerifyIdentity(Account legacy,string newRoot,string workRoot) {
    var config=Path.Combine(legacy.Path,"config"); Directory.CreateDirectory(config);
    var oldName=Field(3,new byte[]{8,10}.Concat(Field(2,Encoding.UTF8.GetBytes("旧版测试昵称"))).ToArray());
    File.WriteAllBytes(Path.Combine(config,"AccInfo.dat"),oldName);
    Check(NameResolver.Resolve(legacy).Nickname=="旧版测试昵称","旧版微信本地昵称解析");
    var client=Path.Combine(root,"local-client");
    var store=Path.Combine(client,@"web\profiles\profile_abcd000000000000\Local Storage\leveldb"); Directory.CreateDirectory(store);
    var record=Encoding.ASCII.GetBytes("Nickname").Concat(Encoding.Unicode.GetBytes("|测试昵称\0")).Concat(new byte[]{0xff})
     .Concat(Encoding.ASCII.GetBytes("wxid_standalone123")).Concat(new byte[]{0}).ToArray();
    File.WriteAllBytes(Path.Combine(store,"000001.log"),record);
    var owner=new Account { Id="wxid_standalone123_abcd", Path=Path.Combine(newRoot,"wxid_standalone123_abcd"), Kind=SourceKind.New };
    Check(NameResolver.Resolve(owner,client).Nickname=="测试昵称","新版缓存完整账号匹配");
    owner.Id="wxid_standalone";
    Check(NameResolver.Resolve(owner,client).Nickname==null,"新版缓存拒绝账号前缀误匹配");
    owner.Id="wxid_4";
    Check(NameResolver.Resolve(owner,client).Nickname==null,"新版缓存拒绝过短的目录标识");
    for(int i=0;i<2;i++) {
     long id=820112+i;
     string member=Path.Combine(workRoot,(12345000+i).ToString());
     File.WriteAllText(Path.Combine(member,"Config.cfg"),"{\"LoginCompanyId\":\""+id+"\"}");
     string organization="测试企业 "+(i+1);
     var profile=Path.Combine(workRoot,"Profiles",id.ToString()); Directory.CreateDirectory(profile);
     var corp=Field(1,Encoding.UTF8.GetBytes(organization)).Concat(Varint(43*8)).Concat(Varint(id)).ToArray();
     var user=Field(2,Encoding.UTF8.GetBytes("同名用户")).Concat(Varint(13*8)).Concat(Varint(id)).ToArray();
     File.WriteAllText(Path.Combine(profile,"io_data.json"),"{\"corp_info\":\""+Convert.ToBase64String(corp)+"\",\"user_info\":\""+Convert.ToBase64String(user)+"\"}");
    }
    var profiles=Engine.Discover(workRoot,SourceKind.Work).Select(NameResolver.Resolve).ToList();
    Check(profiles.Count==2&&profiles.All(x=>x.Nickname=="同名用户")&&profiles[0].CorpName=="测试企业 1"&&profiles[1].CorpName=="测试企业 2","企业微信按公司 ID 区分同昵称账号");
   }
   void VerifyExport(string fixture) {
    string csv=Path.Combine(root,"文件清单.csv"), xlsx=Path.Combine(root,"文件清单.xlsx");
    var columns=new List<string>{"序号","文件名","大小(字节)","完整路径"};
    var rows=new List<object[]>{new object[]{1,"名称 & 中文.txt",3*MB,fixture}};
    TableExport.WriteCsv(csv,columns,rows); TableExport.WriteXlsx(xlsx,columns,rows);
    TableExport.WriteXlsx(xlsx,columns,rows); // overwrite confirmed by the save dialog
    byte[] head=File.ReadAllBytes(csv);
    Check(head[0]==239&&head[1]==187&&head[2]==191&&File.ReadAllText(csv).Contains(fixture),"CSV UTF-8 BOM、中文与完整路径");
    using (var z=ZipFile.OpenRead(xlsx)) using(var stream=z.GetEntry("xl/worksheets/sheet1.xml").Open()) {
     var xml=new XmlDocument(); xml.Load(stream);
     Check(z.Entries.Count==5 && xml.InnerText.Contains("名称 & 中文.txt") && xml.InnerText.Contains("3145728") && xml.InnerText.Contains(fixture),"XLSX 可解析、体积为数值、长路径完整");
    }
   }
   static string Hash(string file) { using (var s=File.OpenRead(file)) using (var sha=SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(s)); }
   void Restore(Entry file,string checksum) {
    if (!Engine.IsInside(root,file.Path)) throw new IOException("拒绝恢复非测试文件");
    string sid; using(var identity=WindowsIdentity.GetCurrent()) sid=identity.User.Value;
    string bin=Path.Combine(Path.GetPathRoot(file.Path),"$Recycle.Bin",sid);
    string match=Directory.EnumerateFiles(bin,"$I*").Single(p=>RecycleShell.MatchesMetadata(p,file.Path,file.Size));
    string data=Path.Combine(bin,"$R"+Path.GetFileName(match).Substring(2));
    if (Hash(data)!=checksum) throw new IOException("回收内容哈希不一致，保留现状");
    File.Move(data,file.Path); File.Delete(match); // test pair only; no other bin entries are changed
   }
  }
  internal static int Run() {
   string dir=Environment.GetEnvironmentVariable("WCC_TEST_DIR");
   if (String.IsNullOrEmpty(dir)) dir=Path.GetTempPath();
   string root=Path.Combine(Path.GetFullPath(dir),"test_"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(root);
   try { new Test(root).Run(); return 0; } catch (Exception e) { Console.Error.WriteLine(e); return 1; }
  }
 }
}
