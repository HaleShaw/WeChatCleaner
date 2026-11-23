using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WeChatCleaner {
 internal sealed class AttachmentKey : IDisposable {
  internal readonly byte[] Aes;
  internal readonly byte? Tail;
  internal AttachmentKey(byte[] aes,byte? tail) { Aes=aes; Tail=tail; }
  public void Dispose() { if (Aes!=null) Array.Clear(Aes,0,Aes.Length); }
 }

 internal sealed class AttachmentKeyNeeded : IOException {
  internal readonly bool NeedsAes, IsV2, NeedsTail;
  internal AttachmentKeyNeeded(bool needsAes,string message,bool isV2,bool needsTail=false) : base(message) {
   NeedsAes=needsAes; IsV2=isV2; NeedsTail=needsTail;
  }
 }

 internal sealed class ImageKeySampleMissing : IOException {
  internal ImageKeySampleMissing() : base("暂无可验的 V2 样本。可从此账号的附件目录选择一张加密图片。") { }
 }

 internal sealed class AttachmentOutput {
  internal readonly string Extension;
  internal readonly byte[] Data;
  internal readonly string PreviewIssue;
  internal readonly ImageKeyProof KeyProof;
  internal AttachmentOutput(string extension,byte[] data,string issue=null,ImageKeyProof proof=ImageKeyProof.None) {
   Extension=extension; Data=data; PreviewIssue=issue; KeyProof=proof;
  }
 }

 internal static class AttachmentMedia {
  const int MaxBytes=64*1024*1024;
  const int ContainerHeader=15;
  const string Silk="silk";
  static readonly byte[] V1= { 7,8,0x56,0x31,8,7 };
  static readonly byte[] V2= { 7,8,0x56,0x32,8,7 };
  static readonly byte[] V1Key=Encoding.ASCII.GetBytes("cfcd208495d565ef");
  static readonly byte[] Jpeg={ 0xff,0xd8,0xff };
  static readonly byte[] Png={ 0x89,0x50,0x4e,0x47,0x0d,0x0a,0x1a,0x0a };
  static readonly byte[] Gif87=Encoding.ASCII.GetBytes("GIF87a"), Gif89=Encoding.ASCII.GetBytes("GIF89a");
  static readonly byte[] Riff=Encoding.ASCII.GetBytes("RIFF"), Webp=Encoding.ASCII.GetBytes("WEBP");
  static readonly byte[] SilkHeader=Encoding.ASCII.GetBytes("#!SILK_V3"), TencentSilk=Encoding.ASCII.GetBytes("\u0002#!SILK_V3");
  static readonly byte[] Amr=Encoding.ASCII.GetBytes("#!AMR\n"), AmrWb=Encoding.ASCII.GetBytes("#!AMR-WB\n");
  static readonly byte[] PngEnd={ 0,0,0,0,0x49,0x45,0x4e,0x44,0xae,0x42,0x60,0x82 };
  static readonly byte[] JpegEnd={ 0xff,0xd9 }, GifEnd={ 0,0x3b };

  internal static bool Candidate(Entry file) {
   if (String.IsNullOrEmpty(file.OwnerRoot)) return false;
   if (file.OwnerKind==SourceKind.Work) return In(file,@"Cache\Voice") || In(file,@"Cache\Image");
   if (file.OwnerKind==SourceKind.Old) return In(file,@"FileStorage\MsgAttach") || In(file,@"FileStorage\Image");
   return In(file,@"msg\attach");
  }
  static bool In(Entry file,string area) { return Engine.IsInside(Path.Combine(file.OwnerRoot,area),file.Path); }
  internal static void RequireSource(Entry file) {
   if (!File.Exists(file.Path)) throw new FileNotFoundException("文件已不存在，请刷新列表。");
   if (!Engine.LinkFree(file.Path) || (!String.IsNullOrEmpty(file.OwnerRoot) && !Engine.IsInside(file.OwnerRoot,file.Path)))
    throw new IOException("文件路径不在账号目录内，或包含目录链接。");
  }

  // Null means this is an ordinary file; the existing open/copy behavior applies.
  internal static AttachmentOutput Prepare(Entry entry,AttachmentKey key,string configuredFfmpeg=null) {
   RequireSource(entry);
   if (!Candidate(entry)) return null;
   string ffmpeg=ResolveFfmpeg(configuredFfmpeg);
   string type;
   byte[] data;
   bool wrapped=false,usedTail=false;
   ImageKeyProof proof=ImageKeyProof.None;
   using (var input=new FileStream(entry.Path,FileMode.Open,FileAccess.Read,FileShare.Read,4096,FileOptions.SequentialScan)) {
    var head=new byte[(int)Math.Min(input.Length,64)];
    ReadExactly(input,head);
    bool v1=Matches(head,0,V1), v2=Matches(head,0,V2);
    type=Format(head,0);
    int xor=type==null && !v1 && !v2 && entry.OwnerKind!=SourceKind.Work ? LegacyKey(head) : -1;
    if (v2 && (key==null || key.Aes==null))
     throw new AttachmentKeyNeeded(true,"这张加密图片需要当前账号的 AES 密钥。",true);
    if (v1 || v2 || xor>=0 || type==Silk || type==".wxgf" || (ffmpeg!=null && IsConvertibleAudio(type))) {
     input.Position=0;
     if (input.Length>MaxBytes) throw new IOException("此文件超过 64 MB，无法在程序内解码。可另存原文件。");
     data=new byte[(int)input.Length];
     ReadExactly(input,data);
     if (v1 || v2) { data=UnwrapAes(data,v1,key,out proof,out usedTail); wrapped=true; }
     else if (xor>=0) { for (int i=0;i<data.Length;i++) data[i]^=(byte)xor; wrapped=true; }
     type=Format(data,0);
    } else data=null;
   }
   string original=Path.GetExtension(entry.Path).ToLowerInvariant();
   if (type==null) {
    if (wrapped || original==".dat" || original==".silk" || original==".aud" || original==".wxgf" || original.Length==0)
     throw new InvalidDataException("没有识别到可解码的图片或语音格式。");
    return null;
   }
   if (proof!=ImageKeyProof.None && !IsImage(type)) throw new InvalidDataException("此 V2 文件未解析为受支持的图片格式。");
   if (type==Silk) return new AttachmentOutput(".mp3",ConvertSilk(data));
   if (type==".wxgf") {
    var png=ConvertWithFfmpeg(ffmpeg,data,type);
    return png!=null ? new AttachmentOutput(".png",png,proof:proof==ImageKeyProof.Aes && usedTail ? ImageKeyProof.Pair : proof) : new AttachmentOutput(type,data,
     "已解包为微信 WXGF 图片，但未能转换为 PNG。可另存为 WXGF，或到「清理设置」配置支持 HEVC 的 FFmpeg 后重试。",proof);
   }
   if ((ffmpeg!=null && IsConvertibleAudio(type))) {
    var mp3=ConvertWithFfmpeg(ffmpeg,data,type);
    if (mp3!=null) return new AttachmentOutput(".mp3",mp3);
   }
   if (!wrapped && (original==type || (type==".jpg" && original==".jpeg"))) return null;
   return new AttachmentOutput(type,wrapped ? data : null,proof:proof);
  }

  // Validation reads V2 samples from this account without writing previews or touching the originals.
  internal static ImageKeyProof VerifyImageKey(Account account,AttachmentKey key,string sample) {
   string attachments=Path.Combine(account.Path,@"msg\attach");
   if (account.Kind!=SourceKind.New || !Directory.Exists(account.Path) || !Engine.LinkFree(account.Path))
    throw new IOException("无法访问此账号的附件目录。请确认数据源已连接。");
   if (!Directory.Exists(attachments)) throw new ImageKeySampleMissing();
   if (!Engine.LinkFree(attachments)) throw new IOException("附件目录不可读取或包含链接。");
   if (!String.IsNullOrWhiteSpace(sample)) {
    if (!Engine.IsInside(attachments,sample)) throw new IOException("请选择此账号 msg\\attach 目录中的图片附件。");
    bool matched;
    var proof=VerifyV2(account,sample,key,MaxBytes,out matched);
    if (!matched) throw new InvalidDataException("请选择此账号 64 MB 以内的新版微信 V2 图片附件。");
    return proof;
   }
   var dirs=new Stack<string>(); dirs.Push(attachments);
   int entries=0, probed=0;
   var watch=Stopwatch.StartNew();
   ImageKeyProof best=ImageKeyProof.None;
   AttachmentKeyNeeded last=null;
   bool unreadable=false;
   while (dirs.Count!=0 && entries<4000 && probed<8 && watch.ElapsedMilliseconds<2500) {
    string dir=dirs.Pop();
    if (!Engine.LinkFree(dir)) continue;
    try {
     foreach (string path in Directory.EnumerateFileSystemEntries(dir)) {
      if (++entries>4000 || probed>=8 || watch.ElapsedMilliseconds>=2500) break;
      try {
       var attrs=File.GetAttributes(path);
       if ((attrs&FileAttributes.ReparsePoint)!=0) continue;
       if ((attrs&FileAttributes.Directory)!=0) { dirs.Push(path); continue; }
       bool matched=false;
       try {
        var proof=VerifyV2(account,path,key,8*1024*1024,out matched);
        if (proof==ImageKeyProof.Pair || (proof==ImageKeyProof.Aes && !key.Tail.HasValue)) return proof;
        if (proof>best) best=proof;
       } finally { if (matched) probed++; }
      } catch (AttachmentKeyNeeded ex) { if (last==null || !ex.NeedsAes) last=ex; }
      catch (InvalidDataException) { }
      catch (IOException) { unreadable=true; } catch (UnauthorizedAccessException) { unreadable=true; }
      catch (System.Security.SecurityException) { unreadable=true; }
     }
    } catch (IOException) { unreadable=true; } catch (UnauthorizedAccessException) { unreadable=true; }
    catch (System.Security.SecurityException) { unreadable=true; }
   }
   if (last!=null && !last.NeedsAes && key.Tail.HasValue) throw last;
   if (best!=ImageKeyProof.None) return best;
   if (last!=null) throw last;
   if (probed>0) throw new InvalidDataException("未能解码此账号的图片样本。可选择另一张 V2 图片重试。");
   if (unreadable) throw new IOException("部分附件无法读取。请检查权限，或手动选择一张可访问的样本。");
   throw new ImageKeySampleMissing();
  }

  static bool IsImage(string type) {
   return type==".jpg" || type==".png" || type==".gif" || type==".webp" || type==".wxgf" || type==".bmp";
  }
  static ImageKeyProof VerifyV2(Account account,string path,AttachmentKey key,int maxSize,out bool matched) {
   matched=false;
   RequireSource(new Entry { Path=path,OwnerRoot=account.Path,OwnerKind=account.Kind });
   using (var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) {
    if (input.Length<ContainerHeader+16 || input.Length>maxSize) return ImageKeyProof.None;
    var head=new byte[V2.Length]; ReadExactly(input,head);
    if (!Matches(head,0,V2)) return ImageKeyProof.None;
    matched=true;
    var bytes=new byte[(int)input.Length]; input.Position=0; ReadExactly(input,bytes);
    ImageKeyProof proof; bool usedTail;
    var image=UnwrapAes(bytes,false,key,out proof,out usedTail);
    if (IsImage(Format(image,0))) return proof;
    throw new InvalidDataException("此样本未解析为受支持的图片格式。");
   }
  }

  static byte[] UnwrapAes(byte[] file,bool v1,AttachmentKey provided,out ImageKeyProof proof,out bool usedTail) {
   proof=ImageKeyProof.None; usedTail=false;
   if (!v1 && (provided==null || provided.Aes==null))
    throw new AttachmentKeyNeeded(true,"这张加密图片需要当前账号的 AES 密钥。",true);
   if (file.Length<ContainerHeader+16) throw new InvalidDataException("微信附件头不完整。");
   long plainSize=ReadUInt32(file,6), tailSize=ReadUInt32(file,10);
   long available=file.Length-ContainerHeader-tailSize;
   long paddedSize=(plainSize/16+1)*16;
   if (plainSize<8 || plainSize>available || (paddedSize>available && plainSize%16!=0))
    throw new InvalidDataException("微信附件的分段长度无效。");
   int encrypted=(int)(paddedSize<=available ? paddedSize : plainSize);
   byte[] key=v1 ? V1Key : provided.Aes;
   byte[] prefix=DecryptPrefix(file,encrypted,key);
   string type=Format(prefix,0);
   if (type==null && v1 && provided!=null && provided.Aes!=null) {
    prefix=DecryptPrefix(file,encrypted,provided.Aes); type=Format(prefix,0);
   }
   if (type==null) throw new AttachmentKeyNeeded(true,"AES 密钥与图片不匹配，请核对账号和密钥。",!v1);
   if (!v1) proof=ImageKeyProof.Aes;
   // Current DAT files have a padded AES prefix; some block-aligned files have no padding.
   int plain=(int)plainSize, tail=(int)tailSize;
   int padding=encrypted-plain;
   bool pkcs=padding>0;
   for (int i=plain;i<encrypted && pkcs;i++) if (prefix[i]!=(byte)padding) pkcs=false;
   int rawStart=encrypted;
   if (!pkcs) {
    if (plain%16!=0) throw new InvalidDataException("微信附件的 AES 分段填充不正确。");
    rawStart=available-plain==16 ? encrypted : plain;
   }
   int raw=(int)available-rawStart;
   int tailStart=file.Length-tail;
   int clearSize=plain+raw;
   var decoded=new byte[clearSize+tail];
   Buffer.BlockCopy(prefix,0,decoded,0,plain);
   Buffer.BlockCopy(file,ContainerHeader+rawStart,decoded,plain,raw);
   byte? supplied=provided==null ? null : provided.Tail;
   bool structured=!v1 && (type==".jpg" || type==".png");
   if (structured && (tail==0 || !supplied.HasValue)) {
    int checkedThrough;
    int imageEnd=ImageEnd(decoded,clearSize,type,out checkedThrough);
    if (imageEnd>0) return TrimImage(decoded,imageEnd);
   }
   if (tail>0) {
    var end=type==".jpg" ? JpegEnd : type==".png" ? PngEnd : type==".gif" ? GifEnd : null;
    byte? xor=supplied;
    // For V2 JPEG and PNG, use the supplied XOR before looking at the container suffix.
    if (!(structured && supplied.HasValue) && end!=null && tail>=end.Length) {
     int mark=file.Length-end.Length;
     int guess=file[mark]^end[0];
     if (Matches(file,mark,end,guess)) {
      if (!v1 && supplied.HasValue && supplied.Value!=guess) throw BadTail(false);
      xor=(byte)guess;
     }
    }
    if (!xor.HasValue) throw MissingTail(v1);
    for (int i=0;i<tail;i++) decoded[clearSize+i]=(byte)(file[tailStart+i]^xor.Value);
    if (!v1) {
     if (structured) {
      int checkedThrough;
      int imageEnd=ImageEnd(decoded,decoded.Length,type,out checkedThrough);
      // Infer XOR from a marker only when that marker really ends the image.
      if (imageEnd==0 || (!supplied.HasValue && (imageEnd!=decoded.Length || checkedThrough!=imageEnd)))
       throw supplied.HasValue ? BadTail(true) : MissingTail(v1);
      usedTail=supplied.HasValue && checkedThrough>clearSize;
      if (usedTail) proof=ImageKeyProof.Pair;
      return TrimImage(decoded,imageEnd);
     }
     usedTail=supplied.HasValue;
     if (end!=null) {
      if (!Matches(decoded,decoded.Length-end.Length,end)) throw BadTail(true);
      if (usedTail) proof=ImageKeyProof.Pair;
     }
    }
   }
   return decoded;
  }

  static byte[] TrimImage(byte[] decoded,int imageEnd) {
   if (imageEnd!=decoded.Length) Array.Resize(ref decoded,imageEnd);
   return decoded;
  }
  static int ImageEnd(byte[] data,int limit,string type,out int checkedThrough) {
   if (type==".png") return checkedThrough=PngImageEnd(data,limit);
   // Multi-picture JPEGs can have consecutive images and a video after the last image.
   int end=JpegImageEnd(data,0,limit);
   while (end>0 && limit-end>=Jpeg.Length && Matches(data,end,Jpeg)) end=JpegImageEnd(data,end,limit);
   checkedThrough=end;
   if (end==0) return 0;
   // A motion photo can align its MP4 to the following bytes with a short zero gap.
   for (int offset=end; offset-end<=64 && limit-offset>=8; offset++) {
    if (TextAt(data,offset+4,"ftyp",0)) return VideoEnd(data,offset,limit,ref checkedThrough);
    if (data[offset]!=0) break;
   }
   return end;
  }
  static int JpegImageEnd(byte[] data,int start,int limit) {
   // Skip length-coded segments, including any embedded thumbnails.
   int offset=start+2;
   bool inScan=false, sawScan=false;
   while (offset<limit) {
    if (inScan) {
     offset=Array.IndexOf(data,(byte)0xff,offset,limit-offset);
     if (offset<0) return 0;
    } else if (data[offset]!=0xff) return 0;
    while (offset<limit && data[offset]==0xff) offset++;
    if (offset==limit) return 0;
    int marker=data[offset++];
    if (marker==0xd9) return sawScan ? offset : 0;
    // Stuffed FF bytes and restart markers do not end an entropy-coded scan.
    if (marker==0x01 || (inScan && (marker==0 || (marker>=0xd0 && marker<=0xd7)))) continue;
    if (marker<0xc0 || (marker>=0xd0 && marker<=0xd8) || limit-offset<2) return 0;
    int length=(data[offset]<<8)|data[offset+1];
    if (length<2 || length>limit-offset) return 0;
    offset+=length;
    inScan=marker==0xda;
    if (inScan) sawScan=true;
   }
   return 0;
  }
  static int PngImageEnd(byte[] data,int limit) {
   int offset=Png.Length;
   bool sawData=false;
   while (limit-offset>=12) {
    uint size=ReadBigEndian(data,offset);
    if (size>limit-offset-12) return 0;
    if (offset==Png.Length && (size!=13 || !TextAt(data,offset+4,"IHDR",0))) return 0;
    if (TextAt(data,offset+4,"IEND",0))
     return sawData && Matches(data,offset,PngEnd) ? offset+PngEnd.Length : 0;
    if (TextAt(data,offset+4,"IDAT",0)) sawData=true;
    offset+=12+(int)size;
   }
   return 0;
  }
  static int VideoEnd(byte[] data,int offset,int limit,ref int checkedThrough) {
   const uint Ftyp=0x66747970, Mdat=0x6d646174, Moov=0x6d6f6f76;
   bool hasMedia=false, hasIndex=false;
   while (limit-offset>=8) {
    uint kind=ReadBigEndian(data,offset+4);
    switch (kind) {
     case Ftyp: case Mdat: case Moov:
     case 0x66726565: case 0x736b6970: case 0x77696465: // free, skip, wide
     case 0x6d6f6f66: case 0x6d667261: case 0x73696478: // moof, mfra, sidx
     case 0x6d657461: case 0x75756964: // meta, uuid
      break;
     default: return hasMedia && hasIndex ? offset : 0;
    }
    int header=8;
    uint size=ReadBigEndian(data,offset);
    if (size==1) {
     header=16;
     if (limit-offset<header || ReadBigEndian(data,offset+8)!=0) return 0;
     size=ReadBigEndian(data,offset+12);
    }
    if (size<header || size>limit-offset) return 0;
    if (kind==Ftyp && (size<header+8 || size%4!=0)) return 0;
    if (kind==Mdat && size>header) hasMedia=true;
    if (kind==Moov && size>=header+8) hasIndex=true;
    // A length does not verify XOR if only the opaque box payload reaches the encrypted region.
    checkedThrough=offset+header;
    offset+=(int)size;
   }
   return hasMedia && hasIndex ? offset : 0;
  }
  static uint ReadBigEndian(byte[] data,int offset) {
   return ((uint)data[offset]<<24)|((uint)data[offset+1]<<16)|((uint)data[offset+2]<<8)|data[offset+3];
  }
  static AttachmentKeyNeeded MissingTail(bool v1) {
   return new AttachmentKeyNeeded(false,"这张图片还需要 XOR 值，请与 AES 密钥一起核对。",!v1,true);
  }

  static AttachmentKeyNeeded BadTail(bool required) {
   return new AttachmentKeyNeeded(false,"AES 已通过，但 XOR 值与图片不匹配，或图片尾部不完整。",true,required);
  }

  static byte[] DecryptPrefix(byte[] file,int length,byte[] key) {
   using (var aes=Aes.Create()) {
    aes.Mode=CipherMode.ECB; aes.Padding=PaddingMode.None; aes.Key=key;
    using (var decrypt=aes.CreateDecryptor()) return decrypt.TransformFinalBlock(file,ContainerHeader,length);
   }
  }
  static uint ReadUInt32(byte[] file,int i) { return (uint)file[i]|((uint)file[i+1]<<8)|((uint)file[i+2]<<16)|((uint)file[i+3]<<24); }
  static void ReadExactly(Stream input,byte[] buffer) {
   int read=0;
   while (read<buffer.Length) {
    int chunk=input.Read(buffer,read,buffer.Length-read);
    if (chunk==0) throw new EndOfStreamException("文件在读取时发生变化或已截断。");
    read+=chunk;
   }
  }
  static bool Matches(byte[] bytes,int offset,byte[] magic,int mask=0) {
   if (offset<0 || offset>bytes.Length-magic.Length) return false;
   for (int i=0;i<magic.Length;i++) if ((bytes[offset+i]^mask)!=magic[i]) return false;
   return true;
  }
  static bool TextAt(byte[] data,int offset,string value,int mask) {
   if (offset<0 || offset>data.Length-value.Length) return false;
   for (int i=0;i<value.Length;i++) if ((data[offset+i]^mask)!=value[i]) return false;
   return true;
  }
  static string Format(byte[] d,int mask) {
   if (Matches(d,0,Jpeg,mask)) return ".jpg";
   if (Matches(d,0,Png,mask)) return ".png";
   if (Matches(d,0,Gif87,mask) || Matches(d,0,Gif89,mask)) return ".gif";
   if (Matches(d,0,Riff,mask) && Matches(d,8,Webp,mask)) return ".webp";
   if (TextAt(d,0,"wxgf",mask)) return ".wxgf";
   if (Matches(d,0,SilkHeader,mask) || Matches(d,0,TencentSilk,mask)) return Silk;
   if (Matches(d,0,Amr,mask) || Matches(d,0,AmrWb,mask)) return ".amr";
   if (TextAt(d,0,"RIFF",mask) && TextAt(d,8,"WAVE",mask)) return ".wav";
   if (TextAt(d,0,"fLaC",mask)) return ".flac";
   if (TextAt(d,0,"OggS",mask) && (TextAt(d,28,"OpusHead",mask) || TextAt(d,29,"vorbis",mask))) return ".ogg";
   if (TextAt(d,4,"ftypM4A ",mask)) return ".m4a";
   if (TextAt(d,0,"ID3",mask) || (mask==0 && HasMp3Frame(d))) return ".mp3";
   if (TextAt(d,0,"BM",mask) && d.Length>=18 && (d[6]^mask)==0 && (d[7]^mask)==0 && (d[8]^mask)==0 && (d[9]^mask)==0) return ".bmp";
   return null;
  }
  static bool HasMp3Frame(byte[] d) {
   return d.Length>=4 && d[0]==255 && (d[1]&0xe6)==0xe2 && (d[1]&0x18)!=8 && (d[2]&0xf0)!=0 && (d[2]&0xf0)!=0xf0 && (d[2]&0x0c)!=0x0c;
  }
  static int LegacyKey(byte[] header) {
   if (header.Length==0) return -1;
   foreach (int first in new[]{0xff,0x89,'G','R','w','#',2,'I','B'}) {
    int mask=header[0]^first;
    if (mask!=0 && Format(header,mask)!=null) return mask;
   }
   return -1;
  }

  internal static string ResolveFfmpeg(string configured) {
   string path;
   string value=String.IsNullOrWhiteSpace(configured) ? Environment.GetEnvironmentVariable("WCC_FFMPEG") : configured;
   return TryFfmpegPath(value,out path) ? path : null;
  }
  internal static bool TryFfmpegPath(string value,out string path) {
   path=null;
   if (String.IsNullOrWhiteSpace(value)) return false;
   string candidate=value.Trim().Trim('"').Replace('/','\\');
   if (candidate.Length<4 || !((candidate[0]>='A' && candidate[0]<='Z') || (candidate[0]>='a' && candidate[0]<='z')) || candidate[1]!=':' || candidate[2]!='\\') return false;
   try {
    if (!Path.GetExtension(candidate).Equals(".exe",StringComparison.OrdinalIgnoreCase) ||
        !Path.GetFileName(candidate).StartsWith("ffmpeg",StringComparison.OrdinalIgnoreCase)) return false;
    string full=Engine.NormalPath(candidate);
    if (!File.Exists(full) || !Engine.LinkFree(full)) return false;
    path=full; return true;
   } catch (ArgumentException) { return false; } catch (NotSupportedException) { return false; } catch (IOException) { return false; } catch (System.Security.SecurityException) { return false; }
  }
  static bool IsConvertibleAudio(string type) { return type==".wav" || type==".amr" || type==".flac" || type==".ogg" || type==".m4a"; }
  static byte[] ConvertWithFfmpeg(string exe,byte[] data,string type) {
   if (exe==null) return null;
   bool picture=type==".wxgf";
   int offset=picture ? HevcStart(data) : 0;
   if (offset<0) return null;
   try {
    using (var scratch=new MediaScratch()) {
     string input=scratch.PathFor("input"+(picture ? ".hevc" : type));
     string output=scratch.PathFor(picture ? "output.png" : "output.mp3");
     using (var stream=new FileStream(input,FileMode.CreateNew,FileAccess.Write)) stream.Write(data,offset,data.Length-offset);
     string args="-nostdin -hide_banner -loglevel error -protocol_whitelist file -n " + (picture ? "-f hevc " : "") +
      "-i "+Quote(input)+(picture ? " -map 0:v:0 -frames:v 1 -c:v png " : " -map 0:a:0 -vn -c:a libmp3lame -q:a 4 ")+Quote(output);
     if (!Run(exe,args,scratch.Folder)) return null;
     return ReadConverted(output,picture ? ".png" : ".mp3");
    }
   } catch (IOException) { return null; } catch (System.ComponentModel.Win32Exception) { return null; }
  }
  static int HevcStart(byte[] bytes) {
   for (int i=4;i<bytes.Length-5;i++) {
    if (bytes[i]!=0 || bytes[i+1]!=0) continue;
    int code=bytes[i+2]==1 ? i+3 : bytes[i+2]==0 && bytes[i+3]==1 ? i+4 : -1;
    if (code>0 && (bytes[code]==0x40 || bytes[code]==0x42) && (bytes[code+1]&7)!=0) return i;
   }
   return -1;
  }
  static byte[] ConvertSilk(byte[] data) {
   using (var scratch=new MediaScratch()) {
    string exe=scratch.PathFor("silk-to-mp3.exe"), input=scratch.PathFor("input.silk"), output=scratch.PathFor("output.mp3");
    using (var resource=typeof(AttachmentMedia).Assembly.GetManifestResourceStream("WeChatCleaner.SilkToMp3.exe")) {
     if (resource==null) throw new IOException("此构建没有内置 SILK 转换器。");
     using (var disk=new FileStream(exe,FileMode.CreateNew,FileAccess.Write)) resource.CopyTo(disk);
    }
    using (var stream=new FileStream(input,FileMode.CreateNew,FileAccess.Write)) stream.Write(data,0,data.Length);
    if (!Run(exe,Quote(input)+" "+Quote(output),scratch.Folder))
     throw new InvalidDataException("SILK 语音无法转换为 MP3，文件可能不完整或格式不受支持。");
    return ReadConverted(output,".mp3");
   }
  }
  static bool Run(string exe,string args,string work) {
   var start=new ProcessStartInfo(exe,args) { UseShellExecute=false, CreateNoWindow=true, WorkingDirectory=work };
   start.EnvironmentVariables["TMP"]=work;
   start.EnvironmentVariables["TEMP"]=work;
   using (var process=Process.Start(start)) {
    if (process==null) return false;
    if (process.WaitForExit(30000)) return process.ExitCode==0;
    try { process.Kill(); process.WaitForExit(500); } catch (InvalidOperationException) { }
    return false;
   }
  }
  static string Quote(string path) { return "\""+path+"\""; }
  static byte[] ReadConverted(string file,string type) {
   var info=new FileInfo(file);
   if (!info.Exists || info.Length<16 || info.Length>MaxBytes) throw new IOException("转换器没有生成有效的媒体文件。");
   var bytes=File.ReadAllBytes(file);
   if (Format(bytes,0)!=type) throw new InvalidDataException("转换结果的格式不正确。");
   return bytes;
  }
 }

 // Only new files made by the current process are removed. Never recursively delete a user path.
 internal sealed class MediaScratch : IDisposable {
  internal readonly string Folder;
  readonly System.Collections.Generic.List<string> files=new System.Collections.Generic.List<string>();
  internal static string UserTempRoot() {
   // A launcher can override TEMP and TMP for the child process. Resolve the Windows user folder instead.
   string local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,Environment.SpecialFolderOption.DoNotVerify);
   if (String.IsNullOrEmpty(local) || !Engine.LinkFree(local)) throw new IOException("当前 Windows 用户的本地应用数据目录不可用。");
   return Path.Combine(local,"Temp","WeChatCleaner");
  }
  internal MediaScratch() {
   string root=UserTempRoot();
   var temp=Path.GetDirectoryName(root);
   Directory.CreateDirectory(temp);
   if (!Engine.LinkFree(temp)) throw new IOException("Windows 用户临时目录包含链接，无法安全创建预览文件。");
   Directory.CreateDirectory(root);
   if (!Engine.LinkFree(root)) throw new IOException("WeChat Cleaner 临时目录不可用。");
   Folder=Path.Combine(root,"wcc-media-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(Folder);
  }
  internal string PathFor(string name) { string path=Path.Combine(Folder,name); files.Add(path); return path; }
  public void Dispose() {
   if (!Engine.LinkFree(Folder)) return;
   foreach (var path in files) { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
   try { Directory.Delete(Folder); } catch (IOException) { } catch (UnauthorizedAccessException) { }
  }
 }
}
