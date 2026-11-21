using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32;

namespace WeChatCleaner {
 // Windows 8+ uses recycle-only IFileOperation; Windows 7 uses an explicit
 // RecycleItem call. Neither path retries with a permanent delete.
 public sealed class RecycleShell {
  const uint RecycleFlags = 0x00080000 /* RECYCLEONDELETE */ | 0x0040 /* ALLOWUNDO */ |
    0x4000 /* WANTNUKEWARNING */ | 0x0010 /* NOCONFIRMATION */ | 0x0400 /* NOERRORUI */ | 0x0004 /* SILENT */ |
    0x100000 /* EARLYFAILURE */ | 0x2000 /* NOCONNECTED ELEMENTS */;
  const uint RecycleTransfer = 0x80;
  const int UserCancelled = unchecked((int)0x800704C7);
  static readonly Guid ShellItemId = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");

  internal bool Move(Entry file, string accountRoot) {
   return Environment.OSVersion.Version < new Version(6,2) ? MoveLegacy(file,accountRoot) : MoveModern(file,accountRoot);
  }

  bool MoveModern(Entry file, string accountRoot) {
   string bin = PersonalBin(file.Path);
   IShellItem source = null;
   IFileOperation operation = null;
   var sink = new Sink(file, accountRoot);
   try {
    var iid = ShellItemId;
    SHCreateItemFromParsingName(Engine.NormalPath(file.Path), IntPtr.Zero, ref iid, out source);
    operation = (IFileOperation)new FileOperation();
    operation.SetOperationFlags(RecycleFlags);
    operation.DeleteItem(source, sink);
    operation.PerformOperations();
    bool aborted;
    operation.GetAnyOperationsAborted(out aborted);
    if (sink.Rejection != null) throw new IOException(sink.Rejection);
    if (sink.Result < 0) Marshal.ThrowExceptionForHR(sink.Result);
    if (aborted) throw new IOException("Windows 未完成移入回收站操作");
    if (!sink.Recycled) return false;
    // The per-item success callback supplies a newly created recycle entry.
    // If it also exposes a physical path, validate the matching $I/$R pair.
    return sink.RecycledPath == null || MatchesRecord(bin, sink.RecycledPath, file);
   } finally {
    GC.KeepAlive(sink);
    if (operation != null) Marshal.ReleaseComObject(operation);
    if (source != null) Marshal.ReleaseComObject(source);
   }
  }

  internal bool MoveLegacy(Entry file, string accountRoot) {
   string bin = PersonalBin(file.Path);
   IShellItem source = null, parent = null, recycleBin = null, created = null;
   ITransferSource transfer = null;
   IntPtr transferPointer = IntPtr.Zero;
   try {
    var shellItem = ShellItemId;
    SHCreateItemFromParsingName(Engine.NormalPath(file.Path),IntPtr.Zero,ref shellItem,out source);
    source.GetParent(out parent);
    var handler = new Guid("D5E346A1-F753-4932-B403-4574800E2498"); // BHID_Transfer
    var iid = typeof(ITransferSource).GUID;
    parent.BindToHandler(IntPtr.Zero,ref handler,ref iid,out transferPointer);
    transfer = (ITransferSource)Marshal.GetObjectForIUnknown(transferPointer);
    var destination = new Guid("B7534046-3ECB-4C18-BE4E-64CD4CB7D6AC"); // FOLDERID_RecycleBinFolder
    SHGetKnownFolderItem(ref destination,0,IntPtr.Zero,ref shellItem,out recycleBin);
    string reason;
    if (!Engine.CleanupAllowed(file,accountRoot,out reason)) throw new IOException(reason);
    transfer.RecycleItem(source,recycleBin,0,out created); // TSF_NORMAL; there is no RemoveItem fallback.
    if (created == null) return false;
    var path = RecycledItemPath(created);
    return path == null || MatchesRecord(bin,path,file);
   } finally {
    if (created!=null) Marshal.ReleaseComObject(created);
    if (recycleBin!=null) Marshal.ReleaseComObject(recycleBin);
    if (transfer!=null) Marshal.ReleaseComObject(transfer);
    if (transferPointer!=IntPtr.Zero) Marshal.Release(transferPointer);
    if (parent!=null) Marshal.ReleaseComObject(parent);
    if (source!=null) Marshal.ReleaseComObject(source);
   }
  }

  static string RecycledItemPath(IShellItem recycled) {
   IntPtr text = IntPtr.Zero;
   try {
    recycled.GetDisplayName(0x80058000,out text); // SIGDN_FILESYSPATH
    string name=Marshal.PtrToStringUni(text);
    return name!=null && Path.GetFileName(name).StartsWith("$R",StringComparison.OrdinalIgnoreCase) ? name : null;
   } catch (COMException) { return null; }
   finally { if (text!=IntPtr.Zero) Marshal.FreeCoTaskMem(text); }
  }

