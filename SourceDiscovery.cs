using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace WeChatCleaner {
 // The Windows inputs and the directory matching are separated so regression tests can
 // exercise startup without seeing any real accounts, even with an empty test profile.
 internal sealed class DiscoveryLocations {
  internal string Documents, Roaming, Local;
  internal string[] InstallParents = new string[0];
  internal string[] ShortcutFolders = new string[0];
  internal Func<CancellationToken, IEnumerable<SourceEntry>> RegisteredPaths;
  internal Func<CancellationToken, IEnumerable<string>> ClientImages;

  internal static DiscoveryLocations Windows() {
   string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
   string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
   string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
   string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
   string app = AppDomain.CurrentDomain.BaseDirectory;
   return new DiscoveryLocations {
    Documents = documents, Roaming = roaming, Local = local,
    InstallParents = new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetEnvironmentVariable("ProgramW6432"),
     local, SourceDiscovery.Under(local,"Programs"), app, Path.GetDirectoryName(Engine.NormalPath(app)),
     documents, desktop, SourceDiscovery.Under(documents,"PortableApps"), SourceDiscovery.Under(desktop,"PortableApps") },
    ShortcutFolders = new[] { desktop, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
     Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
     SourceDiscovery.Under(roaming,@"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar") },
    RegisteredPaths = SourceDiscovery.ReadRegistry, ClientImages = SourceDiscovery.RegisteredOrRunningClients
   };
  }
 }

 internal static class SourceDiscovery {
  static readonly string[] RootNames = { "WeChat Files", "xwechat_files", "WXWork" };
  static readonly string[][] ClientNames = { new[]{"WeChat"}, new[]{"Weixin","xwechat"}, new[]{"WXWork","WeCom"} };
  static readonly string[] ExeNames = { "WeChat", "Weixin", "WXWork", "WeChatPortable", "WeixinPortable", "WXWorkPortable", "WeCom", "WeComPortable" };
  static readonly string[] DataKeys = { "FileSavePath", "FileStoragePath", "FileStorePath", "FilePath", "DataPath", "DataSavePath", "SavePath",
   "StoragePath", "StorageDir", "DataDir", "UserDataPath", "UserDataDir", "GlobalDataPath" };
  static readonly string[] InstallKeys = { "InstallPath", "InstallDir", "InstallLocation" };
  static readonly Regex NewPathIni = new Regex(@"^[0-9a-f]{32}\.ini$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
  static readonly string[] CommonIni = { "config.ini", "settings.ini", "global.ini", "path.ini", "UserSetting.ini" };
  const int MaxProbeDirectories = 768, MaxEntriesPerRoot = 512, MaxConfigBytes = 16384;

  internal static List<Account> Find(IEnumerable<SourceEntry> saved, DiscoveryLocations locations, CancellationToken token) {
   var entries=saved.ToArray();
   var ignored=entries.Where(s=>s.Excluded).Select(s=>new SourceEntry { Kind=s.Kind,Path=FullPath(s.Path,null,true) }).Where(s=>s.Path!=null).ToArray();
   var finder = new Finder(locations, token, ignored);
   foreach (var source in entries.Where(s=>!s.Excluded)) {
    if (token.IsCancellationRequested) break;
    finder.Check(source.Path, source.Kind, true);
   }
   if (locations != null && !token.IsCancellationRequested) {
    // Failure to read a discovery hint must not remove already verified sources.
    try { finder.Automatic(); }
    catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
    catch (ArgumentException) { } catch (NotSupportedException) { } catch (COMException) { }
   }
   return finder.Accounts;
  }

  static bool InSource(string root,string path) {
   return ProfileStorage.Same(root,path) || Engine.IsInside(root,path);
  }

  sealed class Finder {
   readonly DiscoveryLocations locations;
   readonly CancellationToken cancel;
   readonly SourceEntry[] ignored;
   readonly Stopwatch elapsed = new Stopwatch();
   readonly HashSet<string> checkedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   readonly HashSet<string> seenAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   readonly HashSet<string> inspectedClientDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   readonly HashSet<string> inspectedConfigs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   int probes;
   internal readonly List<Account> Accounts = new List<Account>();
   internal Finder(DiscoveryLocations locations, CancellationToken cancel,SourceEntry[] ignored) { this.locations=locations; this.cancel=cancel; this.ignored=ignored; }
   bool Done { get { return cancel.IsCancellationRequested || elapsed.ElapsedMilliseconds >= 9000 || probes >= MaxProbeDirectories; } }

   internal void Check(string value, SourceKind kind, bool saved) {
    if (cancel.IsCancellationRequested || (!saved && Done)) return;
    string path = FullPath(value, null, saved);
    if (path == null || !checkedRoots.Add((int)kind + "|" + path)) return;
    if (!saved) probes++;
    try {
     foreach (var account in Engine.Discover(path, kind, saved ? Int32.MaxValue : MaxEntriesPerRoot,
         delegate { return cancel.IsCancellationRequested || (!saved && elapsed.ElapsedMilliseconds >= 9000); })) {
      if (ignored.Any(s=>s.Kind==kind && InSource(s.Path,account.Path) && (!saved || !InSource(s.Path,path)))) continue;
      if (seenAccounts.Add((int)kind + "|" + account.Path)) Accounts.Add(account);
     }
    } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { } catch (ArgumentException) { }
   }

   void DataPath(string value, SourceKind kind, string relativeTo) {
    if (Done || String.IsNullOrWhiteSpace(value)) return;
    string text = value.Trim().Trim('"').Trim();
    string sentinel = text.TrimEnd(':');
    if (sentinel.Equals("MyDocument",StringComparison.OrdinalIgnoreCase) || sentinel.Equals("MyDocuments",StringComparison.OrdinalIgnoreCase)) {
     Check(Under(locations.Documents,RootNames[(int)kind]),kind,false); return;
    }
    string path = FullPath(text,relativeTo,false);
    if (path == null) return;
    // A vendor setting may name the data folder, an account, or the folder above the data folder.
    string canonical = Under(path, RootNames[(int)kind]);
    Check(canonical, kind, false);
    if (!String.Equals(Path.GetPathRoot(path), path, StringComparison.OrdinalIgnoreCase)) Check(path, kind, false);
   }

   internal void Automatic() {
    elapsed.Start();
    // Keep the most useful locations first. Probe only the three conventional
    // subfolders under Documents, even when it has been redirected to a different drive.
    for (int i=0; i<RootNames.Length; i++) Check(Under(locations.Documents,RootNames[i]),(SourceKind)i,false);
    if (locations.RegisteredPaths != null) {
     foreach (var hint in locations.RegisteredPaths(cancel)) {
      if (Done) break;
      DataPath(hint.Path, hint.Kind, null);
      // Some upgrades keep FileSavePath under the previous personal WeChat key.
      if (hint.Kind!=SourceKind.Work) DataPath(hint.Path, hint.Kind==SourceKind.Old ? SourceKind.New : SourceKind.Old, null);
     }
    }
    foreach (string area in new[]{locations.Roaming, locations.Local}) {
     foreach (var p in new[]{Under(area,"Tencent"),area}) {
      for (int i=0; i<ClientNames.Length && !Done; i++) {
       var kind = (SourceKind)i;
       Check(Under(p,RootNames[i]),kind,false);
       foreach (var name in ClientNames[i]) InspectClientDir(Under(p,name),kind);
      }
     }
    }
    // App Paths / Tencent install directories, including both registry views, plus active clients.
    if (locations.ClientImages != null) {
     foreach (string image in locations.ClientImages(cancel)) {
      if (Done) break;
      InspectImage(image,null);
     }
    }
    foreach (string parent in locations.InstallParents.Distinct(StringComparer.OrdinalIgnoreCase)) {
     if (Done) break;
     if (String.IsNullOrEmpty(parent)) continue;
     GuessExecutables(parent);
     foreach (var row in ClientNames) foreach (var name in row) {
      GuessExecutables(Under(parent,name)); GuessExecutables(Under(parent,"Tencent",name));
      GuessExecutables(Under(parent,name+"Portable"));
     }
    }
    foreach (string shortcut in Shortcuts(locations.ShortcutFolders)) {
     if (Done) break;
     string image, workDir;
     if (ReadShortcut(shortcut,out image,out workDir)) InspectImage(image,workDir);
    }
   }

   void GuessExecutables(string dir) {
    if (Done || !PlainLocalDirectory(dir)) return;
    foreach (string name in ExeNames) {
     if (Done) return;
     string path = Under(dir,name+".exe");
     if (File.Exists(path)) InspectImage(path,null);
    }
   }

   void InspectImage(string value, string workDir) {
    if (Done) return;
    string image = FullPath(value,null,false);
    if (image == null || !Engine.LinkFree(image) || !File.Exists(image)) return;
    string exe = Path.GetFileNameWithoutExtension(image);
    if (!ExeNames.Contains(exe,StringComparer.OrdinalIgnoreCase) || !Path.GetExtension(image).Equals(".exe",StringComparison.OrdinalIgnoreCase)) return;
    // Older portable launchers can keep the old executable name after updating.
    // The directory signatures, not the exe name, decide the personal WeChat version.
    var kinds = exe.StartsWith("WXWork",StringComparison.OrdinalIgnoreCase) || exe.StartsWith("WeCom",StringComparison.OrdinalIgnoreCase)
     ? new[]{ SourceKind.Work } : new[]{ SourceKind.Old, SourceKind.New };
    string dir = Path.GetDirectoryName(image);
    string parent = Path.GetDirectoryName(dir);
    string higher = parent == null ? null : Path.GetDirectoryName(parent);
    foreach (var kind in kinds) {
     foreach (var p in new[]{dir,parent,higher,FullPath(workDir,null,false)}) {
      InspectClientDir(p,kind);
      InspectClientDir(Under(p,"Data"),kind);
      InspectClientDir(Under(p,"UserData"),kind);
     }
    }
   }

   void InspectClientDir(string value, SourceKind kind) {
    if (Done) return;
    string dir = FullPath(value,null,false);
    if (dir == null || !PlainLocalDirectory(dir) || !inspectedClientDirs.Add((int)kind+"|"+dir)) return;
    DataPath(dir,kind,null);
    var configs = new[]{ dir, Under(dir,"config"), Under(dir,"settings"), Under(dir,"All Users","config") };
    foreach (string folder in configs) {
     if (Done || !PlainLocalDirectory(folder) || !inspectedConfigs.Add((int)kind+"|"+folder)) continue;
     foreach (string name in CommonIni.Concat(ClientNames[(int)kind].SelectMany(n=>new[]{n+".ini", n+"Portable.ini"})))
      LoadConfig(Path.Combine(folder,name),kind,dir);
     if (kind == SourceKind.Old) LoadConfig(Path.Combine(folder,"3ebffe94.ini"),kind,dir);
     if (kind == SourceKind.New) {
      foreach (string file in SafeEntries(folder,"*.ini",false,128)) {
       if (Done) break;
       if (NewPathIni.IsMatch(Path.GetFileName(file))) LoadConfig(file,kind,dir);
      }
     }
    }
   }

   void LoadConfig(string file, SourceKind kind, string clientDir) {
    if (Done || !File.Exists(file) || !Engine.LinkFree(file)) return;
    string text = ReadSmallConfig(file);
    if (text == null) return;
    var lines = text.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
    foreach (string raw in lines.Take(128)) {
     string line = raw.Trim().Trim('\0','\uFEFF').Trim();
     int eq = line.IndexOf('=');
     if (eq>0 && DataKeys.Contains(line.Substring(0,eq).Trim(),StringComparer.OrdinalIgnoreCase)) {
      // Relative hints are meaningful only for local portable layouts.
      DataPath(line.Substring(eq+1),kind,clientDir);
     } else if (lines.Length==1) {
      // Tencent also writes the bare data directory or the token MyDocument:.
      DataPath(line,kind,null);
     }
    }
   }

   IEnumerable<string> Shortcuts(IEnumerable<string> folders) {
    var pending = new Queue<KeyValuePair<string,int>>();
    foreach (var f in folders.Distinct(StringComparer.OrdinalIgnoreCase)) if (PlainLocalDirectory(f)) pending.Enqueue(new KeyValuePair<string,int>(f,0));
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    while (pending.Count>0 && visited.Count<56 && !Done) {
     var here = pending.Dequeue();
     if (!visited.Add(here.Key)) continue;
     foreach (string path in SafeEntries(here.Key,"*.lnk",false,180)) {
      if (Done) yield break;
      if (Path.GetExtension(path).Equals(".lnk",StringComparison.OrdinalIgnoreCase) &&
          ClientLabel(Path.GetFileNameWithoutExtension(path)) && Engine.LinkFree(path)) yield return path;
     }
     if (here.Value >= 2) continue;
     foreach (string p in SafeEntries(here.Key,"*",true,100)) {
      if (ClientLabel(Path.GetFileName(p)) && PlainLocalDirectory(p)) pending.Enqueue(new KeyValuePair<string,int>(p,here.Value+1));
     }
    }
   }
  }

  internal static string Under(string folder, params string[] children) {
   if (String.IsNullOrWhiteSpace(folder)) return null;
   return Path.Combine(new[]{folder}.Concat(children).ToArray());
  }
  static string FullPath(string text, string relativeTo, bool allowNetwork) {
   if (String.IsNullOrWhiteSpace(text)) return null;
   try {
    string value = Environment.ExpandEnvironmentVariables(text.Trim().Trim('"')).Replace('/','\\');
    if (value.IndexOfAny(new[]{'\0','"','|','*','?','<','>','\r','\n'})>=0 || value.StartsWith(@"\\.\",StringComparison.Ordinal)) return null;
    var root = Path.GetPathRoot(value);
    if (String.IsNullOrEmpty(root) && !String.IsNullOrEmpty(relativeTo)) { value=Path.Combine(relativeTo,value); root=Path.GetPathRoot(value); }
    bool network = root != null && root.StartsWith(@"\\",StringComparison.Ordinal);
    if (!(allowNetwork && network) && !(root!=null && root.Length==3 && Char.IsLetter(root[0]) && root[1]==':' && root[2]=='\\')) return null;
    if (!allowNetwork && new DriveInfo(root).DriveType == DriveType.Network) return null;
    return Engine.NormalPath(value);
   } catch (ArgumentException) { return null; } catch (NotSupportedException) { return null; } catch (IOException) { return null; } catch (System.Security.SecurityException) { return null; }
  }
  static bool PlainLocalDirectory(string value) {
   string path = FullPath(value,null,false);
   return path!=null && Engine.LinkFree(path) && Directory.Exists(path);
  }
  static bool ClientLabel(string name) {
   return name.IndexOf("微信",StringComparison.Ordinal)>=0 || new[]{"wechat","weixin","wxwork","wecom","tencent"}
    .Any(s=>name.IndexOf(s,StringComparison.OrdinalIgnoreCase)>=0);
  }
  static IEnumerable<string> SafeEntries(string dir, string pattern, bool directories, int limit) {
   var list = new List<string>();
   try {
    var entries = directories ? Directory.EnumerateDirectories(dir,pattern) : Directory.EnumerateFiles(dir,pattern);
    list.AddRange(entries.Take(limit));
   } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
   return list;
  }
  static string ReadSmallConfig(string file) {
   try {
    using (var stream = new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
     if (stream.Length<=0 || stream.Length>MaxConfigBytes) return null;
     byte[] b=new byte[(int)stream.Length];
     int off=0, n;
     while (off<b.Length && (n=stream.Read(b,off,b.Length-off))>0) off+=n;
     if (off>=2 && (b[0]==0xff && b[1]==0xfe)) return Encoding.Unicode.GetString(b,2,off-2);
     if (off>=2 && (b[0]==0xfe && b[1]==0xff)) return Encoding.BigEndianUnicode.GetString(b,2,off-2);
     if (off>=4 && b[1]==0 && b[3]==0) return Encoding.Unicode.GetString(b,0,off);
     if (off>=4 && b[0]==0 && b[2]==0) return Encoding.BigEndianUnicode.GetString(b,0,off);
     try { return new UTF8Encoding(false,true).GetString(b,0,off); }
     catch (DecoderFallbackException) { return Encoding.Default.GetString(b,0,off); }
    }
   } catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; } catch (System.Security.SecurityException) { return null; }
  }

  static RegistryView[] Views() {
   return Environment.Is64BitOperatingSystem ? new[]{RegistryView.Registry64,RegistryView.Registry32} : new[]{RegistryView.Default};
  }
  internal static IEnumerable<SourceEntry> ReadRegistry(CancellationToken token) {
   for (int i=0; i<ClientNames.Length; i++) foreach (var name in ClientNames[i]) {
    foreach (var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine}) foreach (var view in Views()) {
     if (token.IsCancellationRequested) yield break;
     foreach (var suffix in new[]{"",@"\Settings",@"\Global"}) {
      foreach (var path in RegistryStrings(hive,view,@"SOFTWARE\Tencent\"+name+suffix,DataKeys))
       yield return new SourceEntry { Kind=(SourceKind)i, Path=path };
     }
    }
   }
  }
  static List<string> RegistryStrings(RegistryHive hive, RegistryView view, string subkey, IEnumerable<string> keys) {
   var list = new List<string>();
   try {
    using (var root = RegistryKey.OpenBaseKey(hive,view))
    using (var key = root.OpenSubKey(subkey,false)) {
     if (key!=null) foreach (var name in keys) {
      var value = key.GetValue(name) as string;
      if (!String.IsNullOrWhiteSpace(value)) list.Add(value);
     }
    }
   } catch (IOException) { } catch (UnauthorizedAccessException) { } catch (System.Security.SecurityException) { }
   return list;
  }
  internal static IEnumerable<string> RegisteredOrRunningClients(CancellationToken token) {
   foreach (string name in ExeNames) {
    if (token.IsCancellationRequested) yield break;
    foreach (string path in ClientProcesses.Images(name)) yield return path;
    foreach (var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine}) foreach (var view in Views()) {
     if (token.IsCancellationRequested) yield break;
     foreach (string path in RegistryStrings(hive,view,@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\"+name+".exe",new string[]{null}))
      yield return path;
     var locations = RegistryStrings(hive,view,@"SOFTWARE\Tencent\"+name,InstallKeys);
     locations.AddRange(RegistryStrings(hive,view,@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\"+name,new[]{"InstallLocation"}));
     foreach (string dir in locations) {
      string path = FullPath(dir,null,false);
      if (path!=null) yield return Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase) ? path : Path.Combine(path,name+".exe");
     }
    }
   }
  }

  static bool ReadShortcut(string link, out string target, out string workDir) {
   target = workDir = null;
   object instance = null;
   try {
    instance = new ShellLink();
    ((IPersistFile)instance).Load(link,0); // STGM_READ; never Resolve or execute the shortcut
    var getter=(IShellLinkW)instance;
    var buffer=new StringBuilder(1024);
    getter.GetPath(buffer,buffer.Capacity,IntPtr.Zero,4); // SLGP_RAWPATH
    target=buffer.ToString();
    buffer.Clear(); getter.GetWorkingDirectory(buffer,buffer.Capacity); workDir=buffer.ToString();
    return target.Length>0;
   } catch (COMException) { return false; } catch (InvalidCastException) { return false; }
   finally { if (instance!=null) Marshal.ReleaseComObject(instance); }
  }
  [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
  class ShellLink { }
  [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IShellLinkW {
   void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,int capacity,IntPtr findData,uint flags);
   void GetIDList(out IntPtr id);
   void SetIDList(IntPtr id);
   void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder text,int capacity);
   void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
   void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir,int capacity);
  }
 }

 internal static class ClientProcesses {
  static string Image(int id) {
   var handle = OpenProcess(0x1000,false,id); // PROCESS_QUERY_LIMITED_INFORMATION, Vista+
   if (handle==IntPtr.Zero) return "";
   try {
    var path=new StringBuilder(32768); int chars=path.Capacity;
    return QueryFullProcessImageName(handle,0,path,ref chars) ? path.ToString() : "";
   } finally { CloseHandle(handle); }
  }
  internal static List<string> Images(string name) {
   var images = new List<string>();
   Process[] processes;
   try { processes = Process.GetProcessesByName(name); } catch { return images; }
   foreach (var p in processes) using (p) {
    try { images.Add(Image(p.Id)); } catch (InvalidOperationException) { }
   }
   return images;
  }
  [DllImport("kernel32.dll",SetLastError=true)]
  static extern IntPtr OpenProcess(uint flags,[MarshalAs(UnmanagedType.Bool)] bool inherit,int processId);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  static extern bool QueryFullProcessImageName(IntPtr process,uint flags,StringBuilder path,ref int chars);
  [DllImport("kernel32.dll")]
  [return: MarshalAs(UnmanagedType.Bool)]
  static extern bool CloseHandle(IntPtr handle);
 }
}
