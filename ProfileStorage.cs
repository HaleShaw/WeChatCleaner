using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace WeChatCleaner {
 internal enum ProfileMode { WindowsUser, Portable, Custom, Environment }

 internal sealed class ProfileLocation {
  internal ProfileMode Mode;
  internal string Path;
  internal string Problem;
  internal bool MustExist;
 }

 // The executable's location determines portable storage; the current working directory does not.
 internal sealed class ProfileStorage {
  internal static readonly Encoding Utf8 = new UTF8Encoding(false);
  internal static ProfileStorage App = new ProfileStorage(AppDomain.CurrentDomain.BaseDirectory,
   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WeChatCleaner"));
  internal const string SettingsFile="settings.txt", SourcesFile="sources.txt", KeysFile="image-keys.txt";
  static readonly string[] Files = { SettingsFile, SourcesFile, KeysFile };
  const string MarkerText="WeChatCleaner portable profile 1";
  readonly string userFolder, portableFolder;
  internal readonly string Locator, Marker;

  internal ProfileStorage(string programFolder,string userFolder) {
   this.userFolder=Engine.NormalPath(userFolder);
   string program=Engine.NormalPath(programFolder);
   portableFolder=Path.Combine(program,"config");
   Marker=Path.Combine(portableFolder,".wechat-cleaner-profile");
   string slot;
   using (var sha=SHA256.Create()) slot=BitConverter.ToString(sha.ComputeHash(Utf8.GetBytes(program.ToUpperInvariant()))).Replace("-","");
   Locator=Path.Combine(this.userFolder,"locations",slot+".txt");
  }

  internal ProfileLocation Current { get { return Resolve(System.Environment.GetEnvironmentVariable("WCC_PROFILE_DIR")); } }

  internal ProfileLocation Resolve(string external) {
   if (!String.IsNullOrWhiteSpace(external)) {
    // Preserve the existing environment override, including caller-relative values.
    try { return Location(ProfileMode.Environment,Engine.NormalPath(external),false); }
    catch (Exception ex) { if (!Expected(ex)) throw; return Unavailable(ProfileMode.Environment,external,"WCC_PROFILE_DIR 不是可用的目录路径。"); }
   }
   try {
    if (File.Exists(Locator)) {
     string[] route=ReadSmall(Locator).Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
     if (route.Length==1 && route[0]=="windows-user") return Location(ProfileMode.WindowsUser,userFolder,false);
     if (route.Length==1 && route[0]=="portable") return Location(ProfileMode.Portable,portableFolder,true);
     if (route.Length==2 && route[0]=="custom" && IsAbsolute(route[1])) return Location(ProfileMode.Custom,Engine.NormalPath(route[1]),true);
     return Unavailable(ProfileMode.Custom,null,"无法识别保存的配置位置，请重新选择目录。");
    }
    if (File.Exists(Marker) && ReadSmall(Marker).Trim()==MarkerText) return Location(ProfileMode.Portable,portableFolder,true);
   } catch (Exception ex) { if (!Expected(ex)) throw; return Unavailable(ProfileMode.Custom,null,"无法读取配置位置，请检查目录权限或重新选择。"); }
   return Location(ProfileMode.WindowsUser,userFolder,false);
  }

  internal ProfileLocation Propose(ProfileMode mode,string entered) {
   if (mode==ProfileMode.WindowsUser) return new ProfileLocation { Mode=mode,Path=userFolder };
   if (mode==ProfileMode.Portable) return new ProfileLocation { Mode=mode,Path=portableFolder };
   if (mode!=ProfileMode.Custom) return Unavailable(mode,null,"请选择配置目录。");
   string path=System.Environment.ExpandEnvironmentVariables((entered??"").Trim().Trim('"'));
   try {
    if (!IsAbsolute(path)) return Unavailable(mode,path,"请输入包含盘符或网络共享的完整目录路径。");
    return new ProfileLocation { Mode=mode,Path=Engine.NormalPath(path) };
   } catch (Exception ex) { if (!Expected(ex)) throw; return Unavailable(mode,path,"配置目录路径无效。"); }
  }

  static ProfileLocation Location(ProfileMode mode,string path,bool mustExist) {
   if ((mustExist && !Directory.Exists(path)) || File.Exists(path) || (Directory.Exists(path) && !Engine.LinkFree(path)))
    return Unavailable(mode,path,"配置目录不可用。请检查磁盘、目录权限或重新选择位置。");
   return new ProfileLocation { Mode=mode,Path=path,MustExist=mustExist };
  }
  static ProfileLocation Unavailable(ProfileMode mode,string path,string problem) {
   return new ProfileLocation { Mode=mode,Path=path,Problem=problem,MustExist=true };
  }
  internal static bool IsAbsolute(string path) {
   if (String.IsNullOrWhiteSpace(path)) return false;
   string root=Path.GetPathRoot(path.Replace('/','\\'));
   return !String.IsNullOrEmpty(root) && (root.StartsWith("\\\\",StringComparison.Ordinal) || root.Length==3 && root[1]==':' && root[2]=='\\');
  }
  static string ReadSmall(string file) {
   if (!Engine.LinkFree(file) || new FileInfo(file).Length>16384) throw new IOException("配置位置文件不可读。");
   return File.ReadAllText(file,Utf8);
  }
  internal static string ReadPath(string name) {
   var state=App.Current;
   return state.Problem==null ? Path.Combine(state.Path,name) : null;
  }
  internal static string WritePath(string name) {
   var state=App.Current;
   if (state.Problem!=null) throw new IOException(state.Problem);
   EnsureFolder(state.Path,!state.MustExist);
   return Path.Combine(state.Path,name);
  }
  internal static bool Same(string left,string right) { return String.Equals(left,right,StringComparison.OrdinalIgnoreCase); }
  internal static bool HasConfiguration(ProfileLocation location) {
   return location.Problem==null && Files.Any(name=>File.Exists(Path.Combine(location.Path,name)));
  }

  internal bool TrySwitch(ProfileLocation current,ProfileLocation target,bool copyCurrent,IEnumerable<string> dataFolders,out string error) {
   error=null;
   if (current.Mode==ProfileMode.Environment) { error="当前配置目录由 WCC_PROFILE_DIR 指定。请先取消该覆盖并重启。"; return false; }
   if (target.Problem!=null) { error=target.Problem; return false; }
   var changed=new List<KeyValuePair<string,byte[]>>();
   try {
    foreach (string data in dataFolders.Where(p=>!String.IsNullOrEmpty(p))) {
     if (Same(Engine.NormalPath(data),target.Path) || Engine.IsInside(data,target.Path))
      throw new IOException("请选择微信或企业微信数据目录之外的位置。");
    }
    EnsureFolder(target.Path,true);
    // Probe the destination even when selecting an existing profile without copying.
    using (new FileStream(Path.Combine(target.Path,".wcc-probe-"+Guid.NewGuid().ToString("N")),FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)) { }
    var originals=Files.ToDictionary(name=>name,name=>ReadProfileFile(target.Path,name));
    if (copyCurrent && !Same(current.Path,target.Path)) {
     if (current.Problem!=null) throw new IOException("当前配置目录不可访问，无法复制。可直接使用目标目录。");
     var incoming=Files.ToDictionary(name=>name,name=>ReadProfileFile(current.Path,name));
     if (originals.Values.Any(bytes=>bytes!=null)) {
      string backup=Path.Combine(target.Path,"配置备份-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));
      EnsureFolder(backup,true);
      foreach (var pair in originals) if (pair.Value!=null) WriteBytes(Path.Combine(backup,pair.Key),pair.Value);
     }
     foreach (string name in Files) {
      if (incoming[name]==null && originals[name]==null) continue;
      string file=Path.Combine(target.Path,name);
      WriteBytes(file,incoming[name]??new byte[0]);
      changed.Add(new KeyValuePair<string,byte[]>(file,originals[name]));
     }
    }
    if (target.Mode==ProfileMode.Portable) {
     byte[] before=File.Exists(Marker) ? Utf8.GetBytes(ReadSmall(Marker)) : null;
     WriteText(Marker,MarkerText);
     changed.Add(new KeyValuePair<string,byte[]>(Marker,before));
     // A fresh portable copy needs no Windows user locator.
     if (File.Exists(Locator)) Publish("portable");
    } else Publish(target.Mode==ProfileMode.WindowsUser ? "windows-user" : "custom\n"+target.Path);
    return true;
   } catch (Exception ex) {
    if (!Expected(ex)) throw;
    bool restored=true;
    for (int i=changed.Count-1;i>=0;i--) {
     try { if (changed[i].Value==null) File.Delete(changed[i].Key); else WriteBytes(changed[i].Key,changed[i].Value); }
     catch (Exception rollback) { if (!Expected(rollback)) throw; restored=false; }
    }
    error=restored ? ex.Message : "未能完成切换。原配置仍保留；目标目录的旧配置可从其中的配置备份恢复。";
    return false;
   }
  }
  void Publish(string route) {
   EnsureFolder(Path.GetDirectoryName(Locator),true);
   WriteText(Locator,route);
  }
  static byte[] ReadProfileFile(string folder,string name) {
   string path=Path.Combine(folder,name);
   if (Directory.Exists(path)) throw new IOException("目标目录中存在与配置同名的文件夹。");
   if (!File.Exists(path)) return null;
   if (!Engine.LinkFree(path) || new FileInfo(path).Length>4*1024*1024) throw new IOException("配置文件不可访问或超出大小限制："+name);
   return File.ReadAllBytes(path);
  }
  static void EnsureFolder(string path,bool allowCreate) {
   if (!Directory.Exists(path)) {
    if (!allowCreate) throw new DirectoryNotFoundException("配置目录不可用，请检查磁盘连接。");
    string existing=path;
    while (existing!=null && !Directory.Exists(existing)) existing=Path.GetDirectoryName(existing);
    if (existing==null || !Engine.LinkFree(existing)) throw new IOException("配置位置不可访问或包含目录链接。");
    Directory.CreateDirectory(path);
   }
   if (!Engine.LinkFree(path)) throw new IOException("配置位置不可访问或包含目录链接。");
  }
  internal static void WriteText(string path,string value) { WriteBytes(path,Utf8.GetBytes(value)); }
  static void WriteBytes(string path,byte[] value) {
   string parent=Path.GetDirectoryName(path);
   if (!Engine.LinkFree(parent) || (File.Exists(path) && !Engine.LinkFree(path))) throw new IOException("配置文件不可写入或包含目录链接。");
   string draft=Path.Combine(parent,".wcc-write-"+Guid.NewGuid().ToString("N")+".tmp");
   try {
    using (var stream=new FileStream(draft,FileMode.CreateNew,FileAccess.Write,FileShare.None)) { stream.Write(value,0,value.Length); stream.Flush(true); }
    // Same-directory rename also works for an exFAT/FAT portable volume.
    if (!MoveFileEx(draft,path,1|8)) throw new IOException(new Win32Exception(Marshal.GetLastWin32Error()).Message);
   } finally { try { File.Delete(draft); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
  }
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  static extern bool MoveFileEx(string current,string destination,int flags);
  internal static bool Expected(Exception ex) {
   return ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.Security.SecurityException;
  }
 }
}
