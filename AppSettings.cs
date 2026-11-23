using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeChatCleaner {

 /// One configured data source: a kind plus the directory that holds the account folders.
 public sealed class SourceEntry {
  public SourceKind Kind;
  public string Path;
  internal bool Excluded;
  public string Root { get { return Path; } }
 }

 public static class SourceStore {
  public static string Folder { get { return ProfileStorage.App.Current.Path; } }
  public static string FilePath { get { return ProfileStorage.ReadPath(ProfileStorage.SourcesFile); } }

  public static List<SourceEntry> Load() {
   var list = new List<SourceEntry>();
   string file=FilePath;
   if (!File.Exists(file) || !Engine.LinkFree(file)) return list;
   string[] lines;
   try { lines = File.ReadAllLines(file, Encoding.UTF8); } catch { return list; }
   foreach (string line in lines) {
    if (String.IsNullOrEmpty(line)) continue;
    string[] parts = line.Split('\t');
    if (parts.Length < 2) continue;
    bool excluded=parts[0]=="exclude";
    int kindAt=excluded ? 1 : 0;
    int kind;
    if (parts.Length<=kindAt+1 || !Int32.TryParse(parts[kindAt].Trim(),out kind) || kind<0 || kind>2) continue;
    string path;
    try { path=Encoding.UTF8.GetString(Convert.FromBase64String(parts[kindAt+1].Trim())); } catch { continue; }
    try { if (!ProfileStorage.IsAbsolute(path)) continue; path=Engine.NormalPath(path); }
    catch (Exception ex) { if (!ProfileStorage.Expected(ex)) throw; continue; }
    list.Add(new SourceEntry { Kind=(SourceKind)kind,Path=path,Excluded=excluded });
   }
   return list;
  }
  public static bool Save(List<SourceEntry> list) {
   try {
    string file=ProfileStorage.WritePath(ProfileStorage.SourcesFile);
    var sb=new StringBuilder();
    foreach (var s in list) {
     if (s.Excluded) sb.Append("exclude\t");
     sb.Append((int)s.Kind).Append('\t').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Path))).Append('\n');
    }
    ProfileStorage.WriteText(file,sb.ToString());
    return true;
   } catch { return false; }
  }
 }

 /// Persistent application options plus the display names that could not be detected automatically.
 public sealed class AppSettings {
  public int Days = 30;
  public long MinBytes = 0;
  public bool UseCreated = false;
  public bool IgnoreJunctions = true;
  public bool RemindScan = false;
  public bool AutoStart = false;
  public int PageSize = 100;
  public string LastAccount;
  public string FfmpegPath;
  /// When true the cleanup removes the files for good instead of using the recycle bin.
  public bool PermanentDelete = false;
  public Dictionary<string,string> CustomNames = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

  public static string FilePath { get { return ProfileStorage.ReadPath(ProfileStorage.SettingsFile); } }
  /// When set, settings are read from and written to this file instead of the user profile.
  public string OverridePath;
  string TargetPath { get { return String.IsNullOrEmpty(OverridePath) ? FilePath : OverridePath; } }

  public static AppSettings Load() { return LoadFrom(null); }

  /// Loads settings from an explicit file (null = the default location).
  public static AppSettings LoadFrom(string path) {
   var s = new AppSettings{ OverridePath = path };
   string file=s.TargetPath;
   if (!File.Exists(file) || !Engine.LinkFree(file)) return s;
   string[] lines;
   try { lines = File.ReadAllLines(file, Encoding.UTF8); } catch { return s; }
   foreach (string line in lines) {
    if (String.IsNullOrEmpty(line)) continue;
    int i = line.IndexOf('=');
    if (i <= 0) continue;
    string k = line.Substring(0, i).Trim();
    string v = line.Substring(i+1);
    try {
     if (k=="Days") s.Days = Int32.Parse(v);
     else if (k=="MinBytes") s.MinBytes = Int64.Parse(v);
     else if (k=="UseCreated") s.UseCreated = v=="1";
     else if (k=="IgnoreJunctions") s.IgnoreJunctions = v=="1";
     else if (k=="RemindScan") s.RemindScan = v=="1";
     else if (k=="AutoStart") s.AutoStart = v=="1";
     else if (k=="PageSize") s.PageSize = Int32.Parse(v);
     else if (k=="LastAccount") s.LastAccount = Encoding.UTF8.GetString(Convert.FromBase64String(v));
     else if (k=="PermanentDelete") s.PermanentDelete = v=="1";
     else if (k=="FfmpegPath") s.FfmpegPath = Encoding.UTF8.GetString(Convert.FromBase64String(v));
     else if (k.StartsWith("name:")) {
      string key = k.Substring(5);
      byte[] raw = Convert.FromBase64String(v);
      s.CustomNames[key] = Encoding.UTF8.GetString(raw);
     }
    } catch {}
   }
   s.Days = Math.Max(0,Math.Min(36500,s.Days));
   s.PageSize = Math.Max(1,Math.Min(5000,s.PageSize));
   return s;
  }

  public bool Save() {
   try {
    string file=String.IsNullOrEmpty(OverridePath) ? ProfileStorage.WritePath(ProfileStorage.SettingsFile) : Engine.NormalPath(OverridePath);
    if (!String.IsNullOrEmpty(OverridePath)) Directory.CreateDirectory(Path.GetDirectoryName(file));
    var sb = new StringBuilder();
    sb.Append("Days=").Append(Days).Append('\n');
    sb.Append("MinBytes=").Append(MinBytes).Append('\n');
    sb.Append("UseCreated=").Append(UseCreated?"1":"0").Append('\n');
    sb.Append("IgnoreJunctions=").Append(IgnoreJunctions?"1":"0").Append('\n');
    sb.Append("RemindScan=").Append(RemindScan?"1":"0").Append('\n');
    sb.Append("AutoStart=").Append(AutoStart?"1":"0").Append('\n');
    sb.Append("PageSize=").Append(PageSize).Append('\n');
    if (!String.IsNullOrEmpty(LastAccount)) sb.Append("LastAccount=").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(LastAccount))).Append('\n');
    sb.Append("PermanentDelete=").Append(PermanentDelete?"1":"0").Append('\n');
    if (!String.IsNullOrEmpty(FfmpegPath)) sb.Append("FfmpegPath=").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(FfmpegPath))).Append('\n');
    foreach (var kv in CustomNames)
     sb.Append("name:").Append(kv.Key).Append('=').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Value??""))).Append('\n');
    ProfileStorage.WriteText(file,sb.ToString());
    return true;
   } catch { return false; }
  }
 }
}
