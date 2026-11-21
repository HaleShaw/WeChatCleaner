using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WeChatCleaner {

 #region protobuf helper
 public sealed class ProtoField {
  public int Number; public int WireType; public ulong Varint; public byte[] Bytes;
  public long AsLong { get { return (long)Varint; } }
 }

 /// Minimal protobuf wire-format reader. Enough for the plain-text account descriptors
 /// that WeChat 3.x and WeCom 4.x leave on disk (no decryption involved).
 public static class Proto {
  public static List<ProtoField> Parse(byte[] data) {
   var list = new List<ProtoField>();
   if (data==null) return list;
   int i = 0;
   while (i < data.Length) {
    ulong tag;
    if (!ReadVarint(data, ref i, out tag)) break;
    int number = (int)(tag >> 3);
    int wire = (int)(tag & 7);
    if (number == 0) break;
    if (wire == 0) {
     ulong v;
     if (!ReadVarint(data, ref i, out v)) break;
     list.Add(new ProtoField{ Number=number, WireType=wire, Varint=v });
    } else if (wire == 2) {
     ulong len;
     if (!ReadVarint(data, ref i, out len)) break;
     if (len > (ulong)(data.Length - i)) break;
     var buf = new byte[len];
     Buffer.BlockCopy(data, i, buf, 0, (int)len);
     i += (int)len;
     list.Add(new ProtoField{ Number=number, WireType=wire, Bytes=buf });
    } else if (wire == 5) { if (i+4 > data.Length) break; i += 4; list.Add(new ProtoField{ Number=number, WireType=wire }); }
    else if (wire == 1) { if (i+8 > data.Length) break; i += 8; list.Add(new ProtoField{ Number=number, WireType=wire }); }
    else break;
   }
   return list;
  }
  static bool ReadVarint(byte[] b, ref int i, out ulong value) {
   ulong v = 0; int shift = 0;
   while (i < b.Length && shift < 64) {
    byte x = b[i++];
    v |= (ulong)(x & 0x7F) << shift;
    if ((x & 0x80)==0) { value = v; return true; }
    shift += 7;
   }
   value = 0;
   return false;
  }
  public static ProtoField Get(List<ProtoField> fields, int number) {
   foreach (var f in fields) if (f.Number==number) return f;
   return null;
  }
  public static string Utf8(byte[] b) {
   if (b==null) return null;
   try { return Encoding.UTF8.GetString(b); } catch { return null; }
  }
  /// True when the byte string decodes to plausible human readable text.
  public static bool IsPrintable(byte[] b) {
   if (b==null || b.Length==0) return false;
   try {
    var s = Encoding.UTF8.GetString(b);
    if (s.Length==0) return false;
    foreach (char c in s) if (c < 0x20 || c == 0x7F) return false;
    return true;
   } catch { return false; }
  }
 }
 #endregion

 public sealed class Identity {
  public string Nickname, CorpName, Avatar, Source;
  public bool Found { get { return !String.IsNullOrEmpty(Nickname) || !String.IsNullOrEmpty(CorpName); } }
 }

 /// Resolves the real display name of an account from files that the clients keep in plain text.
 /// Everything here is read-only and never touches the encrypted chat databases.
 public static class NameResolver {

  static readonly Encoding Utf8 = Encoding.UTF8;

  public static Identity Resolve(Account a) { return Resolve(a,null); }

  internal static Identity Resolve(Account a, string clientCache) {
   try {
    if (a.Kind==SourceKind.Old) return FromOldWeChat(a);
    if (a.Kind==SourceKind.Work) return FromWorkCom(a);
    return FromNewWeChat(a,clientCache);
   } catch { return new Identity{ Source="解析失败" }; }
  }

  #region WeChat 3.x
  /// WeChat 3.x keeps config\AccInfo.dat in plain text: field 2 is the wxid, field 3 holds
  /// nested records where {1:10, 2:<utf8>} is the nickname and {2:<url>} the avatar.
  static Identity FromOldWeChat(Account a) {
   var id = new Identity();
   string f = Path.Combine(a.Path, "config", "AccInfo.dat");
   if (!File.Exists(f)) { id.Source = "未找到 AccInfo.dat"; return id; }
   byte[] b = ReadShared(f);
   if (b==null) { id.Source = "无法读取 AccInfo.dat"; return id; }
   foreach (var top in Proto.Parse(b)) {
    if (top.Number != 3 || top.Bytes==null) continue;
    var inner = Proto.Parse(top.Bytes);
    var kind = Proto.Get(inner, 1);
    var val = Proto.Get(inner, 2);
    if (val==null || val.Bytes==null) continue;
    string text = Proto.Utf8(val.Bytes);
    if (text==null) continue;
    if (kind!=null && kind.Varint==10) {
     if (id.Nickname==null && text.Length>0 && text.Length<=48 && LooksLikeName(text)) {
      id.Nickname = text;
      id.Source = "config\\AccInfo.dat";
     }
    }
    if (text.StartsWith("http", StringComparison.OrdinalIgnoreCase) && text.Length>40) {
     if (id.Avatar==null) id.Avatar = text;
    }
   }
   if (id.Nickname==null) id.Source = "AccInfo.dat 中没有昵称字段";
   return id;
  }
  static bool LooksLikeName(string s) {
   if (s.Length==0) return false;
   foreach (char c in s) if (char.IsControl(c)) return false;
   bool hasLetter = false;
   foreach (char c in s) if (char.IsLetter(c)) { hasLetter = true; break; }
   return hasLetter;
  }
  #endregion

  #region WeCom 4.x
  /// WeCom keeps one profile folder per company under <WXWork>\Profiles. Each profile stores a
  /// base64 protobuf in io_data.json: user_info holds the member nickname (field 2) and the
  /// company id (field 13); corp_info holds the company name (field 1) and the company id (field 43).
  /// The account folder holds the same company id in Config.cfg as LoginCompanyId, which links
  /// the profile back to the account directory.
  sealed class ProfileInfo { public long CorpId; public string CorpName, Nickname; }
  static Dictionary<long,ProfileInfo> profileCache;
  static string profileCacheDir;
  static readonly object profileLock = new object();

  /// Reads every WeCom profile once and remembers it; the same folders serve every account.
  static Dictionary<long,ProfileInfo> LoadProfiles(string profilesDir) {
   lock (profileLock) {
    if (profileCache != null && profileCacheDir == profilesDir) return profileCache;
    var map = new Dictionary<long,ProfileInfo>();
    try {
     foreach (var dir in Directory.GetDirectories(profilesDir)) {
      string io = Path.Combine(dir, "io_data.json");
      if (!File.Exists(io) || !Engine.LinkFree(io)) continue;
      string json;
      try { json = File.ReadAllText(io, Encoding.UTF8); } catch { continue; }
      string corpB64 = JsonString(json, "corp_info");
      string userB64 = JsonString(json, "user_info");
      if (corpB64==null && userB64==null) continue;
      long profileCorp = 0;
      string corpName = null, nick = null;
      if (corpB64!=null) {
       byte[] raw = FromBase64(corpB64);
       if (raw!=null) {
        var cf = Proto.Parse(raw);
        string best = null;
        foreach (var f in cf) {
         if (f.Number != 1 || f.Bytes == null) continue;
         if (!Proto.IsPrintable(f.Bytes)) continue;
         string t = Proto.Utf8(f.Bytes);
         if (t == null || t.Length == 0 || t.Length > 60) continue;
         if (best == null || t.Length > best.Length) best = t;
        }
        if (best != null) corpName = best;
        var cid = Proto.Get(cf, 43);
        if (cid!=null) profileCorp = cid.AsLong;
       }
      }
      if (userB64!=null) {
       byte[] raw = FromBase64(userB64);
       if (raw!=null) {
        var uf = Proto.Parse(raw);
        foreach (var f in uf) {
         if (f.Number != 2 || f.Bytes == null) continue;
         if (!Proto.IsPrintable(f.Bytes)) continue;
         string t = Proto.Utf8(f.Bytes);
         if (t != null && t.Length > 0 && t.Length <= 48 && nick == null) nick = t;
        }
        var cid = Proto.Get(uf, 13);
        if (cid!=null && profileCorp==0) profileCorp = cid.AsLong;
       }
      }
      if (profileCorp==0) continue;
      if (!map.ContainsKey(profileCorp))
       map[profileCorp] = new ProfileInfo{ CorpId=profileCorp, CorpName=corpName, Nickname=nick };
     }
    } catch {}
    profileCache = map;
    profileCacheDir = profilesDir;
    return map;
   }
  }

  static Identity FromWorkCom(Account a) {
   var id = new Identity();
   long corpId = ReadCompanyId(Path.Combine(a.Path, "Config.cfg"));
   string parent = Path.GetDirectoryName(a.Path.TrimEnd('\\','/'));
   string profiles = String.IsNullOrEmpty(parent) ? null : Path.Combine(parent, "Profiles");
   if (profiles!=null && Directory.Exists(profiles) && Engine.LinkFree(profiles)) {
    var map = LoadProfiles(profiles);
    ProfileInfo hit = null;
    if (corpId!=0 && map.ContainsKey(corpId)) hit = map[corpId];
    if (hit==null && corpId==0) {
     foreach (var kv in map) {
      if (a.Id!=null && a.Id.EndsWith(kv.Key.ToString(), StringComparison.Ordinal)) { hit = kv.Value; break; }
     }
    }
    if (hit!=null) {
     if (hit.Nickname!=null) { id.Nickname = hit.Nickname; id.Source = "Profiles\\io_data.json"; }
     if (hit.CorpName!=null) { id.CorpName = hit.CorpName; id.Source = "Profiles\\io_data.json"; }
    }
   }
   if (id.Nickname==null || id.CorpName==null) ScanCefIdentity(a, id);
   if (id.Nickname==null && id.CorpName==null) {
    if (corpId!=0) id.Source = "企业 ID " + corpId + "（未找到资料缓存）";
    else id.Source = "未找到企业资料缓存";
   }
   return id;
  }

  /// Fallback: the CEF cache of the account folder keeps the signed-in account JSON.
  static void ScanCefIdentity(Account a, Identity id) {
   string cef = Path.Combine(a.Path, "WXWorkCefCache");
   if (!Directory.Exists(cef)) return;
   string first = Path.Combine(cef, "Cache", "Cache_Data", "data_1");
   var files = new List<string>();
   if (File.Exists(first)) files.Add(first);
   foreach (string f in EnumerateFiles(cef, 4*1024*1024)) if (f != first) files.Add(f);
   foreach (string f in files) {
    byte[] b = ReadShared(f);
    if (b==null) continue;
    string s = Utf8.GetString(b);
    string vid = JsonField(s, "vid");
    if (vid!=null && !String.Equals(vid, a.Id, StringComparison.OrdinalIgnoreCase)) continue;
    string nick = JsonField(s, "nick_name");
    string corp = JsonField(s, "corp_name");
    if (corp==null) corp = JsonField(s, "corpName");
    if (nick!=null && id.Nickname==null) { id.Nickname = Unescape(nick); id.Source = "WXWorkCefCache"; }
    if (corp!=null && id.CorpName==null) { id.CorpName = Unescape(corp); id.Source = "WXWorkCefCache"; }
    if (id.Nickname!=null && id.CorpName!=null) return;
   }
  }
  static long ReadCompanyId(string cfgPath) {
   if (!File.Exists(cfgPath)) return 0;
   byte[] b = ReadShared(cfgPath);
   if (b==null) return 0;
   string s = Utf8.GetString(b);
   var m = Regex.Match(s, "\"LoginCompanyId\"\\s*:\\s*\"(\\d+)\"");
   if (m.Success) { long v; if (long.TryParse(m.Groups[1].Value, out v)) return v; }
   return 0;
  }
  static string JsonField(string json, string key) {
   var m = Regex.Match(json, "\""+Regex.Escape(key)+"\"\\s*:\\s*\"([^\"\\\\]{0,160})\"");
   return m.Success ? m.Groups[1].Value : null;
  }
  /// Extracts a JSON string value without regex backtracking (the payload is ~300 KB).
  static string JsonString(string json, string key) {
   if (json == null) return null;
   string k = "\"" + key + "\"";
   int i = json.IndexOf(k, StringComparison.Ordinal);
   if (i < 0) return null;
   i = json.IndexOf(':', i + k.Length);
   if (i < 0) return null;
   i++;
   while (i < json.Length && (json[i]==' ' || json[i]=='\t' || json[i]=='\r' || json[i]=='\n')) i++;
   if (i >= json.Length || json[i] != '"') return null;
   i++;
   int start = i;
   while (i < json.Length && json[i] != '"') i++;
   if (i >= json.Length) return null;
   return json.Substring(start, i - start);
  }
  static byte[] FromBase64(string s) {
   try { return Convert.FromBase64String(s); } catch { return null; }
  }
  #endregion

  #region WeChat 4.x
  /// WeChat 4.x keeps its profile inside encrypted databases, but the web view runtime
  /// caches the signed-in account in plain text: the mini program local storage holds a
  /// record with "username" (the wxid) and "Nickname" side by side. We read that cache,
  /// match it against the account folder and report the name.
  static Identity FromNewWeChat(Account a, string clientCache) {
   var id = new Identity();
   string wxid = a.Id, tag = null;
   int cut = wxid.LastIndexOf('_');
   if (cut > 5 && wxid.Length-cut == 5 && Regex.IsMatch(wxid.Substring(cut+1),"^[0-9a-fA-F]{4}$")) {
    tag = wxid.Substring(cut+1); wxid = wxid.Substring(0,cut);
   }
   if (wxid.Length < 7 || !Regex.IsMatch(wxid,"^[a-zA-Z0-9_-]+$") || wxid.Equals("wxid",StringComparison.OrdinalIgnoreCase)) {
    id.Source = "无可核对的账号标识"; return id;
   }

   string client = clientCache ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Tencent", "xwechat", "radium");
   if (Directory.Exists(client)) {
    foreach (string level in LocalStorageDirs(client, tag)) {
     foreach (string f in EnumerateFiles(level, 96*1024*1024)) {
      byte[] b = ReadShared(f);
      if (b == null) continue;
      int i = 0;
      while (true) {
       i = IndexOfAscii(b, "Nickname", i);
       if (i < 0) break;
       string nick = Utf16TextAfter(b, i + 8, 300);
       if (nick != null && LooksLikeName(nick)) {
        // the very same record has to carry the wxid of this account
        int recordEnd = Math.Min(b.Length,i+512);
        int nextName = IndexOfAscii(b,"Nickname",i+8,recordEnd);
        if (nextName>=0) recordEnd=nextName;
        int wx = IndexOfExactAccount(b,wxid,i+8,recordEnd);
        int firstAccount = IndexOfAscii(b,"wxid_",i+8,recordEnd);
        if (wx>=0 && (firstAccount<0 || firstAccount==wx)) {
         id.Nickname = nick;
         id.Source = "微信客户端本地缓存";
         int av = IndexOfAscii(b,"myAvatar",i,recordEnd);
         if (av>=0) {
          string url = AsciiAfterPipe(b, av + 8, 500);
          if (url != null && url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) id.Avatar = url;
         }
         return id;
        }
       }
       i += 8;
      }
     }
    }
   }
   if (id.Nickname == null) {
    string key = Path.Combine(a.Path, "..", "all_users", "login", wxid, "key_info.db");
    id.Source = File.Exists(Path.GetFullPath(key))
     ? "数据已加密，未能自动识别昵称"
     : "数据已加密，未找到账号密钥";
   }
   return id;
  }

  /// Local Storage folders of the web view profiles, the ones matching the account tag first.
  static List<string> LocalStorageDirs(string clientRoot, string tag) {
   var result = new List<string>();
   string profiles = Path.Combine(clientRoot, "web", "profiles");
   if (!Directory.Exists(profiles)) return result;
   var all = new List<string>();
   foreach (var d in Directory.GetDirectories(profiles)) {
    string ls = Path.Combine(d, "Local Storage", "leveldb");
    if (Directory.Exists(ls)) all.Add(ls);
   }
   if (!String.IsNullOrEmpty(tag)) {
    foreach (var d in all) {
     string name = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(d)));
     int u = name.LastIndexOf('_');
     string hash = u >= 0 ? name.Substring(u+1) : name;
     if (hash.StartsWith(tag, StringComparison.OrdinalIgnoreCase)) result.Add(d);
    }
   }
   foreach (var d in all) if (!result.Contains(d)) result.Add(d);
   return result;
  }

  /// A cached value is stored as "<code>|<text>", the text being UTF-16 little endian for
  /// Chinese names. The pipe marks the start of the text and also fixes its byte alignment,
  /// so the name can be read without guessing.
  static string Utf16TextAfter(byte[] data, int start, int window) {
   int end = Math.Min(data.Length, start + window);
   for (int i = start; i + 6 < end; i++) {
    if (data[i] != 0x7C || data[i+1] != 0x00) continue;
    var sb = new StringBuilder();
    for (int j = i + 2; j + 1 < end; j += 2) {
     int cu = data[j] | (data[j+1] << 8);
     if ((cu >= 0x4E00 && cu <= 0x9FFF) || (cu >= 0x20 && cu <= 0x7E)) sb.Append((char)cu);
     else break;
    }
    if (sb.Length > 0 && sb.Length <= 24) return sb.ToString();
   }
   // plain ASCII values are stored without the UTF-16 pipe
   for (int i = start; i + 2 < end; i++) {
    if (data[i] != 0x7C) continue;
    var sb = new StringBuilder();
    for (int j = i + 1; j < end; j++) {
     byte c = data[j];
     if (c >= 0x20 && c <= 0x7E) sb.Append((char)c); else break;
    }
    if (sb.Length > 0 && sb.Length <= 24) return sb.ToString();
   }
   return null;
  }

  /// Plain ASCII values are stored after a single pipe byte.
  static string AsciiAfterPipe(byte[] data, int start, int window) {
   int end = Math.Min(data.Length, start + window);
   for (int i = start; i < end; i++) {
    if (data[i] != 0x7C) continue;
    var sb = new StringBuilder();
    for (int j = i + 1; j < end; j++) {
     byte c = data[j];
     if (c >= 0x20 && c <= 0x7E) sb.Append((char)c); else break;
    }
    if (sb.Length > 8 && sb.Length <= 400) return sb.ToString();
   }
   return null;
  }

  static bool AccountChar(byte b) {
   return b>='a'&&b<='z' || b>='A'&&b<='Z' || b>='0'&&b<='9' || b=='_' || b=='-';
  }
  static int IndexOfExactAccount(byte[] data, string id, int start, int end) {
   int at=IndexOfAscii(data,id,start,end);
   while (at>=0) {
    int after=at+id.Length;
    if ((at==0 || !AccountChar(data[at-1])) && (after==data.Length || !AccountChar(data[after]))) return at;
    at=IndexOfAscii(data,id,at+1,end);
   }
   return -1;
  }
  static int IndexOfAscii(byte[] data, string text, int from, int limit=Int32.MaxValue) {
   byte[] pat = Encoding.ASCII.GetBytes(text);
   if (pat.Length == 0) return -1;
   for (int i = Math.Max(0, from); i + pat.Length <= Math.Min(data.Length,limit); i++) {
    bool ok = true;
    for (int k = 0; k < pat.Length; k++) if (data[i+k] != pat[k]) { ok = false; break; }
    if (ok) return i;
   }
   return -1;
  }

  #endregion

  #region shared helpers
  /// Reads a file even while the owning application keeps it open.
  public static byte[] ReadShared(string path) {
   try {
    if (!Engine.LinkFree(path)) return null;
    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite|FileShare.Delete))
    using (var ms = new MemoryStream()) {
     if (fs.Length>96*1024*1024) return null;
     fs.CopyTo(ms);
     return ms.ToArray();
    }
   } catch { return null; }
  }
  public static List<string> EnumerateFiles(string root, long maxSize) {
   var result = new List<string>();
   var stack = new Stack<string>();
   stack.Push(root);
   while (stack.Count>0) {
    string d = stack.Pop();
    if (!Engine.LinkFree(d)) continue;
    string[] files=null, dirs=null;
    try { files = Directory.GetFiles(d); dirs = Directory.GetDirectories(d); } catch { continue; }
    foreach (string f in files) { try { if (new FileInfo(f).Length<=maxSize) result.Add(f); } catch {} }
    if (dirs!=null) foreach (string s in dirs) stack.Push(s);
   }
   return result;
  }
  static string Unescape(string s) {
   if (s==null) return null;
   return s.Replace("\\\"","\"").Replace("\\\\","\\").Replace("\\/","/").Replace("\\u002F","/");
  }
  #endregion
 }
}
