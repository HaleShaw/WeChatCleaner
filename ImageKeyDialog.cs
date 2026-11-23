using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace WeChatCleaner {
 internal sealed class ImageKeyDialog : ModalWindow {
  static readonly FontFamily Mono=new FontFamily("Consolas, Courier New");
  readonly SecretInput aes, xor;
  readonly TextBlock status;
  readonly Border statusCard;
  readonly Button show, cancel;
  readonly bool optionalAes;
  bool revealed;
  internal readonly StackPanel Extra=new StackPanel(), Secondary=new StackPanel { Orientation=Orientation.Horizontal, VerticalAlignment=VerticalAlignment.Center };
  internal readonly Button Confirm, SavePending;
  internal bool UseOriginal { get; private set; }

  internal ImageKeyDialog(Window owner,string title,string account,string root,string file,string help,AttachmentKey initial,
   string submit,bool allowOriginal=false,bool optionalAes=false)
   : this(owner,title,account,root,file,help,initial,submit,allowOriginal,optionalAes,new StackPanel(),new Grid()) { }

  ImageKeyDialog(Window owner,string title,string account,string root,string file,string help,AttachmentKey initial,
   string submit,bool allowOriginal,bool optionalAes,StackPanel content,Grid footer)
   : base(owner,title,content,footer,632,430,true,true,true) {
   var body=content; this.optionalAes=optionalAes;
   Fit(owner,632,430,470,230);
   SizeToContent=SizeToContent.Height;
   var identity=new StackPanel();
   identity.Children.Add(Ui.Label(account,13,Ui.Ink,true));
   identity.Children.Add(ContextLine(root??"旧版记录未保存账号目录。重新添加原目录后可恢复关联。"));
   if (!String.IsNullOrEmpty(file)) identity.Children.Add(ContextLine("文件："+file));
   body.Children.Add(new Border { Child=identity,Background=Ui.Canvas,BorderBrush=Ui.Line,BorderThickness=new Thickness(1),
    CornerRadius=new CornerRadius(8),Padding=new Thickness(12,9,12,9) });

   var toolbar=new DockPanel { Margin=new Thickness(0,12,0,6) };
   show=Ui.ActionButton("显示密钥","同时显示或隐藏 AES 密钥和 XOR 值",ToggleShow);
   AutomationProperties.SetName(show,"显示或隐藏密钥");
   DockPanel.SetDock(show,Dock.Right); toolbar.Children.Add(show);
   if (submit==null && initial==null) show.IsEnabled=false;
   var revealHelp=Ui.Label(help,12,Ui.Muted,false); revealHelp.Margin=new Thickness(0,0,14,0);
   toolbar.Children.Add(revealHelp); body.Children.Add(toolbar);

   bool readOnly=submit==null;
   var fields=new Grid();
   fields.ColumnDefinitions.Add(new ColumnDefinition()); fields.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(132) });
   string text=initial==null || initial.Aes==null ? "" : BitConverter.ToString(initial.Aes).Replace("-","");
   string tail=initial!=null && initial.Tail.HasValue ? "0x"+initial.Tail.Value.ToString("X2",CultureInfo.InvariantCulture) : "";
   aes=new SecretInput("AES 密钥",optionalAes ? "选填" : null,text,64,readOnly);
   xor=new SecretInput("XOR 值","选填",tail,8,readOnly);
   aes.View.Margin=new Thickness(0,0,12,0);
   fields.Children.Add(aes.View); Grid.SetColumn(xor.View,1); fields.Children.Add(xor.View); body.Children.Add(fields);
   body.Children.Add(Extra);

   status=Ui.Label("",12,Ui.Danger,false);
   statusCard=new Border { Child=status,Visibility=Visibility.Collapsed,CornerRadius=new CornerRadius(7),Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,10,0,0) };
   body.Children.Add(statusCard);

   footer.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto}); footer.ColumnDefinitions.Add(new ColumnDefinition());
   footer.Children.Add(Secondary);
   if (allowOriginal) Secondary.Children.Add(Ui.Button("保存加密原件",delegate { UseOriginal=true; DialogResult=true; },null));
   cancel=Ui.Button(readOnly ? "关闭" : "取消",null,null); cancel.IsCancel=true;
   Confirm=Ui.Button(submit??"",null,"PrimaryButton"); Confirm.IsDefault=!readOnly;
   Confirm.Visibility=readOnly ? Visibility.Collapsed : Visibility.Visible;
   SavePending=Ui.Button("保存为待验证",null,null); SavePending.Visibility=Visibility.Collapsed;
   var actions=new WrapPanel { HorizontalAlignment=HorizontalAlignment.Right };
   foreach (var button in new[]{cancel,SavePending,Confirm}) { button.Margin=new Thickness(8,0,0,0); actions.Children.Add(button); }
   Grid.SetColumn(actions,1); footer.Children.Add(actions);
   Ui.FocusFirst(this,readOnly ? (Control)cancel : (initial!=null && initial.Aes!=null && !initial.Tail.HasValue && file!=null ? xor.Password : aes.Password));
   Closed+=delegate { aes.Clear(); xor.Clear(); };
  }
  static TextBlock ContextLine(string text) {
   var line=Ui.Label(text,11.5,Ui.Muted,false); line.Margin=new Thickness(0,4,0,0); line.ToolTip=text;
   line.TextWrapping=TextWrapping.NoWrap; line.TextTrimming=TextTrimming.CharacterEllipsis; return line;
  }
  internal AttachmentKey ReadKey() {
   AttachmentKey key; string error; bool aesError;
   if (AttachmentKeys.TryParse(aes.Value,xor.Value,out key,out error,out aesError,optionalAes)) return key;
   Report(error,true,aesError); return null;
  }
  internal void Report(string message,bool error,bool? focusAes=null) {
   status.Text=message??""; status.Foreground=error ? Ui.Danger : Ui.Muted;
   statusCard.Background=Ui.Brush(error ? "#FCF0ED" : "#F1F6F3");
   statusCard.Visibility=String.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
   if (focusAes.HasValue) (focusAes.Value ? aes : xor).Focus();
  }
  internal void ReportKeyIssue(AttachmentKeyNeeded issue,bool error,bool focus) {
   xor.SetTag(issue.NeedsTail ? "此文件需要" : "选填");
   Report(issue.Message,error,focus ? (bool?)issue.NeedsAes : null);
  }
  internal void SetBusy(bool busy) {
   Confirm.IsEnabled=SavePending.IsEnabled=Secondary.IsEnabled=show.IsEnabled=aes.View.IsEnabled=xor.View.IsEnabled=Extra.IsEnabled=!busy;
   if (busy) Report("正在验证图片…",false);
  }
  void ToggleShow() {
   revealed=!revealed;
   aes.Reveal(revealed); xor.Reveal(revealed);
   show.Content=revealed ? "隐藏密钥" : "显示密钥";
  }
  sealed class SecretInput {
   internal readonly StackPanel View=new StackPanel();
   internal readonly PasswordBox Password;
   readonly TextBox text;
   readonly TextBlock hint;
   readonly bool readOnly;
   internal SecretInput(string label,string tag,string initial,int length,bool readOnly) {
    this.readOnly=readOnly;
    var line=new DockPanel { Margin=new Thickness(0,0,0,5) };
    if (tag!=null && !readOnly) {
     hint=Ui.Label(tag,11,Ui.Muted,false); DockPanel.SetDock(hint,Dock.Right); line.Children.Add(hint);
    }
    var caption=Ui.Label(label,12,Ui.Ink,true); line.Children.Add(caption); View.Children.Add(line);
    var field=new Grid();
    Password=new PasswordBox { FontFamily=Mono,MaxLength=length,Password=initial,IsTabStop=!readOnly,IsHitTestVisible=!readOnly,Focusable=!readOnly };
    InputMethod.SetIsInputMethodEnabled(Password,false);
    AutomationProperties.SetName(Password,label); field.Children.Add(Password);
    text=new TextBox { FontFamily=Mono,MaxLength=length,IsUndoEnabled=false,IsReadOnly=readOnly,Visibility=Visibility.Collapsed };
    InputMethod.SetIsInputMethodEnabled(text,false);
    AutomationProperties.SetName(text,label); field.Children.Add(text); View.Children.Add(field);
   }
   internal string Value { get { return text.Visibility==Visibility.Visible ? text.Text : Password.Password; } }
   internal void Reveal(bool visible) {
    if (visible) { text.Text=Password.Password; Password.Clear(); }
    else { Password.Password=text.Text; text.Clear(); }
    text.Visibility=visible ? Visibility.Visible : Visibility.Collapsed;
    Password.Visibility=visible ? Visibility.Collapsed : Visibility.Visible;
   }
   internal void SetTag(string tag) { if (hint!=null) hint.Text=tag; }
   internal void Focus() { if (!readOnly) { if (text.Visibility==Visibility.Visible) text.Focus(); else Password.Focus(); } }
   internal void Clear() { text.Clear(); Password.Clear(); }
  }
 }
}
