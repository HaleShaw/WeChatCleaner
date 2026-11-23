using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Threading.Tasks;

namespace WeChatCleaner {
 public partial class MainWindow {
  FrameworkElement BuildCleanupSettings() {
   var panel=new StackPanel();
   panel.Children.Add(Ui.Label("清理规则",15,Ui.Ink,true));
   var columns=new Grid { Margin=new Thickness(0,10,0,0) };
   for (int i=0;i<3;i++) columns.ColumnDefinitions.Add(new ColumnDefinition());
   var shown=settings;
   var mode=new ComboBox { Width=155,HorizontalAlignment=HorizontalAlignment.Left,IsEnabled=profileLocation.Problem==null };
   mode.Items.Add("移至回收站"); mode.Items.Add("直接删除"); mode.SelectedIndex=shown.PermanentDelete ? 1 : 0;
   AutomationProperties.SetName(mode,"清理方式");
   var modeDesc=RuleColumn(columns,0,"清理方式",mode,ModeDescription());
   bool changingMode=false;
   mode.SelectionChanged+=delegate {
    if (changingMode || shown!=settings || mode.SelectedIndex<0) return;
    bool before=shown.PermanentDelete; shown.PermanentDelete=mode.SelectedIndex==1;
    if (!shown.Save()) {
     shown.PermanentDelete=before; changingMode=true; mode.SelectedIndex=before ? 1 : 0; changingMode=false;
     Toast("无法保存清理方式，仍使用原设置。");
    }
    modeDesc.Text=ModeDescription();
   };
   var keep=new ComboBox { Width=155,HorizontalAlignment=HorizontalAlignment.Left,IsEditable=true,IsTextSearchEnabled=false,IsEnabled=profileLocation.Problem==null };
   foreach (int days in new[]{30,90,180,365}) keep.Items.Add(AgeText(days));
   if (shown.Days>0 && !keep.Items.Contains(AgeText(shown.Days))) keep.Items.Add(AgeText(shown.Days));
   keep.Items.Add("所有时间"); keep.Items.Add("自定义…"); keep.SelectedItem=AgeText(shown.Days);
   AutomationProperties.SetName(keep,"默认保留时间");
   RuleColumn(columns,1,"默认保留时间",keep,"默认应用到账号页，可单独调整。");
   bool changingDays=false;
   Action<string> saveDays=delegate(string text) {
    if (changingDays || shown!=settings) return;
    int days;
    if (TryDays(text,out days) && days!=shown.Days) {
     int before=shown.Days; shown.Days=days;
     if (shown.Save()) { DisplayAge(days); ApplyFilter(); }
     else { shown.Days=before; Toast("无法保存默认保留时间，仍使用原设置。"); }
    } else if (!TryDays(text,out days) && text!="自定义…") Toast("请输入 1 到 36500 之间的天数。");
    changingDays=true;
    string label=AgeText(shown.Days);
    if (!keep.Items.Contains(label)) keep.Items.Insert(keep.Items.Count-1,label);
    keep.SelectedItem=label; keep.Text=label; changingDays=false;
   };
   keep.SelectionChanged+=delegate { if (keep.SelectedItem as string=="自定义…") Ui.FocusEdit(keep); else if (keep.SelectedIndex>=0) saveDays(keep.SelectedItem as string); };
   Ui.ApplyOnExit(keep,delegate { saveDays(Ui.EditText(keep)); });
   var skip=Ui.Label("始终开启",12,Ui.Accent,true); skip.MinHeight=32;
   RuleColumn(columns,2,"跳过目录联接",skip,"扫描和清理不会沿链接越出账号目录。");
   panel.Children.Add(columns);
   return panel;
  }
  string ModeDescription() {
   return settings.PermanentDelete ? "文件会被永久删除，每次操作前确认。" : "默认方式，可从 Windows 回收站找回。";
  }
  static TextBlock RuleColumn(Grid grid,int index,string label,FrameworkElement control,string description) {
   var column=new StackPanel { Margin=new Thickness(index==0 ? 0 : 14,0,10,0) };
   column.Children.Add(Ui.Label(label,12,Ui.Ink,true));
   control.Margin=new Thickness(0,4,0,0); column.Children.Add(control);
   var help=Ui.Label(description,11.5,Ui.Muted,false); help.Margin=new Thickness(0,5,0,0); column.Children.Add(help);
   Grid.SetColumn(column,index); grid.Children.Add(column);
   return help;
  }

