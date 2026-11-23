using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace WeChatCleaner {
 internal static class FileActions {
  static readonly HashSet<Window> Busy=new HashSet<Window>();
  static MediaScratch previews;

  static bool Alive(Window owner) { return owner==null || owner.IsVisible; }
  internal static Task Open(Window owner,Entry file,string ffmpegPath=null,string accountName=null,FileActionTargets targets=null) {
   string ext=Path.GetExtension(file.Path).ToLowerInvariant();
   if (Array.IndexOf(new[]{".exe",".dll",".msi",".lnk",".url",".scr",".cmd",".bat",".com",".ps1",".hta",".vbs",".js",".reg",".wsf"},ext)>=0) {
    MessageBox.Show(owner,"此文件可能执行程序或脚本。可通过「打开路径」在资源管理器中查看。","无法安全预览",MessageBoxButton.OK,MessageBoxImage.Information);
    return Task.FromResult(false);
   }
   var modal=owner as ModalWindow;
   return Execute(owner,file,false,modal==null ? (Action<string>)null : modal.Notice,ffmpegPath,accountName,targets);
  }
  internal static Task Save(Window owner,Entry file,Action<string> notice,string ffmpegPath=null,string accountName=null,FileActionTargets targets=null) {
   return Execute(owner,file,true,notice,ffmpegPath,accountName,targets);
  }

  static async Task Execute(Window owner,Entry file,bool saving,Action<string> notice,string ffmpegPath,string accountName,FileActionTargets targets) {
   if (!Busy.Add(owner)) return;
   try {
    Choice choice;
    try { choice=await Prepare(owner,file,saving,ffmpegPath,accountName); }
    catch (Exception ex) {
     if (!(ex is InvalidDataException) && !(ex is IOException)) throw;
     if ((!saving && !AttachmentMedia.Candidate(file)) || !File.Exists(file.Path)) throw;
     choice=OfferOriginal(owner,ex.Message);
    }
    using (choice) {
     if (!Alive(owner) || choice==null) return;
     if (saving || choice.UseOriginal) {
      if (!await SavePrepared(owner,file,choice.Output,choice.UseOriginal,notice,targets)) return;
     } else {
      var output=choice.Output;
      if (output!=null && output.PreviewIssue!=null) {
       if (OfferOriginal(owner,output.PreviewIssue)!=null) await SavePrepared(owner,file,null,true,notice,targets);
       return;
      }
      string path=output==null ? file.Path : await CreatePreview(file,output);
      if (!Alive(owner)) return;
      if (targets!=null && targets.Open!=null) targets.Open(path);
      else using (Process.Start(new ProcessStartInfo(path) { UseShellExecute=true })) { }
     }
     if (choice.Key!=null && !AttachmentKeys.Remember(file.OwnerRoot,choice.Key,accountName,choice.Output.KeyProof,keepSavedTail:true) && Alive(owner)) {
      const string warning="文件操作已完成，但无法保存图片密钥。请检查配置目录。";
      if (notice!=null) notice(warning);
      else MessageBox.Show(owner,warning,"图片密钥",MessageBoxButton.OK,MessageBoxImage.Information);
     }
    }
   } catch (Exception ex) {
    if (targets!=null) throw;
    if (Alive(owner)) MessageBox.Show(owner,ex.Message,saving ? "另存为失败" : "无法查看文件",MessageBoxButton.OK,MessageBoxImage.Warning);
   } finally { Busy.Remove(owner); }
  }

  static async Task<bool> SavePrepared(Window owner,Entry file,AttachmentOutput output,bool raw,Action<string> notice,FileActionTargets targets) {
   string name=file.Name??Path.GetFileName(file.Path);
   var dlg=new SaveFileDialog { FileName=output==null ? name : Path.GetFileNameWithoutExtension(name)+output.Extension,
    Title=raw ? "保存加密原件" : "另存为",OverwritePrompt=true,AddExtension=true,
    InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) };
   string ext=output!=null ? output.Extension : raw ? Path.GetExtension(name) : null;
   if (!String.IsNullOrEmpty(ext)) {
    dlg.DefaultExt=ext;
    string label=raw ? "原附件" : ext==".mp3" ? "MP3 音频" : ext.Substring(1).ToUpperInvariant()+" 文件";
    dlg.Filter=label+" (*"+ext+")|*"+ext;
    dlg.FileOk+=delegate(object sender,System.ComponentModel.CancelEventArgs e) {
     if (!Path.GetExtension(dlg.FileName).Equals(ext,StringComparison.OrdinalIgnoreCase)) {
      MessageBox.Show(owner,"请保留 "+ext+" 扩展名。",dlg.Title,MessageBoxButton.OK,MessageBoxImage.Information); e.Cancel=true;
     }
    };
   }
   bool selected=targets!=null && targets.SelectDestination!=null ? targets.SelectDestination(dlg) : dlg.ShowDialog(owner)==true;
   if (!selected) return false;
   string destination=dlg.FileName;
   await Task.Run(()=>WriteTo(file,destination,output==null ? null : output.Data));
   if (notice!=null && Alive(owner)) notice((raw ? "已保存加密原件：" : "已保存：")+Path.GetFileName(destination));
   return true;
  }

  sealed class Choice : IDisposable {
   internal bool UseOriginal;
   internal AttachmentOutput Output;
   internal AttachmentKey Key;
   public void Dispose() { if (Key!=null) Key.Dispose(); }
  }
  static Choice Decoded(AttachmentOutput output,AttachmentKey key,Entry file) {
   return new Choice { Output=output,Key=key!=null && output!=null && output.KeyProof!=ImageKeyProof.None && file.OwnerKind==SourceKind.New ?
    new AttachmentKey((byte[])key.Aes.Clone(),key.Tail) : null };
  }
  static async Task<Choice> Prepare(Window owner,Entry file,bool saving,string ffmpegPath,string accountName) {
   AttachmentKeyNeeded reason;
   try { return Decoded(await Task.Run(()=>AttachmentMedia.Prepare(file,null,ffmpegPath)),null,file); }
   catch (AttachmentKeyNeeded ex) { reason=ex; }
   if (!Alive(owner)) return null;
   using (var saved=reason.IsV2 && file.OwnerKind==SourceKind.New ? AttachmentKeys.Load(file.OwnerRoot) : null) {
    if (saved!=null) {
     try { return Decoded(await Task.Run(()=>AttachmentMedia.Prepare(file,saved,ffmpegPath)),saved,file); }
     catch (AttachmentKeyNeeded ex) { reason=ex; }
    }
    if (!Alive(owner)) return null;
    string account=String.IsNullOrWhiteSpace(accountName) ? Path.GetFileName(file.OwnerRoot) : accountName;
    string help=reason.IsV2 ? (saving ? "解密图片保存成功后" : "图片打开成功后")+"，会自动记住此账号通过验证的值。" :
     "使用已取得的值验证此文件。";
    if (saved!=null && saved.Tail.HasValue) help+=" 如果只验证了 AES，已保存的 XOR 会保留。";
    var input=new ImageKeyDialog(owner,saving ? "验证密钥并另存图片" : "验证密钥以查看图片",account,file.OwnerRoot,
     file.Name??Path.GetFileName(file.Path),help,saved,saving ? "验证并另存" : "验证并查看",true,!reason.IsV2 && !reason.NeedsAes);
    input.ReportKeyIssue(reason,saved!=null,false);
    Choice choice=null;
    input.Confirm.Click+=async delegate {
     if (!input.Confirm.IsEnabled) return;
     using (var key=input.ReadKey()) {
      if (key==null) return;
      input.SetBusy(true);
      try {
       var decoded=await Task.Run(()=>AttachmentMedia.Prepare(file,key,ffmpegPath));
       if (!input.IsVisible) return;
       if (!saving && decoded!=null && decoded.PreviewIssue!=null) { input.Report(decoded.PreviewIssue,true); return; }
       choice=Decoded(decoded,key,file); input.DialogResult=true;
      } catch (AttachmentKeyNeeded ex) { if (input.IsVisible) input.ReportKeyIssue(ex,true,true); }
      catch (Exception ex) { if (input.IsVisible) input.Report(ex.Message,true); }
      finally { if (input.IsVisible) input.SetBusy(false); }
     }
    };
    if (input.ShowDialog()!=true) return null;
    return input.UseOriginal ? new Choice { UseOriginal=true } : choice;
   }
  }
  static Choice OfferOriginal(Window owner,string message) {
   if (!Alive(owner)) return null;
   var label=Ui.Label(message+" 可以保留未转换的原文件。",13,Ui.Ink,false);
   var cancel=Ui.Button("取消",null,null); cancel.IsCancel=true;
   var save=Ui.Button("保存原文件",null,"PrimaryButton");
   var dialog=new ModalWindow(owner,"保留原文件",label,Ui.ButtonRow(cancel,save),510,222,true);
   save.Click+=delegate { dialog.DialogResult=true; };
   return dialog.ShowDialog()==true ? new Choice { UseOriginal=true } : null;
  }
  static Task<string> CreatePreview(Entry file,AttachmentOutput output) {
   if (previews==null) {
    previews=new MediaScratch();
    if (Application.Current!=null) Application.Current.Exit += delegate { previews.Dispose(); };
   }
   string path=previews.PathFor(Guid.NewGuid().ToString("N")+output.Extension);
   return Task.Run(delegate {
    using (var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write)) WriteContent(file,stream,output.Data);
    return path;
   });
  }
  internal static void Reveal(Window owner,Entry file) {
   try {
    AttachmentMedia.RequireSource(file);
    Process.Start("explorer.exe","/select,\""+file.Path+"\"");
   } catch (Exception ex) { MessageBox.Show(owner,ex.Message,"无法打开路径",MessageBoxButton.OK,MessageBoxImage.Warning); }
  }
  internal static void Copy(Window owner,Entry file,Action<string> notice) {
   try { Clipboard.SetText(file.Path); notice("已复制完整路径"); }
   catch (Exception ex) { MessageBox.Show(owner,ex.Message,"复制路径失败",MessageBoxButton.OK,MessageBoxImage.Warning); }
  }
  internal static void CopyTo(Entry file,string destination) { WriteTo(file,destination,null); }
  internal static void WriteTo(Entry file,string destination,byte[] bytes) {
   string full=Engine.NormalPath(destination);
   if (String.Equals(Engine.NormalPath(file.Path),full,StringComparison.OrdinalIgnoreCase) ||
       (!String.IsNullOrEmpty(file.OwnerRoot) && Engine.IsInside(file.OwnerRoot,full))) {
    throw new IOException("请保存到账号目录之外，避免覆盖微信数据。");
   }
   string parent=Path.GetDirectoryName(full);
   bool existed=File.Exists(full);
   if (!Engine.LinkFree(parent) || (existed && !Engine.LinkFree(full))) throw new IOException("目标目录或文件不可访问，或包含链接。");
   string temp=Path.Combine(parent,".wcc-save-"+Guid.NewGuid().ToString("N")+".tmp");
   try {
    using (var to=new FileStream(temp,FileMode.CreateNew,FileAccess.Write)) WriteContent(file,to,bytes);
    if (existed) File.Replace(temp,full,null);
    else File.Move(temp,full);
   } finally { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
  }
  static void WriteContent(Entry file,Stream destination,byte[] bytes) {
   if (bytes==null) {
    AttachmentMedia.RequireSource(file);
    using (var input=new FileStream(file.Path,FileMode.Open,FileAccess.Read,FileShare.Read)) input.CopyTo(destination);
   } else destination.Write(bytes,0,bytes.Length);
  }
 }

 // The regression harness can choose fixture paths without opening a native dialog or player.
 internal sealed class FileActionTargets {
  internal readonly Func<SaveFileDialog,bool> SelectDestination;
  internal readonly Action<string> Open;
  internal FileActionTargets(Func<SaveFileDialog,bool> selectDestination,Action<string> open=null) { SelectDestination=selectDestination; Open=open; }
 }
}
