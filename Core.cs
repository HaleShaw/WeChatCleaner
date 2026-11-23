using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WeChatCleaner {

 public enum SourceKind { Old, New, Work }

 /// How risky it is to delete the files of a category.
 public enum RiskLevel {
  Protected = 0,      // never touched by a cleanup
  Irreplaceable = 1,  // chat files, they cannot be downloaded again
  Recoverable = 2      // caches and temporary files, they come back on their own
 }

 public sealed class Account {
  public string Root, Path, Id;
  internal string DiscoveryRoot;
  public SourceKind Kind;
  public string Nickname, CorpName, NameSource;
  public bool HasName { get { return !String.IsNullOrEmpty(Nickname); } }
  public string DisplayName {
   get { return HasName ? Nickname : Id; }
  }
  public string Group { get { return Kind==SourceKind.Old?"个人微信 · 旧版":Kind==SourceKind.New?"个人微信 · 新版":"企业微信"; } }
  public string ShortGroup { get { return Kind==SourceKind.Old?"旧版微信":Kind==SourceKind.New?"新版微信":"企业微信"; } }
  public string SubLabel { get { return String.IsNullOrEmpty(CorpName) ? (HasName ? Id : "昵称未识别") : (HasName ? CorpName + " · " + Id : CorpName); } }
 }

 public sealed class Category : System.ComponentModel.INotifyPropertyChanged {
  // bound directly from the WPF views, so these are real properties (not fields)
  public string Name { get; set; }
  public string RelativePath { get; set; }
  public string Description { get; set; }
  public bool Protected { get; set; }
  public RiskLevel Risk { get; set; }
  bool selected;
  public bool Selected {
   get { return selected; }
   set { if (selected != value) { selected = value; Raise("Selected"); } }
  }
  public List<Entry> Files = new List<Entry>();
  public long Total { get { return Files.Sum(f=>f.Size); } }
  public long Matched { get; private set; }
  public int MatchedCount { get; private set; }
  public IEnumerable<Entry> Pending { get { return Protected ? Enumerable.Empty<Entry>() : Files.Where(f => f.Matches && !f.Excluded); } }
  public bool CanSelect { get { return !Protected; } }
  public string TotalText { get { return Engine.Size(Total); } }
  public string MatchedText { get { return Protected ? "—" : Engine.Size(Matched); } }
  public string MatchedCountText { get { return Protected ? "不参与清理" : MatchedCount + " 个文件"; } }
  public string RiskText {
   get {
    if (Protected) return "受保护";
    return Risk == RiskLevel.Recoverable ? "可重新生成" : "不可恢复";
   }
  }
  public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
  public void RefreshMatched() {
   Matched = 0; MatchedCount = 0;
   if (!Protected) foreach (var f in Files) if (f.Matches) { Matched += f.Size; MatchedCount++; }
   Raise("Total"); Raise("TotalText");
   Raise("Matched"); Raise("MatchedCount"); Raise("MatchedText"); Raise("MatchedCountText");
  }
  void Raise(string name) {
   var h = PropertyChanged;
   if (h != null) h(this, new System.ComponentModel.PropertyChangedEventArgs(name));
  }
 }

 public sealed class Entry {
  public string Path, Name, Type;
  public long Size;
  public DateTime Created, Modified;
  public bool Matches, Excluded;
  public Category Category;
  public string OwnerRoot;
  public SourceKind OwnerKind;
 }

 public sealed class ScanResult {
  public List<Category> Categories = new List<Category>();
  public List<string> Errors = new List<string>();
  public bool Cancelled;
  public long TotalBytes { get { return Categories.Sum(c=>c.Total); } }
  public int TotalCount { get { return Categories.Sum(c=>c.Files.Count); } }
  public long MatchedBytes { get { return Categories.Sum(c=>c.Matched); } }
  public int MatchedCount { get { return Categories.Sum(c=>c.MatchedCount); } }
  public List<Entry> Pending { get { return Categories.Where(c=>c.Selected).SelectMany(c=>c.Pending).ToList(); } }
 }

 public static class Engine {

  #region discovery
  public static List<Account> Discover(string root, SourceKind kind) {
   return Discover(root, kind, Int32.MaxValue, delegate { return false; });
  }
  internal static List<Account> Discover(string root, SourceKind kind, int maxChildren, Func<bool> stop) {
   root = NormalPath(root);
   var result = new List<Account>();
   if (!LinkFree(root) || !Directory.Exists(root)) return result;
   if (IsAccount(root, kind)) {
    var parent = Directory.GetParent(root);
    AddAccount(result, parent==null?root:parent.FullName, root, kind, root);
   } else {
    foreach (var d in Directory.EnumerateDirectories(root).Take(maxChildren)) {
     if (stop()) break;
     if (PlainDirectory(d) && IsAccount(d, kind)) AddAccount(result, root, d, kind, root);
    }
   }
   return result.OrderBy(x=>x.Id, StringComparer.OrdinalIgnoreCase).ToList();
  }
  static bool IsAccount(string p, SourceKind k) {
   string n = Path.GetFileName(p.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
   if (k==SourceKind.Old) return PlainDirectory(Path.Combine(p,"FileStorage"));
   if (k==SourceKind.New) return PlainDirectory(Path.Combine(p,"db_storage")) && PlainDirectory(Path.Combine(p,"msg"));
   return Regex.IsMatch(n, @"^\d+$") && PlainDirectory(Path.Combine(p,"Data"));
  }
  static bool PlainDirectory(string path) {
   try { return (File.GetAttributes(path) & (FileAttributes.Directory|FileAttributes.ReparsePoint)) == FileAttributes.Directory; }
   catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; } catch (System.Security.SecurityException) { return false; }
  }
  static void AddAccount(List<Account> list, string root, string p, SourceKind k,string discoveryRoot) {
   p = NormalPath(p);
   list.Add(new Account { Root=NormalPath(root), Path=p, Id=Path.GetFileName(p), Kind=k, DiscoveryRoot=discoveryRoot });
  }
  #endregion

  #region rules
  public static List<Category> Rules(SourceKind k) {
   var c = new List<Category>();
    Action<string,string,string,bool,RiskLevel> add = (n,p,d,pro,risk) =>
     c.Add(new Category{ Name=n, RelativePath=p, Description=d, Protected=pro, Risk=risk });
   if (k==SourceKind.Old) {
    add("接收的文件",@"FileStorage\File","聊天文件，删除后可能无法重新下载",false,RiskLevel.Irreplaceable);
    add("视频",@"FileStorage\Video","聊天视频",false,RiskLevel.Irreplaceable);
    add("图片与附件",@"FileStorage\MsgAttach","图片和其他附件",false,RiskLevel.Irreplaceable);
    add("缓存",@"FileStorage\Cache","微信缓存，需自行确认",false,RiskLevel.Recoverable);
    add("临时文件",@"FileStorage\Temp","临时文件，需自行确认",false,RiskLevel.Recoverable);
    add("手机临时文件",@"FileStorage\TempFromPhone","手机接收的临时文件",false,RiskLevel.Recoverable);
    add("图片缓存",@"FileStorage\Image","缩略图缓存，可重新下载",false,RiskLevel.Recoverable);
    add("收藏",@"FileStorage\Fav","收藏数据，受保护",true,RiskLevel.Protected);
    add("表情",@"FileStorage\CustomEmotion","表情数据，受保护",true,RiskLevel.Protected);
    add("聊天数据库",@"Msg","聊天记录，受保护",true,RiskLevel.Protected);
    add("备份",@"BackupFiles","备份，受保护",true,RiskLevel.Protected);
   } else if (k==SourceKind.New) {
    add("接收的文件",@"msg\file","聊天文件，删除后可能无法重新下载",false,RiskLevel.Irreplaceable);
    add("视频",@"msg\video","聊天视频",false,RiskLevel.Irreplaceable);
    add("图片与附件",@"msg\attach","图片和其他附件",false,RiskLevel.Irreplaceable);
    add("缓存",@"cache","客户端缓存，可重新生成",false,RiskLevel.Recoverable);
    add("临时文件",@"temp","临时文件，需自行确认",false,RiskLevel.Recoverable);
    add("聊天数据库",@"db_storage","聊天数据库，受保护",true,RiskLevel.Protected);
    add("迁移数据",@"msg\migrate","历史迁移数据，需自行确认",false,RiskLevel.Recoverable);
    add("备份",@"Backup","备份，受保护",true,RiskLevel.Protected);
   } else {
    add("聊天文件",@"Cache\File","本地聊天文件，删除后可能无法重新下载",false,RiskLevel.Irreplaceable);
    add("聊天图片",@"Cache\Image","本地聊天图片，删除后可能无法重新下载",false,RiskLevel.Irreplaceable);
    add("聊天视频",@"Cache\Video","本地聊天视频，删除后可能无法重新下载",false,RiskLevel.Irreplaceable);
    add("聊天语音",@"Cache\Voice","本地语音消息，清理前可试听或另存",false,RiskLevel.Irreplaceable);
    add("其他缓存数据",@"Cache","未识别用途的缓存，只统计、不清理",true,RiskLevel.Protected);
    add("企业微信缓存",@"WXWorkCefCache","可能影响本地显示，需确认",false,RiskLevel.Recoverable);
    add("临时文件",@"Temp","企业微信临时文件",false,RiskLevel.Recoverable);
    add("聊天数据",@"Data","聊天数据，受保护",true,RiskLevel.Protected);
    add("索引",@"Index","索引数据，受保护",true,RiskLevel.Protected);
    add("关联数据",@"CacheMapping","关联数据，受保护",true,RiskLevel.Protected);
    add("微盘数据",@"WeDrive","企业数据，受保护",true,RiskLevel.Protected);
    add("表情",@"Emotion","表情数据，受保护",true,RiskLevel.Protected);
    add("备份",@"Backup","备份，受保护",true,RiskLevel.Protected);
    add("其他账号文件",".","账号根文件与其他未分类目录，只统计、不清理",true,RiskLevel.Protected);
   }
   return c;
  }
  #endregion

  #region scanning
  public static ScanResult Scan(Account a, Func<bool> cancel, Action<int> progress) {
   var r = new ScanResult();
   r.Categories = Rules(a.Kind);
   if (!Directory.Exists(a.Path) || !LinkFree(a.Path)) { r.Errors.Add("账号目录不可访问：" + a.Path); return r; }
   int count = 0;
   var root = NormalPath(a.Path);
   if (a.Kind == SourceKind.Work) {
    // Walk the account once; a nested rule replaces the inherited category.
    var folders = r.Categories.ToDictionary(c=>NormalPath(Path.Combine(root,c.RelativePath)),StringComparer.OrdinalIgnoreCase);
    if (!ScanTree(a,root,folders[root],folders,r,cancel,progress,ref count)) r.Cancelled=true;
   } else {
    foreach (var category in r.Categories) {
     if (cancel()) { r.Cancelled=true; break; }
     string start=Path.Combine(root,category.RelativePath);
     if (!Directory.Exists(start)) continue;
     if (!LinkFree(start)) { r.Errors.Add("跳过链接或不可访问的目录："+start); continue; }
     if (!ScanTree(a,start,category,null,r,cancel,progress,ref count)) { r.Cancelled=true; break; }
    }
   }
   if (!r.Cancelled) progress(count);
   return r;
  }
  static bool ScanTree(Account a,string start,Category first,Dictionary<string,Category> folders,
     ScanResult result,Func<bool> cancel,Action<int> progress,ref int count) {
   var todo = new Stack<KeyValuePair<string,Category>>();
   todo.Push(new KeyValuePair<string,Category>(start,first));
   while (todo.Count>0) {
    if (cancel()) return false;
    var next=todo.Pop();
    string dir=next.Key;
    Category category;
    if (folders==null || !folders.TryGetValue(dir,out category)) category=next.Value;
    try {
     if ((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0) { result.Errors.Add("跳过链接目录："+dir); continue; }
     foreach (string p in Directory.EnumerateFiles(dir)) {
      if (cancel()) return false;
      try {
       var fi=new FileInfo(p);
       if ((fi.Attributes&FileAttributes.ReparsePoint)!=0) continue;
       category.Files.Add(new Entry { Path=fi.FullName, Name=fi.Name, Type=fi.Extension, Size=fi.Length,
        Created=fi.CreationTime, Modified=fi.LastWriteTime, Category=category, OwnerRoot=a.Path, OwnerKind=a.Kind });
       if (++count%400==0) progress(count);
      } catch (Exception ex) { result.Errors.Add(p+"："+ex.Message); }
     }
     foreach (string sub in Directory.EnumerateDirectories(dir)) todo.Push(new KeyValuePair<string,Category>(sub,category));
    } catch (Exception ex) { result.Errors.Add(dir+"："+ex.Message); }
   }
   return true;
  }
  public static void Filter(ScanResult r, int days, long minBytes, bool created) {
   var cutoff = DateTime.Now.AddDays(-Math.Max(0, Math.Min(36500, days)));
   foreach (var c in r.Categories) {
    foreach (var f in c.Files)
     f.Matches = !c.Protected && (days<=0 || (created ? f.Created : f.Modified) < cutoff) && f.Size >= minBytes;
    c.RefreshMatched();
   }
  }
  #endregion

  #region helpers
  public static string Size(long n) {
   string[] u = {"B","KB","MB","GB","TB"};
   double v = n; int i = 0;
   while (v >= 1024 && i < u.Length-1) { v /= 1024; i++; }
   return v.ToString("0.##")+" "+u[i];
  }
  public static string NormalPath(string path) {
   string full = Path.GetFullPath(path).Replace('/', '\\');
   return full.Length == Path.GetPathRoot(full).Length ? full : full.TrimEnd('\\');
  }
  public static bool IsInside(string parent, string path) {
   return NormalPath(path).StartsWith(NormalPath(parent).TrimEnd('\\')+"\\", StringComparison.OrdinalIgnoreCase);
  }
  internal static bool LinkFree(string path) {
   try {
    var ancestors = new Stack<string>();
    for (string cur = NormalPath(path); cur != null; cur = Path.GetDirectoryName(cur)) ancestors.Push(cur);
    foreach (string dir in ancestors)
     if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) return false;
    return true;
   } catch { return false; }
  }
  public static bool Revalidate(Entry f, string root, out string reason) {
   reason = "";
   try {
    if (!IsInside(root, f.Path)) { reason = "路径超出账号目录"; return false; }
    if (!LinkFree(f.Path)) { reason = "路径不存在、不可访问或包含链接"; return false; }
    var fi = new FileInfo(f.Path);
    if (!fi.Exists || fi.Length != f.Size || fi.LastWriteTimeUtc != f.Modified.ToUniversalTime()) { reason = "文件已变化或不存在"; return false; }
    return true;
   } catch (Exception ex) { reason = ex.Message; return false; }
  }
  internal static bool CleanupAllowed(Entry file, string root, out string reason) {
   try {
    if (file.Category == null || file.Category.Protected || !String.Equals(NormalPath(root), NormalPath(file.OwnerRoot), StringComparison.OrdinalIgnoreCase)) {
     reason = "文件受保护或不属于该账号"; return false;
    }
    var rules = CleanupRules[(int)file.OwnerKind];
    if (file.OwnerKind==SourceKind.Work) {
     // The same most specific directory wins here and during scanning.
     var rule=rules.FirstOrDefault(c=>IsInside(Path.Combine(root,c.RelativePath),file.Path));
     if (rule==null || rule.Protected) { reason="文件不在可清理分类内"; return false; }
    } else if (!rules.Any(c=>!c.Protected && IsInside(Path.Combine(root,c.RelativePath),file.Path)) ||
               rules.Any(c=>c.Protected && IsInside(Path.Combine(root,c.RelativePath),file.Path))) {
     reason="文件不在可清理分类内"; return false;
    }
    return Revalidate(file, root, out reason);
   } catch (Exception ex) { reason = ex.Message; return false; }
  }
  static readonly List<Category>[] CleanupRules = { Rules(SourceKind.Old), Rules(SourceKind.New),
   Rules(SourceKind.Work).OrderByDescending(c=>c.RelativePath.Length).ToList() };
  #endregion

  #region client detection
  static Dictionary<SourceKind,string> clientCache = new Dictionary<SourceKind,string>();
  static Dictionary<SourceKind,DateTime> clientCacheTime = new Dictionary<SourceKind,DateTime>();

  public static string ClientRunningInfo(SourceKind kind) {
   // enumerating every process is expensive, so the answer is cached for a few seconds
   DateTime stamp;
   string cached;
   lock (clientCache) {
    if (clientCache.TryGetValue(kind, out cached) && clientCacheTime.TryGetValue(kind, out stamp) && (DateTime.Now - stamp).TotalSeconds < 5)
     return cached;
   }
   string fresh = ClientRunningInfoUncached(kind);
   lock (clientCache) {
    clientCache[kind] = fresh;
    clientCacheTime[kind] = DateTime.Now;
   }
   return fresh;
  }

  static string ClientRunningInfoUncached(SourceKind kind) {
   string proc = kind==SourceKind.Old?"wechat":kind==SourceKind.New?"weixin":"wxwork";
   // QueryFullProcessImageName works with a client of different bitness.
   return String.Join("、",ClientProcesses.Images(proc).Select(p=>p.Length==0 ? proc : proc+" ("+p+")").Distinct());
  }
  public static bool ClientRunning(SourceKind kind) { return ClientRunningInfo(kind)!=""; }
  public static string ClientName(SourceKind kind) { return kind==SourceKind.Old?"旧版微信":kind==SourceKind.New?"新版微信":"企业微信"; }
  #endregion

  #region cleanup
  public sealed class RecycleReport {
   public int Recycled, Purged, Failed, Skipped, Unverified;
   public readonly List<string> Errors = new List<string>();
   public readonly List<Entry> Removed = new List<Entry>();
   public int Succeeded { get { return Recycled + Purged; } }
   public string Summary { get { return String.Format("成功 {0} · 跳过 {1} · 失败 {2} · 结果待核验 {3}", Succeeded, Skipped, Failed, Unverified); } }
   internal void Problem(Entry f, string why) { Errors.Add(f.Path + "：" + why); }
  }
  public static RecycleReport DeleteToRecycleBin(List<Entry> files, string accountRoot) { return DeleteFiles(files, accountRoot, false); }
  public static RecycleReport DeletePermanently(List<Entry> files, string accountRoot) { return DeleteFiles(files, accountRoot, true); }
  public static RecycleReport DeleteFiles(List<Entry> files, string accountRoot, bool permanent) {
   return DeleteFiles(files, permanent, accountRoot);
  }
  internal static RecycleReport DeleteFiles(IEnumerable<Entry> files, bool permanent, string expectedRoot) {
   var report = new RecycleReport();
   var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   var recycle = new RecycleShell();
   foreach (var f in files) {
    string reason;
    string root = expectedRoot ?? f.OwnerRoot;
    if (!seen.Add(f.Path)) { report.Skipped++; report.Problem(f, "重复选择"); continue; }
    if (!CleanupAllowed(f, root, out reason)) { report.Skipped++; report.Problem(f, reason); continue; }
    try {
     bool verified = permanent;
     if (permanent) File.Delete(f.Path);
     else verified = recycle.Move(f, root);
     if (!IsMissing(f.Path)) { report.Failed++; report.Problem(f, "原位置仍有文件，可能正被占用"); continue; }
     report.Removed.Add(f);
     if (permanent) report.Purged++;
     else if (verified) report.Recycled++;
     else { report.Unverified++; report.Problem(f, "原位置已无文件，回收站未能逐项核对"); }
    } catch (Exception ex) {
     if (IsMissing(f.Path)) {
      report.Removed.Add(f); report.Unverified++;
     } else report.Failed++;
     report.Problem(f, ex.Message);
    }
   }
   return report;
  }
  internal static bool IsMissing(string path) {
   try { File.GetAttributes(path); return false; }
   catch (FileNotFoundException) { return true; }
   catch (DirectoryNotFoundException) { return false; } // unreachable volume/ancestor is not proof of deletion
   catch { return false; }
  }
  #endregion
 }
}
