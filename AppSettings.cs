using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WeChatCleaner {

 /// One configured data source: a kind plus the directory that holds the account folders.
 public sealed class SourceEntry {
  public SourceKind Kind;
  public string Path;
  public string Root { get { return Path; } }
 }

 public static class SourceStore {
  public static string Folder {
   get {
    string profile = Environment.GetEnvironmentVariable("WCC_PROFILE_DIR");
    return String.IsNullOrWhiteSpace(profile) ? System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WeChatCleaner") : System.IO.Path.GetFullPath(profile);
   }
  }
  public static string FilePath { get { return System.IO.Path.Combine(Folder, "sources.txt"); } }

  public static List<SourceEntry> Load() {
   var list = new List<SourceEntry>();
   if (!File.Exists(FilePath)) return list;
   string[] lines;
   try { lines = File.ReadAllLines(FilePath, Encoding.UTF8); } catch { return list; }
   foreach (string line in lines) {
    if (String.IsNullOrEmpty(line)) continue;
    string[] parts = line.Split('\t');
    if (parts.Length < 2) continue;
    int kind;
    if (!Int32.TryParse(parts[0].Trim(), out kind)) continue;
    if (kind < 0 || kind > 2) continue;
    string path;
    try { path = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Trim())); } catch { continue; }
    if (String.IsNullOrEmpty(path)) continue;
    list.Add(new SourceEntry{ Kind=(SourceKind)kind, Path=path });
   }
   return list;
  }
  public static bool Save(List<SourceEntry> list) {
   try {
    System.IO.Directory.CreateDirectory(Folder);
    var sb = new StringBuilder();
    foreach (var s in list) sb.Append((int)s.Kind).Append('\t').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(s.Path))).Append('\n');
    File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
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
  /// When true the cleanup removes the files for good instead of using the recycle bin.
  public bool PermanentDelete = false;
  public Dictionary<string,string> CustomNames = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);

  public static string FilePath { get { return System.IO.Path.Combine(SourceStore.Folder, "settings.txt"); } }
  /// When set, settings are read from and written to this file instead of the user profile.
  public string OverridePath;
  string TargetPath { get { return String.IsNullOrEmpty(OverridePath) ? FilePath : OverridePath; } }

  public static AppSettings Load() { return LoadFrom(null); }

  /// Loads settings from an explicit file (null = the default location).
  public static AppSettings LoadFrom(string path) {
   var s = new AppSettings{ OverridePath = path };
   if (!File.Exists(s.TargetPath)) return s;
   string[] lines;
   try { lines = File.ReadAllLines(s.TargetPath, Encoding.UTF8); } catch { return s; }
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

  public void Save() {
   try {
    string dir = System.IO.Path.GetDirectoryName(TargetPath);
    if (!String.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
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
    foreach (var kv in CustomNames)
     sb.Append("name:").Append(kv.Key).Append('=').Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(kv.Value??""))).Append('\n');
    File.WriteAllText(TargetPath, sb.ToString(), Encoding.UTF8);
   } catch {}
  }
 }
}