  static int PolicyValue(RegistryKey hive, string path, string key) {
   using (var k = hive.OpenSubKey(path)) {
    object v = k == null ? null : k.GetValue(key);
    return v is int ? (int)v : 0;
   }
  }
  static bool MachinePolicy(string path,string key) {
   var views = Environment.Is64BitOperatingSystem ? new[]{ RegistryView.Registry64, RegistryView.Registry32 } : new[]{ RegistryView.Default };
   foreach (var view in views) using (var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))
    if (PolicyValue(hive,path,key)!=0) return true;
   return false;
  }
  static string PersonalBin(string file) {
   string root = Path.GetPathRoot(Engine.NormalPath(file));
   if (root.StartsWith("\\\\", StringComparison.Ordinal) ||
       new DriveInfo(root).DriveType != DriveType.Fixed)
    throw new IOException("该位置不支持安全移入回收站");
   const string config = @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket";
   const string policy = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";
   var volume = new StringBuilder(128);
   if (!GetVolumeNameForVolumeMountPoint(root, volume, volume.Capacity)) throw new IOException("无法识别回收站所在卷");
   string volumeId = volume.ToString().TrimEnd('\\').Substring(10);
   if (PolicyValue(Registry.CurrentUser, policy, "NoRecycleFiles") != 0 ||
       MachinePolicy(policy,"NoRecycleFiles") ||
       PolicyValue(Registry.CurrentUser, config, "NukeOnDelete") != 0 ||
       PolicyValue(Registry.CurrentUser, config + @"\Volume\" + volumeId, "NukeOnDelete") != 0)
    throw new IOException("该卷的回收站已在 Windows 中停用");
   string sid;
   using (var identity = WindowsIdentity.GetCurrent()) sid = identity.User.Value;
   string bin = Path.Combine(root, "$Recycle.Bin", sid);
   if (!Directory.Exists(bin) || !Engine.LinkFree(bin)) throw new IOException("无法核验该卷的个人回收站，已停止处理");
   return bin;
  }

  internal static bool MatchesRecord(string bin, string recycledPath, Entry file) {
   try {
    string r = Engine.NormalPath(recycledPath);
    string leaf = Path.GetFileName(r);
    if (!String.Equals(Path.GetDirectoryName(r), bin, StringComparison.OrdinalIgnoreCase) ||
        !leaf.StartsWith("$R", StringComparison.OrdinalIgnoreCase) || new FileInfo(r).Length != file.Size) return false;
    return MatchesMetadata(Path.Combine(bin, "$I" + leaf.Substring(2)), file.Path, file.Size);
   } catch { return false; }
  }
  internal static bool MatchesMetadata(string record, string source, long bytes) {
   try {
    using (var fs = new FileStream(record, FileMode.Open, FileAccess.Read, FileShare.ReadWrite|FileShare.Delete))
    using (var reader = new BinaryReader(fs, Encoding.Unicode)) {
     long version = reader.ReadInt64();
     if ((version != 1 && version != 2) || reader.ReadInt64() != bytes) return false;
     reader.ReadInt64(); // Windows deletion timestamp
     int length = version == 2 ? reader.ReadInt32() : (int)((fs.Length - fs.Position) / 2);
     if (length < 1 || length > 32768) return false;
     string original = Encoding.Unicode.GetString(reader.ReadBytes(length * 2)).TrimEnd('\0');
     return String.Equals(Engine.NormalPath(original), Engine.NormalPath(source), StringComparison.OrdinalIgnoreCase);
    }
   } catch { return false; }
  }
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
  [return: MarshalAs(UnmanagedType.Bool)]
  static extern bool GetVolumeNameForVolumeMountPoint(string mount, StringBuilder path, int size);
  [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
  static extern void SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

  [DllImport("shell32.dll", PreserveSig=false)]
  static extern void SHGetKnownFolderItem(ref Guid folder,uint flags,IntPtr token,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out IShellItem item);

  [ComImport, Guid("00ADB003-BDE9-45C6-8E29-D09F9353E108"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface ITransferSource {
   void Advise(IntPtr sink,out uint cookie);
   void Unadvise(uint cookie);
   void SetProperties(IntPtr properties);
   void OpenItem(IShellItem item,uint flags,ref Guid iid,out IntPtr opened);
   void MoveItem(IShellItem item,IShellItem destination,[MarshalAs(UnmanagedType.LPWStr)] string name,uint flags,out IShellItem moved);
   void RecycleItem(IShellItem item,IShellItem destination,uint flags,out IShellItem recycled);
  }

  [ComImport, Guid("3AD05575-8857-4850-9277-11B85BDB8E09")]
  class FileOperation { }
  [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IShellItem {
   void BindToHandler(IntPtr ctx, ref Guid handler, ref Guid iid, out IntPtr result);
   void GetParent(out IShellItem item);
   void GetDisplayName(uint kind, out IntPtr name);
   void GetAttributes(uint mask, out uint attrs);
   void Compare(IShellItem other, uint hint, out int order);
  }
  [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  interface IFileOperation {
   void Advise(IntPtr sink, out uint cookie);
   void Unadvise(uint cookie);
   void SetOperationFlags(uint flags);
   void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
   void SetProgressDialog(IntPtr dialog);
   void SetProperties(IntPtr changes);
   void SetOwnerWindow(IntPtr owner);
   void ApplyPropertiesToItem(IntPtr item);
   void ApplyPropertiesToItems(IntPtr items);
   void RenameItem(IntPtr item, IntPtr name, IntPtr sink);
   void RenameItems(IntPtr items, IntPtr name);
   void MoveItem(IntPtr item, IntPtr dest, IntPtr name, IntPtr sink);
   void MoveItems(IntPtr items, IntPtr dest);
   void CopyItem(IntPtr item, IntPtr dest, IntPtr name, IntPtr sink);
   void CopyItems(IntPtr items, IntPtr dest);
   void DeleteItem(IShellItem item, IProgressSink sink);
   void DeleteItems(IntPtr items);
   void NewItem(IntPtr folder, uint attributes, IntPtr name, IntPtr template, IntPtr sink);
   void PerformOperations();
   void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
  }

  [ComVisible(true), Guid("04B0F1A7-9490-44BC-96E1-4296A31252E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
  public interface IProgressSink {
   [PreserveSig] int StartOperations();
   [PreserveSig] int FinishOperations(int result);
   [PreserveSig] int PreRenameItem(uint flags, IntPtr item, IntPtr name);
   [PreserveSig] int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr created);
   [PreserveSig] int PreMoveItem(uint flags, IntPtr item, IntPtr dest, IntPtr name);
   [PreserveSig] int PostMoveItem(uint flags, IntPtr item, IntPtr dest, IntPtr name, int result, IntPtr created);
   [PreserveSig] int PreCopyItem(uint flags, IntPtr item, IntPtr dest, IntPtr name);
   [PreserveSig] int PostCopyItem(uint flags, IntPtr item, IntPtr dest, IntPtr name, int result, IntPtr created);
   [PreserveSig] int PreDeleteItem(uint flags, IntPtr item);
   [PreserveSig] int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created);
   [PreserveSig] int PreNewItem(uint flags, IntPtr item, IntPtr name);
   [PreserveSig] int PostNewItem(uint flags, IntPtr item, IntPtr name, IntPtr template, uint attrs, int result, IntPtr created);
   [PreserveSig] int UpdateProgress(uint total, uint done);
   [PreserveSig] int ResetTimer();
   [PreserveSig] int PauseTimer();
   [PreserveSig] int ResumeTimer();
  }
  [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
  public sealed class Sink : IProgressSink {
   readonly Entry file;
   readonly string root;
   public int Result;
   public bool Recycled;
   public string Rejection, RecycledPath;
   internal Sink(Entry file, string root) { this.file = file; this.root = root; }
   public int PreDeleteItem(uint flags, IntPtr item) {
    if ((flags & RecycleTransfer) == 0) { Rejection = "Windows 无法保证移入回收站，已拦截"; return UserCancelled; }
    string reason;
    if (!Engine.CleanupAllowed(file, root, out reason)) { Rejection = reason; return UserCancelled; }
    return 0;
   }
   public int PostDeleteItem(uint flags, IntPtr item, int result, IntPtr created) {
    Result = result;
    Recycled = (flags & RecycleTransfer) != 0 && result >= 0 && created != IntPtr.Zero;
    if (Recycled) {
     object obj = null;
     try { obj = Marshal.GetObjectForIUnknown(created); RecycledPath = RecycledItemPath((IShellItem)obj); }
     catch (COMException) { }
     finally { if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    }
    return 0;
   }
   public int StartOperations() { return 0; }
   public int FinishOperations(int result) { return 0; }
   public int PreRenameItem(uint flags, IntPtr item, IntPtr name) { return UserCancelled; }
   public int PostRenameItem(uint flags, IntPtr item, IntPtr name, int result, IntPtr created) { return 0; }
   public int PreMoveItem(uint flags, IntPtr item, IntPtr dest, IntPtr name) { return UserCancelled; }
   public int PostMoveItem(uint flags, IntPtr item, IntPtr dest, IntPtr name, int result, IntPtr created) { return 0; }
   public int PreCopyItem(uint flags, IntPtr item, IntPtr dest, IntPtr name) { return UserCancelled; }
   public int PostCopyItem(uint flags, IntPtr item, IntPtr dest, IntPtr name, int result, IntPtr created) { return 0; }
   public int PreNewItem(uint flags, IntPtr item, IntPtr name) { return UserCancelled; }
   public int PostNewItem(uint flags, IntPtr item, IntPtr name, IntPtr template, uint attrs, int result, IntPtr created) { return 0; }
   public int UpdateProgress(uint total, uint done) { return 0; }
   public int ResetTimer() { return 0; }
   public int PauseTimer() { return 0; }
   public int ResumeTimer() { return 0; }
  }
 }
}
