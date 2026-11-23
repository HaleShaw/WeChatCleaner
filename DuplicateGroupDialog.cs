using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace WeChatCleaner {
 internal sealed class DuplicateGroupDialog : ModalWindow {
  readonly DuplicateGroup group;
  readonly bool permanent;
  readonly string ffmpegPath;
  readonly Func<Entry,string> ownerName;
  readonly Action<Engine.RecycleReport> cleanup;
  readonly TableView table;
  readonly CheckBox selectAll;
  readonly Button clean;
  readonly TextBlock hint;
  List<FileChoice> rows=new List<FileChoice>();
  bool batch;

  internal DuplicateGroupDialog(Window owner,DuplicateGroup group,bool permanent,Func<Entry,string> ownerName,Action<Engine.RecycleReport> cleanUp,string ffmpegPath=null)
   : this(owner,group,permanent,ownerName,cleanUp,ffmpegPath,new Grid(),new Grid()) { }
  DuplicateGroupDialog(Window owner,DuplicateGroup group,bool permanent,Func<Entry,string> ownerName,Action<Engine.RecycleReport> cleanUp,string ffmpegPath,Grid body,Grid foot)
   : base(owner,"重复文件分组",body,foot,1160,590,true,true,false) {
   Fit(owner,1160,590,825,395); AllowResize();
   this.group=group; this.permanent=permanent; this.ownerName=ownerName; cleanup=cleanUp; this.ffmpegPath=ffmpegPath;
   selectAll=Ui.CenterCheck("选择或取消选择本组所有文件");
   selectAll.Click+=delegate {
    bool enable=rows.Any(r=>!r.Included); batch=true;
    foreach (var r in rows) r.Included=enable;
    batch=false; UpdateSelection();
   };
   table=new TableView(39,
    new TableColumn(48,"序号",o=>Text(((FileChoice)o).Number.ToString(),TextAlignment.Center,0,false)),
    new TableColumn(48,selectAll,o=>((FileChoice)o).Check(true,"选择 ")),
    new TableColumn(-1,"所在账号",o=>Text(this.ownerName(((FileChoice)o).File),TextAlignment.Left,12,true)),
    new TableColumn(-1.75,"文件名",o=>Text(((FileChoice)o).File.Name,TextAlignment.Left,12,true)),
    new TableColumn(93,"大小",o=>Text(Engine.Size(((FileChoice)o).File.Size),TextAlignment.Right,12,false)),
    new TableColumn(153,"创建时间",o=>Text(((FileChoice)o).File.Created.ToString("yyyy-MM-dd HH:mm:ss"),TextAlignment.Center,5,false)),
    new TableColumn(166,"操作",o=>Actions(((FileChoice)o).File)));
   AutomationProperties.SetName(table,"重复文件分组表格"); body.Children.Add(table);

   foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   hint=Ui.Label("",11.5,Ui.Muted,false); hint.Margin=new Thickness(0,0,16,0); foot.Children.Add(hint);
   var close=Ui.Button("关闭",null,null); close.IsCancel=true;
   clean=Ui.Button(permanent?"永久删除":"移至回收站",delegate { Run(rows.Where(r=>r.Included).Select(r=>r.File)); },"DangerButton");
   var buttons=Ui.ButtonRow(close,clean); Grid.SetColumn(buttons,1); foot.Children.Add(buttons);
   Reload(true);
  }
  static FrameworkElement Text(string value,TextAlignment align,double pad,bool strong) {
   var t=TableView.Text(value,align,strong,pad,strong?Ui.Ink:Ui.Muted); if (strong) t.ToolTip=value; return t;
  }
  FrameworkElement Actions(Entry file) {
   var panel=new StackPanel { Orientation=Orientation.Horizontal, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center };
   panel.Children.Add(Ui.ActionButton("查看","识别图片或语音后打开",delegate { FileActions.Open(this,file,ffmpegPath,ownerName(file)); }));
   panel.Children.Add(Ui.ActionButton("另存为","按识别出的格式另存",delegate { FileActions.Save(this,file,Notice,ffmpegPath,ownerName(file)); }));
   var del=Ui.ActionButton("删除",permanent?"永久删除":"移至回收站",delegate { Run(new[]{file}); });
   del.Foreground=Ui.Danger; panel.Children.Add(del); return panel;
  }
  void Reload(bool reset) {
   var keep=new HashSet<string>(rows.Where(r=>r.Included).Select(r=>r.File.Path),StringComparer.OrdinalIgnoreCase);
   rows=group.Files.OrderBy(f=>f.Created).ThenBy(f=>f.Path,StringComparer.OrdinalIgnoreCase).Select((f,i)=>new FileChoice { File=f,Number=i+1,Included=keep.Contains(f.Path) }).ToList();
   foreach (var row in rows) row.PropertyChanged += delegate { if (!batch) UpdateSelection(); };
   SetSubtitle("共 " + rows.Count + " 份 · " + Engine.Size(group.Size) + " / 份 · 按创建时间从早到晚");
   table.SetRows(rows,"此分组已没有文件",reset); UpdateSelection();
  }
  void UpdateSelection() {
   int count=rows.Count(r=>r.Included);
   selectAll.IsEnabled=rows.Count>0;
   selectAll.IsChecked=count==0 ? (bool?)false : count==rows.Count ? true : (bool?)null;
   clean.IsEnabled=count>0;
   hint.Foreground=count>0&&count==rows.Count ? Ui.Danger : Ui.Muted;
   hint.Text=count==0 ? "内容相同不代表应用中的引用可互换，请自行确认保留的副本。" :
    "已选 " + count + " 份 · " + Engine.Size(rows.Where(r=>r.Included).Sum(r=>r.File.Size)) + (count==rows.Count ? " · 已选中全部副本" : "");
  }
  void Run(IEnumerable<Entry> files) {
   var report=CleanupUi.Run(this,files,permanent,delegate(Engine.RecycleReport result) {
    if (result.Removed.Count==0) return;
    var removed=new HashSet<string>(result.Removed.Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
    if (cleanup!=null) cleanup(result);
    group.Files.RemoveAll(f=>removed.Contains(f.Path));
    Reload(false);
   });
   if (report!=null) Notice(report.Summary);
  }
 }
}
