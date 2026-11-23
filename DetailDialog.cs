using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace WeChatCleaner {
 public sealed class DetailDialog : ModalWindow {
  const string AllTypes = "全部类型";
  const string Custom = "自定义…";
  const string NoExtension = "无扩展名";
  readonly Category category;
  readonly bool permanent;
  readonly string ffmpegPath, accountName;
  readonly Action<int> rememberPageSize;
  readonly Action<IEnumerable<string>> refresh;
  readonly Action<Engine.RecycleReport> cleanup;
  readonly TableView table;
  readonly Dictionary<Entry,FileChoice> choices = new Dictionary<Entry,FileChoice>();
  readonly Dictionary<string,TextBlock> marks = new Dictionary<string,TextBlock>();
  List<FileChoice> filtered = new List<FileChoice>();
  List<FileChoice> pageRows = new List<FileChoice>();
  readonly ComboBox typeBox = new ComboBox();
  readonly ComboBox pageBox = new ComboBox();
  readonly TextBox jumpBox = new TextBox();
  readonly StackPanel numbered = new StackPanel { Orientation = Orientation.Horizontal };
  readonly CheckBox head;
  readonly Button previous, next, pick, export, reload;
  readonly TextBlock total, count;
  int page, pageSize;
  string sortKey = "name";
  bool descending, updatingTypes, updatingSize, batching, closed;

  public DetailDialog(Window owner, Account account, Category category, int pageSize, bool permanent)
   : this(owner,account,category,pageSize,permanent,null,null,null) { }
  internal DetailDialog(Window owner, Account account, Category category, int pageSize, bool permanent,
    Action<int> remember, Action<IEnumerable<string>> refresh, Action<Engine.RecycleReport> cleanup, string ffmpegPath=null)
   : this(owner,account,category,pageSize,permanent,remember,refresh,cleanup,ffmpegPath,new Grid(),new StackPanel { Orientation = Orientation.Horizontal }) { }
  DetailDialog(Window owner, Account account, Category category, int pageSize, bool permanent,
    Action<int> remember, Action<IEnumerable<string>> refresh, Action<Engine.RecycleReport> cleanup, string ffmpegPath, Grid body, StackPanel footer)
   : base(owner,category.Name + " · 文件明细",body,footer,1500,800,true,false,false) {
   Fit(owner,1500,800,820,455); AllowResize();
   this.category = category; this.permanent = permanent; this.pageSize = Math.Max(1,Math.Min(5000,pageSize));
   this.rememberPageSize = remember; this.refresh = refresh; this.cleanup = cleanup; this.ffmpegPath=ffmpegPath; accountName=account.DisplayName;
   SetSubtitle(account.Group + " · " + account.DisplayName + "   /   " + category.RelativePath);
   body.RowDefinitions.Add(new RowDefinition());
   body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

   head = Ui.CenterCheck("纳入：选择当前类型的全部分页");
   head.Click += delegate { SetSelection(filtered,filtered.Any(r=>!r.Included)); };
   typeBox.Width = 117; typeBox.Height = 30; typeBox.MinHeight = 0; typeBox.FontSize = 12;
   typeBox.HorizontalAlignment = HorizontalAlignment.Center; typeBox.VerticalAlignment = VerticalAlignment.Center;
   typeBox.ToolTip = "按文件类型筛选"; AutomationProperties.SetName(typeBox,"文件类型");
   typeBox.SelectionChanged += delegate { if (!updatingTypes) { page = 0; Rebuild(true); } };
   table = new TableView(36,3,1,
    new TableColumn(45,"序号",o=>Cell(((FileChoice)o).Number.ToString(),TextAlignment.Center,false,0)),
    new TableColumn(46,head,o=>((FileChoice)o).Check(!category.Protected,"纳入 ")),
    new TableColumn(252,Header("文件名","name"),o=>LongText(((FileChoice)o).File.Name,12)),
    new TableColumn(130,typeBox,o=>Cell(TypeOf(((FileChoice)o).File),TextAlignment.Center,false,6)),
    new TableColumn(100,Header("大小","size"),o=>Cell(Engine.Size(((FileChoice)o).File.Size),TextAlignment.Right,false,14)),
    new TableColumn(154,Header("创建时间","created"),o=>Cell(Stamp(((FileChoice)o).File.Created),TextAlignment.Center,false,8)),
    new TableColumn(154,Header("修改时间","modified"),o=>Cell(Stamp(((FileChoice)o).File.Modified),TextAlignment.Center,false,8)),
    new TableColumn(754,"完整路径",o=>LongText(((FileChoice)o).File.Path,12)),
    new TableColumn(category.Protected ? 202 : 244,"操作",o=>Actions(((FileChoice)o).File)));
   AutomationProperties.SetName(table,"文件明细表格");
   body.Children.Add(table);

   var controls = new Grid { Margin = new Thickness(0,10,0,0) };
   controls.ColumnDefinitions.Add(new ColumnDefinition()); controls.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); controls.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
   reload = Ui.Button("⟳",RefreshFiles,"GhostButton"); reload.Width = 31; reload.Padding = new Thickness(1);
   reload.FontSize = 16; reload.ToolTip = "刷新文件信息"; AutomationProperties.SetName(reload,"刷新文件信息");
   pick = Ui.MenuButton("选择",Ui.MenuItem("全部分页 · 全选",delegate { SetSelection(filtered,true); }),
    Ui.MenuItem("全部分页 · 取消选择",delegate { SetSelection(filtered,false); }),new Separator(),
    Ui.MenuItem("当前页 · 反选",delegate { InvertPage(); }));
   pick.ToolTip = "全选或取消当前类型下的全部分页";
   total = Ui.Label("",12,Ui.Muted,false); total.Margin = new Thickness(10,0,10,0);
   export = Ui.MenuButton("导出",Ui.MenuItem("导出 Excel (.xlsx)",delegate { Export(true); }),Ui.MenuItem("导出 CSV (.csv)",delegate { Export(false); }));
   var start = Ui.ButtonRow(reload,pick);
   start.Children.Add(total); start.Children.Add(export); controls.Children.Add(start);
   pick.Visibility = category.Protected ? Visibility.Collapsed : Visibility.Visible;
   export.Visibility = pick.Visibility;

   var pager = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
   count = Ui.Label("",11.5,Ui.Muted,false); count.Margin = new Thickness(0,0,9,0); pager.Children.Add(count);
   previous = PageButton("‹",delegate { Go(page-1); },false); previous.ToolTip = "上一页"; AutomationProperties.SetName(previous,"上一页");
   next = PageButton("›",delegate { Go(page+1); },false); next.ToolTip = "下一页"; AutomationProperties.SetName(next,"下一页");
   pager.Children.Add(previous); pager.Children.Add(numbered); pager.Children.Add(next);

   pageBox.IsEditable = true; pageBox.IsTextSearchEnabled = false; pageBox.Width = 124; pageBox.Height = 32;
   pageBox.Margin = new Thickness(4,0,0,0); pageBox.ToolTip = "每页条数，支持 1 到 5000 条";
   AutomationProperties.SetName(pageBox,"每页条数");
   foreach (int n in new[]{25,50,100,200}) pageBox.Items.Add(SizeLabel(n));
   pageBox.Items.Add(Custom);
   DisplayPageSize();
   pageBox.SelectionChanged += delegate {
    if (updatingSize) return;
    var value = pageBox.SelectedItem as string;
    if (value == Custom) { Ui.FocusEdit(pageBox); return; }
    int n;
    if (ParseSize(value,out n)) ApplyPageSize(n);
   };
   Ui.ApplyOnExit(pageBox,CommitPageSize);
   pager.Children.Add(pageBox);
   var go = Ui.Label("前往",11.5,Ui.Muted,false); go.Margin = new Thickness(10,0,4,0); pager.Children.Add(go);
   jumpBox.Width = 52; jumpBox.Height = 32; jumpBox.MinHeight = 0; jumpBox.Padding = new Thickness(3,2,3,2);
   jumpBox.TextAlignment = TextAlignment.Center; jumpBox.VerticalAlignment = VerticalAlignment.Center;
   jumpBox.ToolTip = "输入页码后按回车"; AutomationProperties.SetName(jumpBox,"前往页码");
   jumpBox.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
    if (e.Key != Key.Enter) return;
    int n;
    if (Int32.TryParse(jumpBox.Text,out n) && n>=1 && n<=Pages) Go(n-1);
    else { jumpBox.Text = (page+1).ToString(); Notice("请输入 1 到 " + Pages + " 之间的页码"); }
    e.Handled = true;
   };
   pager.Children.Add(jumpBox);
   var suffix = Ui.Label("页",11.5,Ui.Muted,false); suffix.Margin = new Thickness(4,0,0,0); pager.Children.Add(suffix);
   Grid.SetColumn(pager,1); controls.Children.Add(pager);
   controls.SizeChanged += delegate {
    bool compact = controls.ActualWidth < 880;
    Grid.SetRow(pager,compact ? 1 : 0); Grid.SetColumn(pager,compact ? 0 : 1);
    Grid.SetColumnSpan(pager,compact ? 2 : 1);
    pager.Margin = compact ? new Thickness(0,5,0,0) : new Thickness(10,0,0,0);
   };
   Grid.SetRow(controls,1); body.Children.Add(controls);

   var cancel = Ui.Button(category.Protected ? "关闭" : "取消",null,null); cancel.IsCancel = true;
   footer.Children.Add(cancel);
   if (!category.Protected) {
    var ok = Ui.Button("确定",Commit,"PrimaryButton"); ok.Margin = new Thickness(8,0,0,0); footer.Children.Add(ok);
   }
   Closed += delegate { closed=true; };
   ResetTypes(); Rebuild(true);
  }
  static string SizeLabel(int n) { return n + " 条 / 页"; }
  static bool ParseSize(string value, out int n) {
   return Int32.TryParse((value??"").Replace("条 / 页","").Trim(),out n) && n>=1 && n<=5000;
  }
  void DisplayPageSize() {
   updatingSize = true;
   string label = SizeLabel(pageSize);
   if (!pageBox.Items.Contains(label)) pageBox.Items.Insert(pageBox.Items.Count-1,label);
   pageBox.SelectedItem = label; pageBox.Text = label;
   updatingSize = false;
  }
  void CommitPageSize() {
   int n;
   string value=Ui.EditText(pageBox);
   if (ParseSize(value,out n)) ApplyPageSize(n);
   else { if (value != Custom) Notice("每页条数请输入 1 到 5000 之间的整数"); DisplayPageSize(); }
  }
  void ApplyPageSize(int n) {
   if (pageSize != n) { pageSize = n; page = 0; if (rememberPageSize != null) rememberPageSize(n); ShowPage(true); }
   DisplayPageSize();
  }
  static string Stamp(DateTime time) { return time.ToString("yyyy-MM-dd HH:mm:ss"); }
  static string TypeOf(Entry entry) { return String.IsNullOrEmpty(entry.Type) ? NoExtension : entry.Type.ToLowerInvariant(); }
  internal IEnumerable<Entry> VisibleCandidates { get { return category.Files.Where(f=>category.Protected || f.Matches); } }
  void ResetTypes() {
   string keep = typeBox.SelectedItem as string;
   updatingTypes = true; typeBox.Items.Clear(); typeBox.Items.Add(AllTypes);
   foreach (string ext in VisibleCandidates.Select(TypeOf).Distinct().OrderBy(x=>x,StringComparer.Ordinal)) typeBox.Items.Add(ext);
   typeBox.SelectedItem = keep != null && typeBox.Items.Contains(keep) ? keep : AllTypes;
   updatingTypes = false;
  }
  FrameworkElement Header(string text, string key) {
   TextBlock mark;
   var button = TableView.SortHeader(text,delegate {
    if (sortKey == key) descending = !descending; else { sortKey = key; descending = false; }
    page = 0; Rebuild(true);
   },out mark);
   marks[key] = mark; return button;
  }
  FrameworkElement Cell(string value, TextAlignment alignment, bool strong, double pad) {
   return TableView.Text(value,alignment,strong,pad,Ui.Muted);
  }
  FrameworkElement LongText(string value, double pad) {
   var t = TableView.Text(value,TextAlignment.Left,false,pad,Ui.Ink); t.ToolTip = value; return t;
  }
  FrameworkElement Actions(Entry file) {
   var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
   panel.Children.Add(Ui.ActionButton("查看","识别图片或语音后打开",delegate { FileActions.Open(this,file,ffmpegPath,accountName); }));
   panel.Children.Add(Ui.ActionButton("打开路径","在资源管理器中定位",delegate { FileActions.Reveal(this,file); }));
   panel.Children.Add(Ui.ActionButton("复制路径","复制完整路径",delegate { FileActions.Copy(this,file,Notice); }));
   panel.Children.Add(Ui.ActionButton("另存为","按识别出的格式另存",delegate { FileActions.Save(this,file,Notice,ffmpegPath,accountName); }));
   if (!category.Protected) {
    var delete = Ui.ActionButton("删除",permanent ? "永久删除" : "移至回收站",delegate { Delete(file); });
    delete.Foreground = Ui.Danger; panel.Children.Add(delete);
   }
   return panel;
  }
  void Rebuild(bool resetScroll) {
   string wanted = typeBox.SelectedItem as string;
   filtered = new List<FileChoice>();
   foreach (var f in VisibleCandidates) {
    FileChoice c;
    if (!choices.TryGetValue(f,out c)) {
     c = new FileChoice { File = f, Included = !category.Protected && !f.Excluded };
     c.PropertyChanged += delegate { if (!batching) UpdateSelection(); }; choices.Add(f,c);
    }
    if (wanted == null || wanted == AllTypes || wanted == TypeOf(f)) filtered.Add(c);
   }
   filtered.Sort(delegate(FileChoice a,FileChoice b) {
    Entry x=a.File,y=b.File;
    int cmp = sortKey == "size" ? x.Size.CompareTo(y.Size) : sortKey == "created" ? x.Created.CompareTo(y.Created) :
     sortKey == "modified" ? x.Modified.CompareTo(y.Modified) : StringComparer.CurrentCultureIgnoreCase.Compare(x.Name,y.Name);
    return cmp == 0 ? StringComparer.OrdinalIgnoreCase.Compare(x.Path,y.Path) : (descending ? -cmp : cmp);
   });
   foreach (var m in marks) m.Value.Text = m.Key == sortKey ? (descending ? "▼" : "▲") : "";
   ShowPage(resetScroll);
  }
  int Pages { get { return Math.Max(1,(filtered.Count+pageSize-1)/pageSize); } }
  void Go(int n) { if (n>=0 && n<Pages && n!=page) { page=n; ShowPage(true); } else jumpBox.Text = (page+1).ToString(); }
  void ShowPage(bool resetScroll) {
   int pages = Pages; page = Math.Min(page,pages-1);
   pageRows = filtered.Skip(page*pageSize).Take(pageSize).ToList();
   for (int i=0; i<pageRows.Count; i++) pageRows[i].Number = page*pageSize+i+1;
   table.SetRows(pageRows,"当前条件下没有文件",resetScroll);
   previous.IsEnabled=page>0; next.IsEnabled=page<pages-1;
   count.Text = "共 " + filtered.Count + " 条"; jumpBox.Text = (page+1).ToString();
   numbered.Children.Clear();
   int[] visible = (pages<=5 ? Enumerable.Range(0,pages) : new[]{0,page-1,page,page+1,pages-1}.Where(x=>x>=0&&x<pages)).Distinct().OrderBy(x=>x).ToArray();
   int last = -1;
   foreach (int i in visible) {
    if (i>last+1) { var dots=Ui.Label("…",11,Ui.Muted,false); dots.Margin=new Thickness(0,0,4,0); numbered.Children.Add(dots); }
    int target=i;
    var b=PageButton((i+1).ToString(),delegate { Go(target); },i==page);
    AutomationProperties.SetName(b,"第 " + (i+1) + " 页"); numbered.Children.Add(b); last=i;
   }
   UpdateSelection();
  }
  static Button PageButton(string title, Action click, bool active) {
   var b=Ui.Button(title,click,active ? "PrimaryButton" : null);
   b.MinWidth=29; b.Height=32; b.MinHeight=0; b.Padding=new Thickness(5,0,5,0); b.Margin=new Thickness(0,0,4,0); return b;
  }
  void UpdateSelection() {
   var chosen=filtered.Where(r=>r.Included).ToList();
   total.Text=category.Protected ? "受保护 · 仅查看" : "已选择 " + chosen.Count + " 条 · " + Engine.Size(chosen.Sum(r=>r.File.Size));
   head.IsEnabled=!category.Protected&&filtered.Count>0;
   head.IsChecked=chosen.Count==0 ? (bool?)false : (chosen.Count==filtered.Count ? true : (bool?)null);
   pick.IsEnabled=head.IsEnabled; export.IsEnabled=!category.Protected&&chosen.Count>0;
  }
  void SetSelection(IEnumerable<FileChoice> scope, bool value) {
   if (category.Protected) return;
   batching=true; foreach (var c in scope) c.Included=value; batching=false; UpdateSelection();
  }
  void InvertPage() {
   batching=true; foreach (var c in pageRows) c.Included=!c.Included; batching=false; UpdateSelection();
  }
  void Commit() {
   foreach (var kv in choices) kv.Key.Excluded=!kv.Value.Included;
   DialogResult=true;
  }
  sealed class FileState {
   public Entry Entry;
   public bool Missing, Available;
   public long Size;
   public DateTime Created, Modified;
  }
  static FileState ReadMetadata(Entry entry) {
   var state=new FileState { Entry=entry };
   try {
    if (Engine.IsMissing(entry.Path)) { state.Missing=true; return state; }
    if (!Engine.LinkFree(entry.Path)) return state;
    var info=new FileInfo(entry.Path);
    if (!info.Exists) return state;
    state.Size=info.Length; state.Created=info.CreationTime; state.Modified=info.LastWriteTime; state.Available=true;
   } catch (IOException) { } catch (UnauthorizedAccessException) { }
   return state;
  }
  async void RefreshFiles() {
   if (!reload.IsEnabled) return;
   reload.IsEnabled=false;
   var snapshot=category.Files.ToArray();
   try {
    var states=await Task.Run(()=>snapshot.Select(ReadMetadata).ToArray());
    if (closed) return;
    var present=new HashSet<Entry>(category.Files);
    int read=0, inaccessible=0;
    var changed=new List<string>(); var missing=new HashSet<Entry>();
    foreach (var state in states) {
     var f=state.Entry;
     if (!present.Contains(f)) continue;
     if (state.Missing) { missing.Add(f); choices.Remove(f); changed.Add(f.Path); continue; }
     if (!state.Available) { inaccessible++; continue; }
     if (state.Size!=f.Size || state.Modified!=f.Modified || state.Created!=f.Created) changed.Add(f.Path);
     f.Size=state.Size; f.Created=state.Created; f.Modified=state.Modified; read++;
    }
    if (missing.Count>0) category.Files.RemoveAll(missing.Contains);
    if (refresh!=null) refresh(changed); else category.RefreshMatched();
    ResetTypes(); Rebuild(false);
    Notice("已刷新 " + read + " 个文件的信息" + (inaccessible==0 ? "" : " · " + inaccessible + " 个无法读取"));
   } catch (Exception ex) { if (!closed) Notice("刷新失败：" + ex.Message); }
   finally { if (!closed) reload.IsEnabled=true; }
  }
  void Delete(Entry file) {
   var report=CleanupUi.Run(this,new[]{file},permanent,delegate(Engine.RecycleReport r) {
    if (r.Removed.Count==0) return;
    var removed=new HashSet<string>(r.Removed.Select(x=>x.Path),StringComparer.OrdinalIgnoreCase);
    category.Files.RemoveAll(x=>removed.Contains(x.Path));
    foreach (var f in choices.Keys.Where(x=>removed.Contains(x.Path)).ToList()) choices.Remove(f);
    if (cleanup!=null) cleanup(r); else category.RefreshMatched();
    ResetTypes(); Rebuild(false);
   });
   if (report!=null) Notice(report.Summary);
  }
  void Export(bool excel) {
   var rows=filtered.Where(r=>r.Included).ToList();
   if (rows.Count==0) return;
   var dlg=new Microsoft.Win32.SaveFileDialog { Title=excel ? "导出 Excel" : "导出 CSV",
    Filter=excel ? "Excel 工作簿 (*.xlsx)|*.xlsx" : "CSV 文件 (*.csv)|*.csv",
    FileName=category.Name+"_"+DateTime.Now.ToString("yyyyMMdd_HHmm"), AddExtension=true, OverwritePrompt=true };
   if (dlg.ShowDialog(this)!=true) return;
   try {
    var headers=new List<string>{"序号","文件名","类型","大小","大小(字节)","创建时间","修改时间","完整路径"};
    var data=rows.Select((r,i)=>new object[]{i+1,r.File.Name,r.File.Type,Engine.Size(r.File.Size),r.File.Size,Stamp(r.File.Created),Stamp(r.File.Modified),r.File.Path}).ToList();
    if (excel) TableExport.WriteXlsx(dlg.FileName,headers,data); else TableExport.WriteCsv(dlg.FileName,headers,data);
    Notice("已导出 " + rows.Count + " 条记录");
   } catch (Exception ex) { MessageBox.Show(this,ex.Message,"导出失败",MessageBoxButton.OK,MessageBoxImage.Warning); }
  }
 }
}
