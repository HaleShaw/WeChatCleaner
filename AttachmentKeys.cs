using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WeChatCleaner {
 internal enum ImageKeyProof { None, Aes, Pair }
 internal enum ImageKeyCheck { Pending, Verified, NoSample, Failed }

 // Windows DPAPI protects both values for the current user and account directory.
 internal sealed class ImageKeyRecord {
  internal string Slot, AccountRoot, AccountId, DisplayName, ProtectedText;
  internal ImageKeyProof Proof;
  internal ImageKeyCheck Check;
  internal DateTime CheckedUtc;
 }
 internal sealed class ImageKeyRow {
  internal Account Account;
  internal ImageKeyRecord Saved, Relocation;
  internal bool CanUnlock, HasTail;
  internal string Title { get { return Account!=null ? Account.DisplayName : Saved.DisplayName??Saved.AccountId??"旧版密钥记录"; } }
 }
 internal static class AttachmentKeys {
  const int MaxStoreBytes=256*1024;
  static readonly object Gate=new object();
  internal static string FilePath { get { return ProfileStorage.ReadPath(ProfileStorage.KeysFile); } }

  internal static bool TryParse(string aes,string xor,out AttachmentKey key,out string error,out bool aesError,bool aesOptional=false) {
   key=null;
   byte[] bytes=null;
   aesError=!(aesOptional && String.IsNullOrEmpty(aes)) && !TryAes(aes??"",out bytes);
   if (aesError) { error="AES 密钥应为 16 个可打印 ASCII 字符，或 32 位十六进制字符。"; return false; }
   string input=(xor??"").Trim();
   xor=WithoutHexPrefix(input);
   byte tail=0;
   if (input.Length!=0 && (xor.Length!=2 || !Byte.TryParse(xor,NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out tail))) {
    if (bytes!=null) Array.Clear(bytes,0,bytes.Length);
    error="XOR 值应为两位十六进制字符，例如 A6 或 0xA6。"; return false;
   }
   key=new AttachmentKey(bytes,xor.Length==0 ? null : (byte?)tail);
   error=null; return true;
  }

  static string WithoutHexPrefix(string text) {
   return text.StartsWith("0x",StringComparison.OrdinalIgnoreCase) ? text.Substring(2) : text;
  }
  static bool TryAes(string text,out byte[] key) {
   key=null;
   if (text.Length==16 && text.All(c=>c>=32 && c<=126)) { key=Encoding.ASCII.GetBytes(text); return true; }
   text=WithoutHexPrefix(text.Trim());
   if (text.Length!=32) return false;
   var bytes=new byte[16];
   for (int i=0;i<bytes.Length;i++)
    if (!Byte.TryParse(text.Substring(i*2,2),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out bytes[i])) return false;
   key=bytes; return true;
  }

  internal static AttachmentKey Load(string accountPath) {
   if (String.IsNullOrEmpty(accountPath)) return null;
   lock (Gate) {
    try {
     ImageKeyRecord record;
     return Read(FilePath).TryGetValue(PathSlot(accountPath),out record) ? Unlock(record) : null;
    } catch (Exception ex) { if (!Expected(ex)) throw; return null; }
   }
  }

  internal static AttachmentKey Unlock(ImageKeyRecord record) {
   byte[] plain=null;
   try {
    plain=ProtectedData.Unprotect(Convert.FromBase64String(record.ProtectedText),Entropy(record.Slot),DataProtectionScope.CurrentUser);
    if (plain.Length!=16 && plain.Length!=17) return null;
    var aes=new byte[16]; Buffer.BlockCopy(plain,0,aes,0,aes.Length);
    return new AttachmentKey(aes,plain.Length==17 ? (byte?)plain[16] : null);
   } catch (Exception ex) { if (!Expected(ex)) throw; return null; }
   finally { if (plain!=null) Array.Clear(plain,0,plain.Length); }
  }
  static bool CanUnlock(ImageKeyRecord record,out bool hasTail) {
   using (var key=Unlock(record)) {
    hasTail=key!=null && key.Tail.HasValue;
    return key!=null;
   }
  }

  internal static List<ImageKeyRow> Rows(IEnumerable<Account> accounts) {
   lock (Gate) {
    string path=FilePath;
    var records=Read(path);
    var current=accounts.Where(a=>a.Kind==SourceKind.New).Select(a=>new { Account=a,Slot=PathSlot(a.Path) }).ToList();
    var occupied=new HashSet<string>(current.Select(a=>a.Slot),StringComparer.Ordinal);
    var archived=records.Values.Where(r=>!occupied.Contains(r.Slot)).ToList();
    var rows=new List<ImageKeyRow>();
    bool changed=false;
    foreach (var item in current) {
     ImageKeyRecord record;
     var a=item.Account;
     var row=new ImageKeyRow { Account=a };
     if (records.TryGetValue(item.Slot,out record)) {
      row.Saved=record;
      row.CanUnlock=CanUnlock(record,out row.HasTail);
      string name=a.HasName ? a.Nickname : record.DisplayName??a.Id;
      if (!ProfileStorage.Same(record.AccountRoot,a.Path) || !ProfileStorage.Same(record.AccountId,a.Id) || name!=record.DisplayName) {
       record.AccountRoot=Engine.NormalPath(a.Path); record.AccountId=Path.GetFileName(record.AccountRoot); record.DisplayName=name; changed=true;
      }
     } else if (MoveId(a.Id) && current.Count(x=>ProfileStorage.Same(x.Account.Id,a.Id))==1) {
      // Do not guess from nicknames, strip suffixes, or choose among multiple historical copies.
      var matches=archived.Where(r=>ProfileStorage.Same(r.AccountId,a.Id)).Take(2).ToList();
      bool tail;
      if (matches.Count==1 && CanUnlock(matches[0],out tail)) row.Relocation=matches[0];
     }
     rows.Add(row);
    }
    if (changed) {
     try { Write(ProfileStorage.WritePath(ProfileStorage.KeysFile),records); }
     catch (Exception ex) { if (!Expected(ex)) throw; } // still show labels resolved in this session
    }
    foreach (var record in archived.OrderBy(r=>r.DisplayName??r.AccountId??r.Slot,StringComparer.CurrentCultureIgnoreCase)) {
     var row=new ImageKeyRow { Saved=record };
     row.CanUnlock=CanUnlock(record,out row.HasTail); rows.Add(row);
    }
    return rows;
   }
  }
  static bool MoveId(string id) {
   return id!=null && id.Length>5 && id.StartsWith("wxid_",StringComparison.OrdinalIgnoreCase) &&
    id.All(c=>c=='_' || c=='-' || c>='a' && c<='z' || c>='A' && c<='Z' || c>='0' && c<='9');
  }

  internal static bool Remember(string accountPath,AttachmentKey key,string displayName,ImageKeyProof proof,
   ImageKeyRecord related=null,bool keepSavedTail=false) {
   if (String.IsNullOrEmpty(accountPath) || key==null || key.Aes==null || key.Aes.Length!=16 ||
    (proof==ImageKeyProof.Pair && !key.Tail.HasValue)) return false;
   lock (Gate) {
    try {
     string file=ProfileStorage.WritePath(ProfileStorage.KeysFile), root=Engine.NormalPath(accountPath), slot=PathSlot(root), id=Path.GetFileName(root);
     var records=Read(file);
     ImageKeyRecord existing, prior=null;
     records.TryGetValue(slot,out existing);
     if (related!=null && (existing!=null || !MoveId(id) || !ProfileStorage.Same(id,related.AccountId) ||
      !records.TryGetValue(related.Slot,out prior) || prior.ProtectedText!=related.ProtectedText || proof==ImageKeyProof.None)) return false;
     if (proof==ImageKeyProof.None && existing!=null && existing.Proof!=ImageKeyProof.None) return false;
     string name=String.IsNullOrWhiteSpace(displayName) || ProfileStorage.Same(displayName,id) ?
      (existing==null ? (prior==null ? id : prior.DisplayName??id) : existing.DisplayName??id) : displayName;
     byte? tail=key.Tail;
     var check=proof==ImageKeyProof.None ? ImageKeyCheck.Pending : ImageKeyCheck.Verified;
     using (var previous=existing==null ? null : Unlock(existing)) {
      bool sameAes=previous!=null && previous.Aes.SequenceEqual(key.Aes);
      if (keepSavedTail && proof==ImageKeyProof.Aes) {
       tail=previous==null ? null : previous.Tail;
       if (sameAes && tail.HasValue && existing.Proof==ImageKeyProof.Pair) proof=ImageKeyProof.Pair;
      }
      if (keepSavedTail && sameAes && tail==previous.Tail && existing.Proof==proof && existing.Check==check) return true;
     }
     var plain=new byte[tail.HasValue ? 17 : 16];
     Buffer.BlockCopy(key.Aes,0,plain,0,16);
     if (tail.HasValue) plain[16]=tail.Value;
     try {
      records[slot]=new ImageKeyRecord { Slot=slot,AccountRoot=root,AccountId=id,DisplayName=name,Proof=proof,Check=check,
       CheckedUtc=proof==ImageKeyProof.None ? default(DateTime) : DateTime.UtcNow,
       ProtectedText=Convert.ToBase64String(ProtectedData.Protect(plain,Entropy(slot),DataProtectionScope.CurrentUser)) };
     } finally { Array.Clear(plain,0,plain.Length); }
     Write(file,records);
     return true;
    } catch (Exception ex) { if (!Expected(ex)) throw; return false; }
   }
  }

  internal static bool NoteCheck(ImageKeyRecord record,ImageKeyCheck check,ImageKeyProof proof=ImageKeyProof.None) {
   lock (Gate) {
    try {
     string path=ProfileStorage.WritePath(ProfileStorage.KeysFile);
     var records=Read(path);
     ImageKeyRecord actual;
     if (!records.TryGetValue(record.Slot,out actual) || actual.ProtectedText!=record.ProtectedText) return false;
     actual.Check=check; actual.CheckedUtc=DateTime.UtcNow;
     if (proof>actual.Proof) actual.Proof=proof;
     Write(path,records);
     return true;
    } catch (Exception ex) { if (!Expected(ex)) throw; return false; }
   }
  }

  internal static bool Remove(ImageKeyRecord record) {
   lock (Gate) {
    try {
     string path=ProfileStorage.WritePath(ProfileStorage.KeysFile);
     var records=Read(path);
     ImageKeyRecord actual;
     if (!records.TryGetValue(record.Slot,out actual) || actual.ProtectedText!=record.ProtectedText) return false;
     records.Remove(record.Slot);
     Write(path,records);
     return true;
    } catch (Exception ex) { if (!Expected(ex)) throw; return false; }
   }
  }

  static bool Expected(Exception ex) { return ProfileStorage.Expected(ex) || ex is FormatException || ex is CryptographicException; }
  internal static string PathSlot(string root) {
   using (var sha=SHA256.Create()) return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(Engine.NormalPath(root).ToUpperInvariant())));
  }
  internal static byte[] Entropy(string slot) { return Encoding.UTF8.GetBytes("WeChatCleaner/image-key/1/"+slot); }
  static string Encode(string value) { return Convert.ToBase64String(ProfileStorage.Utf8.GetBytes(value??"")); }
  static string Decode(string value) { return ProfileStorage.Utf8.GetString(Convert.FromBase64String(value)); }

  static Dictionary<string,ImageKeyRecord> Read(string path) {
   var records=new Dictionary<string,ImageKeyRecord>(StringComparer.Ordinal);
   if (!File.Exists(path)) return records;
   if (!Engine.LinkFree(path)) throw new IOException("图片密钥记录不可访问。");
   using (var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    if (stream.Length>MaxStoreBytes) throw new InvalidDataException("图片密钥记录超出大小限制。");
    using (var reader=new StreamReader(stream,Encoding.UTF8,true)) {
     string line;
     while ((line=reader.ReadLine())!=null) {
      if (String.IsNullOrWhiteSpace(line)) continue;
      var parts=line.Split('\t');
      ImageKeyRecord record;
      if (parts.Length==2 && parts[0].Length==44 && parts[1].Length>0) {
       record=new ImageKeyRecord { Slot=parts[0],ProtectedText=parts[1] };
      } else if (((parts.Length==6 && parts[0]=="v2") || (parts.Length==9 && parts[0]=="v3")) && parts[1].Length==44 && parts[2].Length>0) {
       record=new ImageKeyRecord { Slot=parts[1],ProtectedText=parts[2],AccountRoot=Decode(parts[3]),AccountId=Decode(parts[4]),DisplayName=Decode(parts[5]) };
       if (!ProfileStorage.IsAbsolute(record.AccountRoot) || PathSlot(record.AccountRoot)!=record.Slot ||
        !ProfileStorage.Same(Path.GetFileName(record.AccountRoot),record.AccountId))
        throw new InvalidDataException("图片密钥记录的账号路径无效。");
       if (parts.Length==9) {
        int proof,check; long ticks;
        if (!Int32.TryParse(parts[6],out proof) || !Enum.IsDefined(typeof(ImageKeyProof),proof) ||
         !Int32.TryParse(parts[7],out check) || !Enum.IsDefined(typeof(ImageKeyCheck),check) ||
         !Int64.TryParse(parts[8],out ticks) || ticks<0 || ticks>DateTime.MaxValue.Ticks)
         throw new InvalidDataException("图片密钥记录的验证状态无效。");
        record.Proof=(ImageKeyProof)proof; record.Check=(ImageKeyCheck)check; record.CheckedUtc=new DateTime(ticks,DateTimeKind.Utc);
       }
      } else throw new InvalidDataException("无法识别图片密钥记录格式，原文件已保留。");
      records[record.Slot]=record;
     }
    }
   }
   return records;
  }
  static void Write(string path,Dictionary<string,ImageKeyRecord> records) {
   var text=new StringBuilder();
   foreach (var r in records.Values.OrderBy(r=>r.Slot,StringComparer.Ordinal)) {
    if (r.AccountRoot==null) text.Append(r.Slot).Append('\t').Append(r.ProtectedText).Append('\n');
    else text.Append("v3\t").Append(r.Slot).Append('\t').Append(r.ProtectedText).Append('\t').Append(Encode(r.AccountRoot)).Append('\t')
     .Append(Encode(r.AccountId)).Append('\t').Append(Encode(r.DisplayName)).Append('\t')
     .Append((int)r.Proof).Append('\t').Append((int)r.Check).Append('\t').Append(r.CheckedUtc.Ticks).Append('\n');
   }
   string body=text.ToString();
   if (ProfileStorage.Utf8.GetByteCount(body)>MaxStoreBytes) throw new InvalidDataException("图片密钥记录超出大小限制。");
   ProfileStorage.WriteText(path,body);
  }
 }
}