  static string ProfileModeText(ProfileMode mode) {
   switch (mode) {
    case ProfileMode.Portable: return "随程序携带";
    case ProfileMode.Custom: return "自定义目录";
    case ProfileMode.Environment: return "WCC_PROFILE_DIR 覆盖";
    default: return "Windows 用户目录";
   }
  }
  FrameworkElement BuildProfileSettings() {
   var panel=new StackPanel();
   panel.Children.Add(Ui.Label("配置目录",15,Ui.Ink,true));
   var hint=Ui.Label("存放清理设置、数据源记录和加密的图片密钥。",12,Ui.Muted,false);
   hint.Margin=new Thickness(0,4,0,6); panel.Children.Add(hint);
   var applied=profileLocation;
   var mode=applied.Mode;
   string typed=mode==ProfileMode.Custom ? applied.Path??"" : "";
   bool busy=false;
   var choices=new WrapPanel(); panel.Children.Add(choices);
   var field=new Grid { Margin=new Thickness(0,7,0,0) };
   field.ColumnDefinitions.Add(new ColumnDefinition());
   field.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   field.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
   var path=new TextBox { VerticalAlignment=VerticalAlignment.Center,Text=applied.Path??"",IsReadOnly=mode!=ProfileMode.Custom };
   AutomationProperties.SetName(path,"配置目录路径"); field.Children.Add(path);
   var browse=FolderButton("浏览…",delegate {
    try {
     using (var picker=new System.Windows.Forms.FolderBrowserDialog { Description="选择 WeChat Cleaner 配置目录",ShowNewFolderButton=true,SelectedPath=DataFolderStartPath(path.Text) }) {
      if (picker.ShowDialog(new FolderOwner(this))==System.Windows.Forms.DialogResult.OK) path.Text=picker.SelectedPath;
     }
    } catch (Exception ex) { if (!ProfileStorage.Expected(ex) && !(ex is System.ComponentModel.Win32Exception)) throw; Toast("无法打开目录选择器，可直接输入路径。"); }
   });
   browse.Margin=new Thickness(8,0,0,0); Grid.SetColumn(browse,1); field.Children.Add(browse);
   var apply=Ui.Button("应用",null,"PrimaryButton"); apply.Margin=new Thickness(8,0,0,0);
   AutomationProperties.SetName(apply,"应用配置目录"); Grid.SetColumn(apply,2); field.Children.Add(apply); panel.Children.Add(field);
   var footer=new DockPanel { Margin=new Thickness(0,6,0,0) };
   var open=Ui.ActionButton("打开目录 ↗","打开当前生效的配置目录",delegate {
    try {
     if (applied.Problem!=null || !Directory.Exists(applied.Path) || !Engine.LinkFree(applied.Path)) { Toast("配置目录尚未创建或当前不可访问。"); return; }
     Process.Start(new ProcessStartInfo(applied.Path) { UseShellExecute=true });
    } catch (Exception) { Toast("无法打开配置目录。"); }
   });
   open.IsEnabled=applied.Problem==null;
   DockPanel.SetDock(open,Dock.Right); footer.Children.Add(open);
   if (applied.Problem!=null && applied.Mode!=ProfileMode.Environment) {
    var retry=Ui.ActionButton("重试","重新读取当前配置目录",delegate {
     var state=ProfileStorage.App.Current;
     if (state.Problem!=null) { Toast(state.Problem); return; }
     ReloadProfile(state); Toast("已恢复配置目录。");
    });
    DockPanel.SetDock(retry,Dock.Right); footer.Children.Add(retry);
   }
   var status=Ui.Label("",11.5,Ui.Muted,false); status.ToolTip=applied.Path;
   footer.Children.Add(status); panel.Children.Add(footer);
   Action<string,bool> message=(text,error)=>{ status.Text=text; status.Foreground=error ? Ui.Danger : Ui.Muted; };
   Action update=delegate {
    var proposed=mode==ProfileMode.Environment ? applied : ProfileStorage.App.Propose(mode,path.Text);
    browse.IsEnabled=!busy && mode==ProfileMode.Custom;
    apply.IsEnabled=!busy && mode!=ProfileMode.Environment && !String.IsNullOrWhiteSpace(path.Text) &&
     (applied.Problem!=null || mode!=applied.Mode || !SamePath(applied.Path,proposed.Path));
    if (applied.Problem!=null) message(applied.Problem,true);
    else if (applied.Mode==ProfileMode.Environment) message("当前由 WCC_PROFILE_DIR 指定。移除环境变量并重启后可在这里切换。",false);
    else if (apply.IsEnabled && proposed.Problem==null) message("当前生效："+applied.Path+"。点击「应用」确认切换。",false);
    else message("当前生效："+ProfileModeText(applied.Mode),false);
   };
   foreach (ProfileMode item in new[]{ProfileMode.WindowsUser,ProfileMode.Portable,ProfileMode.Custom}) {
    ProfileMode option=item;
    var radio=new RadioButton { Content=ProfileModeText(option),GroupName="ProfileMode",IsChecked=option==mode,
     IsEnabled=applied.Mode!=ProfileMode.Environment,Margin=new Thickness(0,0,16,0),Style=(Style)FindResource("ProfileModeRadio") };
    radio.Checked+=delegate {
     mode=option; path.IsReadOnly=mode!=ProfileMode.Custom;
     path.Text=mode==ProfileMode.Custom ? typed : ProfileStorage.App.Propose(mode,null).Path;
     update();
    };
    choices.Children.Add(radio);
   }
   path.TextChanged+=delegate { if (mode==ProfileMode.Custom) typed=path.Text; path.ToolTip=path.Text; update(); };
   path.ToolTip=path.Text;
   apply.Click+=delegate {
    var target=ProfileStorage.App.Propose(mode,path.Text);
    if (target.Problem!=null) { message(target.Problem,true); return; }
    bool? copy=ConfirmProfileSwitch(applied,target);
    if (!copy.HasValue) return;
    busy=true; update();
    string error=null;
    var guarded=sources.Select(s=>s.Path).Concat(accounts.Select(a=>a.Path)).ToArray();
    bool success;
    try {
     success=Ui.Progress(this,"切换配置目录","正在检查目标目录并应用配置…",delegate { return ProfileStorage.App.TrySwitch(applied,target,copy.Value,guarded,out error); },false);
    } catch (InvalidOperationException ex) { success=false; error=ex.Message; }
    busy=false;
    if (!success) { update(); message(error??"无法切换配置目录。",true); return; }
    ReloadProfile(ProfileStorage.App.Current);
    Toast("已切换配置目录。");
   };
   update();
   return panel;
  }
  bool? ConfirmProfileSwitch(ProfileLocation current,ProfileLocation target) {
   bool same=SamePath(current.Path,target.Path), existing=ProfileStorage.HasConfiguration(target);
   bool? copy=null;
   var body=new StackPanel();
   body.Children.Add(Ui.Label(target.Path,13,Ui.Ink,true));
   string description=same ? "保留此目录内的配置，更新存放方式。" : existing ?
    "目标目录已有配置。可直接使用，或先将它备份，再复制当前配置。" :
    "将当前配置复制到此目录。原配置和微信文件会保留。";
   if (current.Problem!=null && !same) description=existing ? "当前配置不可访问。可以启用目标已有的配置。" : "当前配置不可访问。将使用目标目录开始一套新配置。";
   var tip=Ui.Label(description,12,Ui.Muted,false); tip.Margin=new Thickness(0,9,0,0); body.Children.Add(tip);
   var cancelButton=Ui.Button("取消",null,null); cancelButton.IsCancel=true;
   var use=Ui.Button(existing&&!same ? "使用目标配置" : "启用此目录",null,"PrimaryButton"); use.IsDefault=true;
   var migrate=Ui.Button(existing ? "备份后复制" : "复制并启用",null,existing ? null : "PrimaryButton");
   var buttons=Ui.ButtonRow(cancelButton);
   if (!same && current.Problem==null) { migrate.Margin=new Thickness(8,0,0,0); buttons.Children.Add(migrate); }
   if (existing || same || current.Problem!=null) { use.Margin=new Thickness(8,0,0,0); buttons.Children.Add(use); }
   if (!buttons.Children.Contains(use)) migrate.IsDefault=true;
   var modal=new ModalWindow(this,"切换配置目录",body,buttons,560,240,true);
   use.Click+=delegate { copy=false; modal.DialogResult=true; };
   migrate.Click+=delegate { copy=true; modal.DialogResult=true; };
   return modal.ShowDialog()==true ? copy : null;
  }
  void ReloadProfile(ProfileLocation state) {
   profileLocation=state; settings=AppSettings.Load();
   RebuildSettingsCards(); ReloadSources();
   DisplayAge(settings.Days);
   sizeBox.SelectedIndex=Math.Max(0,Array.IndexOf(SizeLimits,settings.MinBytes));
   dateBox.SelectedIndex=settings.UseCreated ? 1 : 0;
  }
  Button FolderButton(string label,Action action) {
   var button=Ui.Button(label,action,null);
   var content=new StackPanel { Orientation=Orientation.Horizontal };
   content.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse("M1,3 L6,3 8,5 15,5 15,13 1,13 Z"),
    Stroke=Ui.Accent,StrokeThickness=1.3,StrokeLineJoin=PenLineJoin.Round,Width=16,Height=14,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,0) });
   content.Children.Add(Ui.Label(label,12,Ui.Ink,true)); button.Content=content;
   AutomationProperties.SetName(button,label);
   return button;
  }

  Button addImageKey;
  FrameworkElement BuildImageKeySettings() {
   var panel=new StackPanel();
   var head=new DockPanel();
   addImageKey=Ui.Button("＋ 添加密钥对",AddImageKey,"PrimaryButton"); addImageKey.IsEnabled=false;
   addImageKey.Margin=new Thickness(12,0,0,0); AutomationProperties.SetName(addImageKey,"添加密钥对");
   DockPanel.SetDock(addImageKey,Dock.Right); head.Children.Add(addImageKey);
   var heading=new StackPanel(); heading.Children.Add(Ui.Label("新版微信图片密钥",15,Ui.Ink,true));
   var description=Ui.Label("AES 密钥和 XOR 值按账号目录验证和保存。",12,Ui.Muted,false);
   description.Margin=new Thickness(0,4,0,0); heading.Children.Add(description);
   head.Children.Add(heading); panel.Children.Add(head);
   imageKeyRows=new StackPanel { Margin=new Thickness(0,12,0,0) };
   AutomationProperties.SetName(imageKeyRows,"新版微信图片密钥账号列表"); panel.Children.Add(imageKeyRows);
   var privacy=Ui.Label("密钥由当前 Windows 用户加密保存。移除数据源后仍可管理；更换电脑或 Windows 用户后需重新录入。",11.5,Ui.Muted,false);
   privacy.Margin=new Thickness(0,8,0,0); panel.Children.Add(privacy);
   return panel;
  }
  void RefreshImageKeySettings() {
   if (imageKeyRows==null || settingsPage==null || settingsPage.Visibility!=Visibility.Visible) return;
   imageKeyRows.Children.Clear(); addImageKey.IsEnabled=false;
   if (profileLocation.Problem!=null) { imageKeyRows.Children.Add(Ui.Label("配置目录不可用，无法读取密钥记录。",12,Ui.Danger,false)); return; }
   if (discovering) { imageKeyRows.Children.Add(Ui.Label("正在识别当前账号…",12,Ui.Muted,false)); return; }
   try {
    var rows=AttachmentKeys.Rows(accounts);
    addImageKey.IsEnabled=rows.Any(r=>r.Account!=null && r.Saved==null);
    addImageKey.ToolTip=addImageKey.IsEnabled ? "为新版微信账号配置 AES 密钥和 XOR 值" : "没有尚未配置密钥的已加载账号";
    var list=new StackPanel();
    var columns=KeyColumns(); columns.Margin=new Thickness(12,0,12,0); columns.MinHeight=31;
    Cell(columns,0,Ui.Label("账号 / 数据目录",11.5,Ui.Muted,true));
    Cell(columns,1,Ui.Label("验证状态",11.5,Ui.Muted,true));
    var action=Ui.Label("操作",11.5,Ui.Muted,true); action.HorizontalAlignment=HorizontalAlignment.Right;
    Cell(columns,2,action);
    list.Children.Add(new Border { Background=Ui.Canvas,Child=columns });
    KeyGroup(list,"当前已加载",rows.Where(r=>r.Account!=null),"没有加载新版微信账号。");
    var history=rows.Where(r=>r.Account==null).ToList();
    if (history.Count!=0) KeyGroup(list,"来源未加载",history,null);
    var frame=new Border { BorderBrush=Ui.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(9),Child=list };
    Ui.ClipCorners(list,8); imageKeyRows.Children.Add(frame);
   } catch (Exception ex) {
    if (!ProfileStorage.Expected(ex) && !(ex is FormatException) && !(ex is InvalidDataException)) throw;
    imageKeyRows.Children.Add(Ui.Label("无法读取图片密钥记录。原记录已保留，请检查配置目录。",12,Ui.Danger,false));
   }
  }
  void AddImageKey() {
   List<ImageKeyRow> pending;
   try { pending=AttachmentKeys.Rows(accounts).Where(r=>r.Account!=null && r.Saved==null).ToList(); }
   catch (Exception ex) { if (!ProfileStorage.Expected(ex) && !(ex is FormatException) && !(ex is InvalidDataException)) throw; RefreshImageKeySettings(); return; }
   if (pending.Count==0) return;
   if (pending.Count==1) { EditImageKey(pending[0],false); return; }
   var box=new ComboBox { Margin=new Thickness(0,7,0,0),HorizontalContentAlignment=HorizontalAlignment.Left };
   AutomationProperties.SetName(box,"配置密钥的账号");
   foreach (var r in pending) box.Items.Add(new ComboBoxItem { Content=r.Title+"  ·  "+r.Account.Path,ToolTip=r.Account.Path });
   box.SelectedIndex=0;
   var body=new StackPanel(); body.Children.Add(Ui.Label("选择要配置的新版微信账号",13,Ui.Ink,true)); body.Children.Add(box);
   var cancel=Ui.Button("取消",null,null); cancel.IsCancel=true;
   var next=Ui.Button("继续",null,"PrimaryButton"); next.IsDefault=true;
   var dialog=new ModalWindow(this,"选择账号",body,Ui.ButtonRow(cancel,next),610,220,true);
   next.Click+=delegate { dialog.DialogResult=true; };
   if (dialog.ShowDialog()==true) EditImageKey(pending[box.SelectedIndex],false);
  }
  static Grid KeyColumns() {
   var g=new Grid();
   g.ColumnDefinitions.Add(new ColumnDefinition());
   g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(148) });
   g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(163) });
   return g;
  }
  static void Cell(Grid grid,int column,FrameworkElement value) { Grid.SetColumn(value,column); grid.Children.Add(value); }
  void KeyGroup(StackPanel list,string title,IEnumerable<ImageKeyRow> rows,string empty) {
   var heading=Ui.Label(title,11,Ui.Muted,true);
   list.Children.Add(new Border { BorderBrush=Ui.Line,BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(12,5,12,5),Child=heading });
   bool any=false;
   foreach (var row in rows) { list.Children.Add(KeyAccountRow(row)); any=true; }
   if (!any && empty!=null) {
    var label=Ui.Label(empty,12,Ui.Muted,false); label.Margin=new Thickness(12,9,12,9); list.Children.Add(label);
   }
  }
  static string KeyState(ImageKeyRow row) {
   if (row.Saved==null) return row.Relocation==null ? "未配置" : "待验证";
   if (!row.CanUnlock) return "无法读取";
   switch (row.Saved.Check) {
    case ImageKeyCheck.Verified: return row.Saved.Proof==ImageKeyProof.Pair ? "验证通过" : "AES 已验证";
    case ImageKeyCheck.NoSample: return "暂无可验样本";
    case ImageKeyCheck.Failed: return "验证失败";
    default: return "待验证";
   }
  }
  static string KeyStateDetail(ImageKeyRow row) {
   if (row.Saved==null) return row.Relocation==null ? null : "可关联历史密钥";
   if (!row.CanUnlock) return "请重新录入";
   if (row.Saved.Check==ImageKeyCheck.NoSample && row.Saved.Proof!=ImageKeyProof.None)
    return row.Saved.Proof==ImageKeyProof.Pair ? "上次验证通过" : "上次 AES 已验证";
   if (row.Saved.Check==ImageKeyCheck.Verified && row.Saved.Proof==ImageKeyProof.Aes)
    return row.HasTail ? "XOR 待验证" : "未填写 XOR";
   return null;
  }
  FrameworkElement KeyAccountRow(ImageKeyRow row) {
   bool loaded=row.Account!=null;
   var g=KeyColumns(); g.MinHeight=49; g.Margin=new Thickness(12,2,12,2);
   var identity=new StackPanel { VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0) };
   var name=Ui.Label(row.Title,12.5,Ui.Ink,true);
   name.TextWrapping=TextWrapping.NoWrap; name.TextTrimming=TextTrimming.CharacterEllipsis; name.ToolTip=row.Title;
   identity.Children.Add(name);
   string root=loaded ? row.Account.Path : row.Saved.AccountRoot;
   var path=Ui.Label(root??("旧版记录 · "+row.Saved.Slot.Substring(0,8)),11,Ui.Muted,false);
   path.Margin=new Thickness(0,3,0,0); path.TextWrapping=TextWrapping.NoWrap; path.TextTrimming=TextTrimming.CharacterEllipsis;
   path.ToolTip=root??"重新添加原目录后可补全这条记录的账号目录。"; identity.Children.Add(path);
   Cell(g,0,identity);

   string state=KeyState(row), detail=KeyStateDetail(row);
   bool ok=row.Saved!=null && row.CanUnlock && row.Saved.Check==ImageKeyCheck.Verified;
   bool failed=row.Saved!=null && (!row.CanUnlock || row.Saved.Check==ImageKeyCheck.Failed);
   bool waiting=!ok && !failed && (row.Relocation!=null || row.Saved!=null);
   var badge=new Border { CornerRadius=new CornerRadius(9),Padding=new Thickness(7,2,7,3),HorizontalAlignment=HorizontalAlignment.Left,
    VerticalAlignment=VerticalAlignment.Center,Background=ok ? Ui.AccentSoft : Ui.Brush(failed ? "#FCF0ED" : waiting ? "#FCF3DF" : "#F0F4F2") };
   badge.Child=Ui.Label(state,11,ok ? Ui.Accent : failed ? Ui.Danger : waiting ? Ui.Brush("#865A1E") : Ui.Muted,true);
   AutomationProperties.SetName(badge,state);
   if (row.Saved!=null && row.Saved.CheckedUtc.Ticks!=0) badge.ToolTip="上次检查："+row.Saved.CheckedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
   var stateColumn=new StackPanel { VerticalAlignment=VerticalAlignment.Center }; stateColumn.Children.Add(badge);
   if (detail!=null) { var secondary=Ui.Label(detail,10.5,Ui.Muted,false); secondary.Margin=new Thickness(1,2,0,0); stateColumn.Children.Add(secondary); }
   Cell(g,1,stateColumn);

   var actions=new StackPanel { Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center };
   if (loaded) {
    if (row.Relocation!=null) actions.Children.Add(Ui.ActionButton("关联旧记录","先验证新目录的样本，旧目录的记录仍保留。",delegate { EditImageKey(row,true); }));
    actions.Children.Add(Ui.ActionButton(row.Saved==null ? "配置" : "编辑","编辑此账号的 AES 密钥和 XOR 值",delegate { EditImageKey(row,false); }));
    if (row.Saved!=null && row.CanUnlock) actions.Children.Add(Ui.ActionButton("重新验证","用当前目录中的样本检查已保存的值",delegate { RecheckImageKey(row); }));
   } else actions.Children.Add(Ui.ActionButton("查看记录","查看历史目录及上次验证状态",delegate { ViewImageKey(row); }));
   if (row.Saved!=null) {
    var forget=Ui.ActionButton("移除","从配置中移除 AES 密钥和 XOR 值",delegate { ConfirmForgetKey(row); });
    forget.Foreground=Ui.Muted; AutomationProperties.SetName(forget,"移除密钥对"); actions.Children.Add(forget);
   }
   Cell(g,2,actions);
   return new Border { BorderBrush=Ui.Line,BorderThickness=new Thickness(0,1,0,0),Child=g };
  }
  void ConfirmForgetKey(ImageKeyRow row) {
   var body=new StackPanel(); body.Children.Add(Ui.Label("移除「"+row.Title+"」的 AES 密钥和 XOR 值？",13,Ui.Ink,true));
   var caption=Ui.Label("只会移除本程序中的密钥记录。微信原文件保持不变。",12,Ui.Muted,false);
   caption.Margin=new Thickness(0,6,0,0); body.Children.Add(caption);
   var cancel=Ui.Button("取消",null,null); cancel.IsCancel=true;
   var remove=Ui.Button("移除密钥对",null,"DangerButton");
   var dialog=new ModalWindow(this,"移除密钥对",body,Ui.ButtonRow(cancel,remove),525,205,true);
   remove.Click+=delegate { dialog.DialogResult=true; };
   if (dialog.ShowDialog()!=true) return;
   Toast(AttachmentKeys.Remove(row.Saved) ? "已移除此账号的图片密钥。" : "无法移除此密钥，请检查配置目录后重试。");
   RefreshImageKeySettings();
  }
  void ViewImageKey(ImageKeyRow row) {
   using (var key=AttachmentKeys.Unlock(row.Saved)) {
    var dialog=new ImageKeyDialog(this,"历史密钥记录",row.Title,row.Saved.AccountRoot,null,
     key==null ? "当前 Windows 用户无法读取保存的值，重新加载此目录后可重新录入。" :
     "数据源未加载。添加原目录后可继续使用此记录。",key,null);
    string detail=KeyStateDetail(row);
    var status=Ui.Label("上次验证结果："+KeyState(row)+(detail==null ? "" : " · "+detail),12,Ui.Muted,true);
    status.Margin=new Thickness(0,11,0,0); dialog.Extra.Children.Add(status);
    dialog.ShowDialog();
   }
  }
  void RecheckImageKey(ImageKeyRow row) {
   using (var key=AttachmentKeys.Unlock(row.Saved)) {
    if (key==null) { Toast("无法读取已保存的值，请重新录入。"); RefreshImageKeySettings(); return; }
    ImageKeyCheck check; ImageKeyProof proof=ImageKeyProof.None; string message;
    try {
     proof=Ui.Progress(this,"重新验证图片密钥","正在检查此账号的 V2 附件…",()=>AttachmentMedia.VerifyImageKey(row.Account,key,null),false);
     bool tailPending=key.Tail.HasValue && proof!=ImageKeyProof.Pair;
     check=tailPending ? ImageKeyCheck.NoSample : ImageKeyCheck.Verified;
     message=tailPending ? "AES 已通过；暂未找到能验证 XOR 的样本。" : proof==ImageKeyProof.Pair ? "AES 密钥和 XOR 值均通过验证。" : "AES 密钥已通过验证。";
    } catch (InvalidOperationException ex) {
     check=ex.InnerException is ImageKeySampleMissing ? ImageKeyCheck.NoSample : ImageKeyCheck.Failed;
     message=ex.Message;
    }
    Toast(AttachmentKeys.NoteCheck(row.Saved,check,proof) ? message : "无法保存验证状态。原密钥记录已保留。");
    RefreshImageKeySettings();
   }
  }
  void EditImageKey(ImageKeyRow row,bool useHistory) {
   var account=row.Account;
   if (account==null) return;
   var record=useHistory ? row.Relocation : row.Saved;
   using (var initial=record==null ? null : AttachmentKeys.Unlock(record)) {
    if (useHistory && initial==null) { Toast("无法读取历史密钥，请为当前目录重新录入。"); return; }
    string title=useHistory ? "关联历史密钥" : row.Saved==null ? "添加图片密钥" : "编辑图片密钥";
    string help=useHistory ? "使用新目录的图片验证，原目录中的密钥记录仍会保留。" :
     initial!=null && initial.Tail.HasValue ? "清空 XOR 值后验证并保存，可只保留 AES。" : "使用这个账号的图片验证。只需 AES 的图片可以将 XOR 留空。";
    var dialog=new ImageKeyDialog(this,title,row.Title,account.Path,null,help,initial,useHistory ? "验证并关联" : "验证并保存");
    string attach=Path.Combine(account.Path,@"msg\attach"),sample=null;
    dialog.Extra.Children.Add(FieldLabel("验证样本（可选）"));
    var field=new TextBox { IsReadOnly=true,Text="自动寻找此账号的加密图片" };
    AutomationProperties.SetName(field,"验证样本");
    var sampleGrid=new Grid(); sampleGrid.ColumnDefinitions.Add(new ColumnDefinition()); sampleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
    sampleGrid.Children.Add(field);
    var browse=FolderButton("选择样本…",delegate {
     var picker=new Microsoft.Win32.OpenFileDialog { Title="选择该账号的加密图片",CheckFileExists=true,
      Filter="微信附件 (*.dat)|*.dat|所有文件 (*.*)|*.*",InitialDirectory=Directory.Exists(attach) ? attach : account.Path };
     try {
      if (picker.ShowDialog(dialog)!=true) return;
      if (!Engine.IsInside(attach,picker.FileName)) { dialog.Report("请选择此账号 msg\\attach 目录下的图片。",true); return; }
      sample=picker.FileName; field.Text=Path.GetFileName(sample); field.ToolTip=sample;
      dialog.SavePending.Visibility=Visibility.Collapsed; dialog.Report(null,false);
     } catch (Exception ex) { if (!ProfileStorage.Expected(ex) && !(ex is System.ComponentModel.Win32Exception)) throw; dialog.Report("无法打开样本选择器。",true); }
    });
    browse.Margin=new Thickness(8,0,0,0); Cell(sampleGrid,1,browse); dialog.Extra.Children.Add(sampleGrid);
    bool mayWait=!useHistory && (row.Saved==null || row.Saved.Proof==ImageKeyProof.None);
    string completion=null;
    dialog.Confirm.Click+=async delegate {
     if (!dialog.Confirm.IsEnabled) return;
     using (var key=dialog.ReadKey()) {
      if (key==null) return;
      dialog.SavePending.Visibility=Visibility.Collapsed; dialog.SetBusy(true);
      try {
       var proof=await Task.Run(()=>AttachmentMedia.VerifyImageKey(account,key,sample));
       if (!dialog.IsVisible) return;
       if (key.Tail.HasValue && proof!=ImageKeyProof.Pair) {
        dialog.Report("AES 已通过。这张样本无法验证 XOR 值。请选择另一张图片，或清空 XOR 后验证，仅保存 AES。",true);
        return;
       }
       if (!AttachmentKeys.Remember(account.Path,key,account.DisplayName,proof,useHistory ? record : null)) {
        dialog.Report("无法保存密钥。请检查配置目录，原记录已保留。",true); return;
       }
       completion=useHistory ? "已关联到新目录，旧记录仍保留。" : proof==ImageKeyProof.Pair ? "密钥对已验证并保存。" : "AES 密钥已验证并保存。";
       dialog.DialogResult=true;
      } catch (ImageKeySampleMissing ex) {
       if (dialog.IsVisible) {
        dialog.Report(ex.Message+(mayWait ? " 可先保存为待验证。" : " 原密钥记录仍保留。"),false);
        dialog.SavePending.Visibility=mayWait ? Visibility.Visible : Visibility.Collapsed;
       }
      } catch (AttachmentKeyNeeded ex) { if (dialog.IsVisible) dialog.Report(ex.Message,true,ex.NeedsAes); }
      catch (Exception ex) { if (dialog.IsVisible) dialog.Report(ex.Message,true); }
      finally { if (dialog.IsVisible) dialog.SetBusy(false); }
     }
    };
    dialog.SavePending.Click+=delegate {
     if (!mayWait || !dialog.SavePending.IsEnabled) return;
     using (var key=dialog.ReadKey()) {
      if (key==null) return;
      if (!AttachmentKeys.Remember(account.Path,key,account.DisplayName,ImageKeyProof.None)) { dialog.Report("无法保存待验证的密钥，原记录已保留。",true); return; }
      completion="已保存为待验证。找到可用图片后可重新验证。"; dialog.DialogResult=true;
     }
    };
    if (dialog.ShowDialog()==true) { Toast(completion); RefreshImageKeySettings(); }
   }
  }
 }
}
