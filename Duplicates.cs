using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WeChatCleaner {

 public sealed class DuplicateGroup {
  public string Hash;
  public List<Entry> Files = new List<Entry>();
  public long Size { get { return Files.Count>0 ? Files[0].Size : 0; } }
  public long TotalBytes { get { return Size * Files.Count; } }
  public long Wasted { get { return Size * Math.Max(0,Files.Count-1); } }
 }

 public sealed class DuplicateResult {
  public List<DuplicateGroup> Groups = new List<DuplicateGroup>();
  public int Scanned, Candidates, Compared;
  public long MinSize;
  public bool Truncated;
  public List<string> Errors = new List<string>();
  public long WastedTotal { get { return Groups.Sum(g=>g.Wasted); } }
 }

 /// Three stage duplicate detection: group by size, then by a partial hash of the first and last
 /// 64 KB, then by the full SHA-256. Only files that survive every stage are hashed fully,
 /// which keeps large data directories fast.
 public static class DuplicateScanner {

  /// minSize skips tiny files: hashing them costs a lot of time and frees almost no space.
  public static DuplicateResult Scan(IEnumerable<Entry> files, Func<bool> cancel, Action<int> progress, long minSize) {
   var r = new DuplicateResult();
   r.MinSize = minSize;
   var bySize = new Dictionary<long, List<Entry>>();
   var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach (var f in files) {
    if (cancel()) return r;
    if (f.Size <= 0 || f.Size < minSize || (f.Category!=null && f.Category.Protected) || !unique.Add(f.Path)) continue;
    r.Scanned++;
    if (r.Scanned % 500 == 0) progress(r.Scanned);
    List<Entry> list;
    if (!bySize.TryGetValue(f.Size, out list)) { list = new List<Entry>(); bySize[f.Size] = list; }
    list.Add(f);
   }
   var candidates = bySize.Where(kv => kv.Value.Count > 1).ToList();
   r.Candidates = candidates.Sum(kv => kv.Value.Count);
   if (candidates.Count == 0) return r;

   var partial = new Dictionary<string, List<Entry>>();
   foreach (var kv in candidates) {
    foreach (var f in kv.Value) {
     if (cancel()) return r;
     string key;
     try { VerifySnapshot(f); key = PartialHash(f.Path, f.Size); } catch (Exception ex) { r.Errors.Add(f.Path+"："+ex.Message); continue; }
     List<Entry> list;
     if (!partial.TryGetValue(key, out list)) { list = new List<Entry>(); partial[key] = list; }
     list.Add(f);
    }
   }
   var stillSame = partial.Where(kv => kv.Value.Count > 1).ToList();
   foreach (var kv in stillSame) {
    if (cancel()) return r;
    var full = new Dictionary<string, List<Entry>>();
    foreach (var f in kv.Value) {
     if (cancel()) return r;
     r.Compared++;
     string hash;
     try { VerifySnapshot(f); hash = FullHash(f.Path); VerifySnapshot(f); } catch (Exception ex) { r.Errors.Add(f.Path+"："+ex.Message); continue; }
     List<Entry> list;
     if (!full.TryGetValue(hash, out list)) { list = new List<Entry>(); full[hash] = list; }
     list.Add(f);
    }
    foreach (var pair in full) {
     if (pair.Value.Count < 2) continue;
     var group = new DuplicateGroup { Hash = pair.Key };
     group.Files.AddRange(pair.Value.OrderBy(x=>x.Path, StringComparer.OrdinalIgnoreCase));
     r.Groups.Add(group);
    }
   }
   r.Groups = r.Groups.OrderByDescending(g=>g.Wasted).ToList();
   progress(r.Scanned);
   return r;
  }

  static void VerifySnapshot(Entry file) {
   string reason;
   if (!Engine.Revalidate(file,file.OwnerRoot,out reason)) throw new IOException(reason);
  }

  static string PartialHash(string path, long size) {
   const int chunk = 64*1024;
   using (var sha = SHA256.Create())
   using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
    var buf = new byte[chunk];
    int read = fs.Read(buf, 0, chunk);
    var head = new byte[read];
    Buffer.BlockCopy(buf, 0, head, 0, read);
    var tail = new byte[0];
    if (size > chunk*2) {
     fs.Seek(-chunk, SeekOrigin.End);
     read = fs.Read(buf, 0, chunk);
     tail = new byte[read];
     Buffer.BlockCopy(buf, 0, tail, 0, read);
    }
    var all = new byte[head.Length + tail.Length];
    Buffer.BlockCopy(head, 0, all, 0, head.Length);
    Buffer.BlockCopy(tail, 0, all, head.Length, tail.Length);
    return BitConverter.ToString(sha.ComputeHash(all)).Replace("-","") + ":" + size;
   }
  }
  static string FullHash(string path) {
   using (var sha = SHA256.Create())
   using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
    return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-","");
   }
  }
 }
}
