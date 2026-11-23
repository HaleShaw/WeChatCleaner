using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WeChatCleaner {
 internal static partial class WpfRegression {
  static readonly List<string> log=new List<string>();
  static string root, documents, old, newer, work, account, secondary, direct;
  static Account sample;
  static MainWindow main;
  const BindingFlags Instance=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
  static DateTime now=DateTime.Now;
  static T Get<T>(object obj,string name) { return (T)obj.GetType().GetField(name,Instance).GetValue(obj); }
  static object Run(object obj,string name,params object[] args) {
   return obj.GetType().GetMethods(Instance).Single(m=>m.Name==name && m.GetParameters().Length==args.Length).Invoke(obj,args);
  }
  static IEnumerable<T> Visuals<T>(DependencyObject x) where T:DependencyObject {
   if (x is T) yield return (T)x;
   if (!(x is Visual)) yield break;
   for (int i=0;i<VisualTreeHelper.GetChildrenCount(x);i++)
    foreach (var item in Visuals<T>(VisualTreeHelper.GetChild(x,i))) yield return item;
  }
  static Button ButtonAt(DependencyObject x,string name) { return Visuals<Button>(x).First(b=>b.IsVisible && (name.Equals(b.Content) || name==AutomationProperties.GetName(b))); }
  static void Click(ButtonBase b) { b.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent,b)); }
  static void Check(bool ok,string name) { if (!ok) throw new InvalidOperationException(name); log.Add("PASS " + name); }
  static async Task Wait(Func<bool> condition,int timeout=5000) {
   var watch=System.Diagnostics.Stopwatch.StartNew();
   while (watch.ElapsedMilliseconds<timeout) { if (condition()) return; await Task.Delay(22); }
   throw new TimeoutException("WPF wait timed out");
  }
  static async Task Paint(Window window) { window.UpdateLayout(); await window.Dispatcher.InvokeAsync(delegate { window.UpdateLayout(); },DispatcherPriority.ApplicationIdle); }
  static void Capture(Window window,string name,double dpi=96) {
   var view=(FrameworkElement)window.Content;
   var size=view.RenderSize;
   var offset=VisualTreeHelper.GetOffset(view);
   var image=new RenderTargetBitmap(Math.Max(1,(int)Math.Ceiling((size.Width+2*Math.Max(0,offset.X))*dpi/96)),
    Math.Max(1,(int)Math.Ceiling((size.Height+2*Math.Max(0,offset.Y))*dpi/96)),dpi,dpi,PixelFormats.Pbgra32);
   image.Render(view);
   var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
   using (var file=File.Create(Path.Combine(root,name+".png"))) png.Save(file);
  }
  static void Write(string folder,string name,int size,int m,int c,byte tag) {
   Directory.CreateDirectory(folder); var path=Path.Combine(folder,name);
   byte[] body=new byte[size]; body[0]=tag; body[size-1]=tag; File.WriteAllBytes(path,body);
   File.SetLastWriteTime(path,now.AddDays(-m)); File.SetCreationTime(path,now.AddDays(-c));
  }
  static void Prepare() {
   documents=Path.Combine(root,"重定向的文档");
   old=Path.Combine(root,"微信数据 3"); newer=Path.Combine(documents,"xwechat_files"); work=Path.Combine(documents,"WXWork");
   account=Path.Combine(old,"wxid_wpf_1"); secondary=Path.Combine(old,"wxid_wpf_2"); direct=Path.Combine(root,"自定义账号");
   for (int i=1;i<=65;i++) Write(Path.Combine(account,@"FileStorage\File\归档与项目资料"),i.ToString("00")+" 会议纪要与归档"+(i%2==0?".pdf":".txt"),i*100+7,80+i,i+4,(byte)i);
   Write(Path.Combine(secondary,@"FileStorage\Cache"),"仅此账号.txt",800,90,85,199);
   Write(Path.Combine(account,"Msg"),"聊天记录.db",100,151,150,63);
   Write(Path.Combine(account,@"FileStorage\Cache"),"缓存.png",677,145,145,22);
   string wx4=Path.Combine(newer,"wxid_4"); Directory.CreateDirectory(Path.Combine(wx4,"db_storage"));
   string org=Path.Combine(work,"75450000"); Directory.CreateDirectory(Path.Combine(org,"Data")); Directory.CreateDirectory(Path.Combine(work,@"75450001\Data"));
   string[] parents={Path.Combine(account,@"FileStorage\Video"),Path.Combine(wx4,@"msg\file"),Path.Combine(org,"Temp"),Path.Combine(secondary,@"FileStorage\Video")};
   for (int i=0;i<25;i++) for (int j=0;j<2+i%3;j++) Write(parents[j],i.ToString("00")+" 副本 "+j+".mp4",1024*1024+i,200-j,200-j,(byte)(i+100));
   Directory.CreateDirectory(Path.Combine(direct,"FileStorage"));
   if (!SourceStore.Save(new List<SourceEntry> {
    new SourceEntry{Kind=SourceKind.Old,Path=old},new SourceEntry{Kind=SourceKind.New,Path=newer},
    new SourceEntry{Kind=SourceKind.Work,Path=Path.Combine(root,"missing")} })) throw new IOException("无法创建测试配置");
   new AppSettings { Days=73,PageSize=65,LastAccount="0|"+account }.Save();
  }
  [STAThread] public static void Main(string[] args) {
   bool readOnlyMachine=args.Length>0 && args[0]=="--machine-startup";
   bool imageKeyActions=args.Length>0 && args[0]=="--image-key-actions";
   log.Add("Windows " + Environment.OSVersion.Version + " · " + (Environment.Is64BitProcess ? "x64" : "x86") + " process");
   string dir=Environment.GetEnvironmentVariable("WCC_TEST_DIR"); if (String.IsNullOrWhiteSpace(dir)) dir=Path.GetTempPath();
   root=Path.Combine(Path.GetFullPath(dir),"wpf_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
   Environment.SetEnvironmentVariable("WCC_PROFILE_DIR",null);
   ProfileStorage.App=new ProfileStorage(Path.Combine(root,"程序"),Path.Combine(root,"示例配置"));
   Program.ConfigureDpi();
   var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
   app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e) { log.Add("FAIL dispatcher: " + e.Exception); e.Handled=true; WriteLog(); app.Shutdown(1); };
   app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Theme.Xaml));
   if (imageKeyActions) documents=Path.Combine(root,"示例文档");
   else if (!readOnlyMachine) Prepare();
   main=readOnlyMachine ? new MainWindow() : new MainWindow(()=>new DiscoveryLocations { Documents=documents },DemoIdentity);
   main.ShowActivated=false; main.Opacity=0;
   main.Loaded+=async delegate {
    try {
     if (readOnlyMachine) await TestMachineStartup();
     else if (imageKeyActions) { TestPaddedImageTails(); await Paint(main); await TestImageKeyFileActions(); }
     else await TestAll();
     WriteLog(); app.Shutdown(0);
    }
    catch(Exception ex) { log.Add("FAIL " + ex); WriteLog(); app.Shutdown(1); }
   };
   Environment.ExitCode=app.Run(main);
  }
  static void WriteLog() { File.WriteAllLines(Path.Combine(root,"wpf-results.txt"),log); }
  static async Task TestMachineStartup() {
   Check(SourceStore.Load().Count==0,"空白配置开始，不读取日常数据源清单");
   await Wait(()=>!Get<bool>(main,"discovering"),25000);
   var accounts=Get<List<Account>>(main,"accounts");
   string doc=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
   string[] roots={"WeChat Files","xwechat_files","WXWork"};
   for (int i=0; i<roots.Length; i++) {
    var data=Path.Combine(doc,roots[i]); var kind=(SourceKind)i;
    var group=accounts.Where(a=>a.Kind==kind&&Engine.IsInside(data,a.Path)).ToList();
    Check(group.Count>0,Engine.ClientName(kind)+"自动识别 " + group.Count + " 个账号");
    foreach (var a in group) {
     if (Get<Account>(main,"current")!=a) Run(main,"SelectAccount",a);
     await Wait(()=>Get<ScanResult>(main,"result")!=null,60000);
     var scan=Get<ScanResult>(main,"result");
     Check(!scan.Cancelled && scan.Errors.Count==0 && scan.TotalCount>0,Engine.ClientName(kind)+"自动读取 " + scan.TotalCount+" 个文件");
    }
   }
   Check(SourceStore.Load().Count==0,"自动来源无需写入手动数据源清单");
  }
  static async Task TestAll() {
   await Wait(()=>!Get<bool>(main,"discovering")&&Get<ScanResult>(main,"result")!=null);
   await Paint(main);
   sample=Get<Account>(main,"current");
   Check(sample.Path==account&&Get<ComboBox>(main,"ageBox").Text=="超过 73 天","恢复上次账号与默认筛选，首页启动");
   Check(Get<List<Account>>(main,"accounts").Count==5,"保存的来源和自动发现合并；账号树含新旧微信与双企业账号");
   Check(Get<ScanResult>(main,"result").TotalCount==92,"初始账号无需点击扫描，首页已读取文件统计");
   var sidebar=Get<Dictionary<string,Button>>(main,"navButtons");
   Check(sidebar["settings"].TranslatePoint(new Point(),main).Y < sidebar["duplicates"].TranslatePoint(new Point(),main).Y,"清理设置在重复文件之前");
   Click(sidebar["duplicates"]); await Paint(main);
   var titleOrigin=HeadingOrigin("duplicatesPage","重复文件");
   Click(sidebar["about"]); await Paint(main);
   var about=Visuals<TextBlock>(main).Where(t=>t.IsVisible).Select(t=>t.Text).ToList();
   Check(about.Contains("版本 "+typeof(MainWindow).Assembly.GetName().Version.ToString(3))&&about.Contains("·　文件扫描与统计在本机完成。"),"关于：程序集版本与单独一行的隐私说明");
   Check(!about.Any(t=>t.StartsWith("工作台 /")) && (HeadingOrigin("aboutPage","关于")-titleOrigin).Length<1,"关于去掉面包屑并与重复文件标题对齐");
   Capture(main,"about");
   Click(sidebar["settings"]); await Paint(main);
   Check(!Visuals<TextBlock>(Get<Grid>(main,"settingsPage")).Any(t=>t.Text.StartsWith("工作台 /")) &&
    (HeadingOrigin("settingsPage","清理设置")-titleOrigin).Length<1,"清理设置去掉面包屑并与重复文件标题对齐");
   var combo=Visuals<ComboBox>(main).Where(b=>b.IsVisible).ToList();
   var keep=combo.Single(b=>AutomationProperties.GetName(b)=="默认保留时间");
   var mode=combo.Single(b=>AutomationProperties.GetName(b)=="清理方式");
   Check(mode.SelectedIndex==0,"设置初始为回收站");
   keep.SelectedItem="超过 90 天"; mode.SelectedIndex=1;
   Check(AppSettings.Load().PermanentDelete&&AppSettings.Load().Days==90&&Get<ComboBox>(main,"ageBox").Text=="超过 90 天","默认天数生效与清理方式保存");
   keep.SelectedItem="超过 73 天"; mode.SelectedIndex=0;
   Run(main,"SelectAccount",sample); await Paint(main);
   var result=Get<ScanResult>(main,"result"); var cat=result.Categories.Single(c=>c.RelativePath==@"FileStorage\File");
   Check(cat.MatchedCount==65&&result.TotalCount==92,"分类表与 65 条明细的数据正确");
   long total=result.TotalBytes; long initial=cat.Matched;
   var date=Get<ComboBox>(main,"dateBox"); date.SelectedIndex=1; await Paint(main);
   Check(cat.MatchedCount==0 && result.TotalBytes==total && Get<TextBlock>(main,"hitCount").Text.Contains(result.MatchedCount.ToString()),"改时间字段后分类和总览更新");
   var categoryTable=Get<TableView>(main,"categoryTable");
   Check(Visuals<TextBlock>(categoryTable).Any(v=>v.IsVisible&&v.Text=="0 个文件"),"筛选变化实际绑定到屏幕单元格");
   date.SelectedIndex=0; var size=Get<ComboBox>(main,"sizeBox"); size.SelectedIndex=1;
   Check(result.MatchedCount==0&&result.TotalBytes==total,"大小筛选无命中，稳定总量");
   size.SelectedIndex=0; var age=Get<ComboBox>(main,"ageBox"); age.Text="100"; Run(main,"CommitAge");
   Check(cat.MatchedCount==cat.Files.Count(f=>f.Modified<DateTime.Now.AddDays(-100)),"账号页自定义天数即时应用（"+cat.MatchedCount+" 条）");
   age.SelectedItem="超过 73 天"; cat.Selected=true; Run(main,"UpdateSelection"); await Paint(main);
   Check(cat.Matched==initial&&result.Pending.Count==65&&Get<Button>(main,"reviewBtn").IsEnabled,"清理按钮和已选合计一致");
   Capture(main,"main"); await TestDetails(cat);
   foreach (string path in new[]{"Msg",@"FileStorage\Temp"}) {
    var detail=new DetailDialog(main,sample,result.Categories.Single(c=>c.RelativePath==path),65,false) { ShowActivated=false,Opacity=0 };
    detail.Show(); await Paint(detail);
    Check(path=="Msg" ? Get<List<FileChoice>>(detail,"pageRows").Count==1&&!Visuals<Button>(detail).Any(b=>"删除".Equals(b.Content))&&!Get<CheckBox>(detail,"head").IsEnabled :
     Visuals<TextBlock>(detail).Any(t=>t.IsVisible&&t.Text=="当前条件下没有文件"),path=="Msg"?"受保护明细可查看，禁止勾选和删除":"0 条明细空态");
    detail.Close();
   }
   QueueDialog("确认清理",d=> { Check(Visuals<TextBlock>(d).Any(t=>t.Text.Contains("将处理 64 个文件")),"主页面确认与待清理数量一致"); d.Close(); });
   Run(main,"ConfirmClean"); Check(cat.Files.All(f=>File.Exists(f.Path)),"关闭清理确认无副作用");
   var account2=Get<List<Account>>(main,"accounts").Single(a=>a.Path==secondary);
   Run(main,"SelectAccount",account2); Check(Get<ScanResult>(main,"result")==null,"切换无旧结果串号");
   await Wait(()=>Get<ScanResult>(main,"result")!=null);
   Check(Get<ScanResult>(main,"result").Categories.Single(c=>c.RelativePath==@"FileStorage\Cache").Files.Count==1,"首次切换账号自动扫描当前账号");
   Run(main,"SelectAccount",sample); Check(Get<ScanResult>(main,"result")==result,"切回保留该账号会话扫描");
   Run(main,"StartScan"); Run(main,"CancelScan"); await Task.Delay(90);
   Check(Get<ScanResult>(main,"result")==result&&Get<TextBlock>(main,"statusText").Text=="扫描已取消","取消扫描忽略回调");
   Click(sidebar["duplicates"]); await TestDuplicates(); TestProgress(); TestAddSource();
   await TestDelayedDiscovery();
   TestPaddedImageTails();
   await TestImageKeySettings();
   await TestProfileSettings();
  }
  static Point HeadingOrigin(string page,string title) {
   return Visuals<TextBlock>(Get<Grid>(main,page)).Single(t=>t.Text==title).TranslatePoint(new Point(),main);
  }
  static IList Items(TableView table) { return (IList)Get<ListBox>(table,"body").ItemsSource; }
  static IEnumerable<int> RowNumbers(TableView table) {
   return Items(table).Cast<TableValue>().Select(item=>Get<int>(item.Value,"Number"));
  }
  static DuplicateGroup GroupOf(object wrapped) { return Get<DuplicateGroup>(((TableValue)wrapped).Value,"Group"); }
  static async Task TestDetails(Category cat) {
   var dialog=new DetailDialog(main,sample,cat,65,false,null,delegate { Run(main,"ApplyFilter"); },null) { ShowActivated=false,Opacity=0 };
   dialog.Show(); await Paint(dialog);
   var table=Get<TableView>(dialog,"table"); var list=Get<ListBox>(table,"body"); var v=Get<ScrollViewer>(table,"vertical"); var h=Get<ScrollBar>(table,"horizontal");
   var page=Get<List<FileChoice>>(dialog,"pageRows");
   Check(list.Items.Count==65&&Math.Abs(v.ExtentHeight-65*36)<1,"65 行且无第 66 个占位；单一纵向范围");
   var name=Visuals<TextBlock>(list).First(t=>t.IsVisible&&t.Text==page[0].File.Name); var action=ButtonAt(list,"复制路径");
   double lx=name.TranslatePoint(new Point(),table).X,rx=action.TranslatePoint(new Point(),table).X;
   h.Value=h.Maximum; await Paint(dialog);
   Check(h.Value>0&&Math.Abs(lx-name.TranslatePoint(new Point(),table).X)<1&&Math.Abs(rx-action.TranslatePoint(new Point(),table).X)<1,"横向末端文件名与操作同时冻结");
   v.ScrollToVerticalOffset(v.ScrollableHeight); await Paint(dialog);
   var last=(ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(64);
   var bottom=h.TranslatePoint(new Point(),table).Y;
   Check(last!=null&&Math.Abs(last.TranslatePoint(new Point(0,last.ActualHeight),table).Y-bottom)<=1.1,"最后一条贴住横栏上沿且完整可见");
   Check(Visuals<Button>(last).Count(b=>b.IsVisible)==5,"末行五个操作随同一行滚动"); Capture(dialog,"detail-bottom");
   var beforeHeight=table.ActualHeight;
   var refresh=ButtonAt(dialog,"刷新文件信息"); Click(refresh); await Wait(()=>refresh.IsEnabled); await Paint(dialog);
   Check(Math.Abs(table.ActualHeight-beforeHeight)<1&&Visuals<TextBlock>(dialog).Any(t=>t.IsVisible&&t.Text.Contains("已刷新 65 个文件")),"刷新提示不改变表高");
   h.Value=0;
   var type=Get<ComboBox>(dialog,"typeBox"); type.SelectedItem=".pdf";
   Check(Get<List<FileChoice>>(dialog,"filtered").Count==32,"列头类型过滤");
   type.SelectedItem="全部类型"; Check(Get<List<FileChoice>>(dialog,"filtered").Count==65,"切回全部类型恢复数据");
   Click(ButtonAt(table,"大小")); Click(ButtonAt(table,"大小"));
   var marks=Get<Dictionary<string,TextBlock>>(dialog,"marks");
   Check(marks["size"].Text=="▼"&&Get<List<FileChoice>>(dialog,"pageRows")[0].File.Size==cat.Files.Max(f=>f.Size),"按字节降序及箭头");
   Click(ButtonAt(table,"文件名")); Click(ButtonAt(table,"文件名"));
   Check(marks["name"].Text=="▼"&&Get<List<FileChoice>>(dialog,"pageRows")[0].File.Name.StartsWith("65"),"文件名降序及箭头");
   foreach(var pair in new[]{new[]{"创建时间","created"},new[]{"修改时间","modified"}}) {
    Click(ButtonAt(table,pair[0])); Check(marks[pair[1]].Text=="▲","按"+pair[0]+"排序及箭头");
   }
   var pageBox=Get<ComboBox>(dialog,"pageBox"); pageBox.Text="37"; Run(dialog,"CommitPageSize");
   Check(Get<List<FileChoice>>(dialog,"pageRows").Count==37&&Get<Button>(dialog,"next").IsEnabled,"自定义分页大小 37");
   Click(Get<Button>(dialog,"next"));
   var second=Get<List<FileChoice>>(dialog,"pageRows"); Check(second.Count==28 && second[0].Number==38,"先排序后翻页，序号连续");
   Click(Get<CheckBox>(dialog,"head")); Check(Get<List<FileChoice>>(dialog,"filtered").All(r=>!r.Included),"表头跨页取消纳入");
   Click(Get<CheckBox>(dialog,"head")); second[0].Included=false;
   Check(Get<TextBlock>(dialog,"total").Text.Contains("64 条")&&!second[0].File.Excluded,"状态条数、体积更新，未确定前是草稿");
   dialog.Width=dialog.MinWidth; await Paint(dialog);
   Check(Get<TextBox>(dialog,"jumpBox").ActualWidth>=48&&Visuals<ScrollBar>(table).Any(b=>b.IsVisible),"最小尺寸下页码与滚动条可用");
   foreach (int n in new[]{25,50,100,200,5000}) {
    Run(dialog,"ApplyPageSize",n); await Paint(dialog);
    var input=(TextBox)pageBox.Template.FindName("PART_EditableTextBox",pageBox);
    input.ApplyTemplate(); dialog.UpdateLayout();
    var viewport=(ScrollViewer)input.Template.FindName("PART_ContentHost",input);
    var font=new Typeface(input.FontFamily,input.FontStyle,input.FontWeight,input.FontStretch);
    var text=new FormattedText(input.Text,System.Globalization.CultureInfo.CurrentUICulture,input.FlowDirection,
     font,input.FontSize,input.Foreground,VisualTreeHelper.GetDpi(input).PixelsPerDip);
    Check(input.Text==n+" 条 / 页" && viewport.ViewportWidth>=text.WidthIncludingTrailingWhitespace &&
     viewport.ScrollableWidth<=.5,"最小窗口完整显示每页条数："+input.Text);
    if (n==25) Capture(dialog,"detail-page-25");
   }
   foreach (double dpi in new[]{120d,144d,192d}) Capture(dialog,"detail-raster-dpi"+dpi,dpi);
   dialog.Close(); Check(!cat.Files.Any(f=>f.Excluded),"关闭明细放弃勾选草稿");
   var commit=new DetailDialog(main,sample,cat,65,false) { ShowActivated=false,Opacity=0 };
   commit.Loaded+=delegate { Run(commit,"SetSelection",Get<List<FileChoice>>(commit,"filtered"),false); Click(ButtonAt(commit,"确定")); };
   Check(commit.ShowDialog()==true&&cat.Files.All(f=>f.Excluded),"确定提交跨页勾选");
   foreach(var f in cat.Files) f.Excluded=false;
   var permanent=new DetailDialog(main,sample,cat,65,true,null,null,report=>Run(main,"OnCleanup",report)) { ShowActivated=false,Opacity=0 };
   permanent.Show(); await Paint(permanent);
   var first=Get<List<FileChoice>>(permanent,"pageRows")[0].File;
   Check(Engine.IsInside(root,first.Path),"行内删除仅针对本次生成的文件");
   string savedCopy=Path.Combine(root,"另存为.txt"); FileActions.CopyTo(first,savedCopy);
   Check(File.ReadAllBytes(savedCopy).SequenceEqual(File.ReadAllBytes(first.Path)),"另存为复制字节保持一致");
   QueueDialog("确认清理",w=> { Check(Visuals<TextBlock>(w).Any(t=>t.Text.Contains("删除")&&t.Text.Contains("无法从回收站找回")),"行内删除显示当前模式"); w.Close(); });
   Click(ButtonAt(permanent,"删除"));
   Check(File.Exists(first.Path) && cat.Files.Count==65,"取消行内删除后源文件和选择量不变");
   ConfirmFixtureDelete();
   Click(ButtonAt(permanent,"删除"));
   Check(!File.Exists(first.Path)&&cat.Files.Count==64&&Get<TextBlock>(permanent,"total").Text.Contains("64 条")&&Get<ScanResult>(main,"result").Pending.Count==64,
    "行内删除成功，当前页、已选数量与主界面同步");
   permanent.Close();
  }
  static async Task TestDuplicates() {
   Run(main,"ScanDuplicates"); await Paint(main);
   var duplicates=Get<DuplicateResult>(main,"dupResult");
   var nextPage=Get<Button>(main,"dupNext"); var prevPage=Get<Button>(main,"dupPrevious");
   Check(duplicates.Groups.Count==25&&nextPage.IsEnabled&&!prevPage.IsEnabled,"原生查重成功关闭进度且可翻页");
   var list=Get<TableView>(main,"duplicateTable"); var firstGroup=GroupOf(Items(list)[0]);
   var scroll=Get<ScrollViewer>(list,"vertical");
   int capacity=list.VisibleRowCapacity;
   Check(capacity>0&&Items(list).Count==capacity&&scroll.ScrollableHeight<=.5,"重复列表首屏显示当前高度容纳的完整行");
   Check(firstGroup.Files.Count==4&&firstGroup.Size==duplicates.Groups.Where(g=>g.Files.Count==4).Max(g=>g.Size),"默认按份数、大小降序");
   var head=Visuals<TextBlock>(list).First(t=>t.Text=="所在账号");
   double y=head.TranslatePoint(new Point(),list).Y;
   var visited=RowNumbers(list).ToList();
   for (int i=0; nextPage.IsEnabled && i<25; i++) {
    Click(nextPage); await Paint(main); visited.AddRange(RowNumbers(list));
   }
   Check(visited.SequenceEqual(Enumerable.Range(1,25))&&!nextPage.IsEnabled&&prevPage.IsEnabled,"重复列表翻到末页，序号连续且无重复");
   Check(Math.Abs(head.TranslatePoint(new Point(),list).Y-y)<.5,"重复列表翻页时表头固定");
   int anchor=RowNumbers(list).First(); double fullHeight=main.Height;
   main.Height=main.MinHeight; await Paint(main);
   Check(list.VisibleRowCapacity<capacity && RowNumbers(list).Contains(anchor) &&
    Items(list).Count<=list.VisibleRowCapacity && scroll.ScrollableHeight<=.5,"缩小窗口后每页组数下降并保留浏览位置");
   Capture(main,"duplicates-compact");
   main.Height=fullHeight; await Paint(main);
   Check(list.VisibleRowCapacity==capacity&&RowNumbers(list).First()==anchor&&scroll.ScrollableHeight<=.5,"恢复窗口高度后回到原来分页");
   Capture(main,"duplicates-expanded");
   Click(ButtonAt(list,"大小")); await Paint(main);
   Check(GroupOf(Items(list)[0]).Size==duplicates.Groups.Max(g=>g.Size),"重复列表按原始大小排序"); Capture(main,"duplicates");
   var group=new DuplicateGroupDialog(main,firstGroup,false,f=>"测试账号",null) { ShowActivated=false,Opacity=0 };
   group.Show(); await Paint(group);
   var rows=Get<List<FileChoice>>(group,"rows");
   Check(group.Owner==main && rows.Select(r=>r.File.Created).SequenceEqual(rows.Select(r=>r.File.Created).OrderBy(t=>t)),"分组所属窗口与创建时间升序");
   Click(Get<CheckBox>(group,"selectAll")); Check(rows.All(r=>r.Included),"分组表头全选");
   Click(Get<CheckBox>(group,"selectAll")); Check(rows.All(r=>!r.Included),"分组表头取消全选"); Capture(group,"duplicate-group"); group.Close();
   var tall=new DuplicateGroup(); tall.Files.AddRange(Enumerable.Range(0,8).SelectMany(i=>firstGroup.Files));
   var scrollGroup=new DuplicateGroupDialog(main,tall,false,f=>"测试账号",null) { ShowActivated=false,Opacity=0 };
   scrollGroup.Show(); await Paint(scrollGroup);
   var groupTable=Get<TableView>(scrollGroup,"table"); var groupScroll=Get<ScrollViewer>(groupTable,"vertical");
   var groupHead=Visuals<TextBlock>(groupTable).First(t=>t.Text=="创建时间"); double headY=groupHead.TranslatePoint(new Point(),groupTable).Y;
   groupScroll.ScrollToEnd(); await Paint(scrollGroup);
   Check(groupScroll.VerticalOffset>0&&Math.Abs(groupHead.TranslatePoint(new Point(),groupTable).Y-headY)<.5,"重复分组窗口滚动时表头固定");
   scrollGroup.Close();
   var deleteTest=new DuplicateGroupDialog(main,firstGroup,true,f=>"测试账号",report=>Run(main,"OnCleanup",report)) { ShowActivated=false,Opacity=0 };
   deleteTest.Show(); await Paint(deleteTest);
   var first=Get<List<FileChoice>>(deleteTest,"rows")[0].File;
   Check(Engine.IsInside(root,first.Path),"分组删除仅针对本次生成的文件");
   long before=duplicates.WastedTotal; int copies=firstGroup.Files.Count;
   Get<List<FileChoice>>(deleteTest,"rows")[0].Included=true;
   QueueDialog("确认清理",w=>w.Close()); Click(ButtonAt(deleteTest,"永久删除"));
   Check(File.Exists(first.Path)&&firstGroup.Files.Count==copies,"取消分组批量清理无副作用");
   ConfirmFixtureDelete(); Click(ButtonAt(deleteTest,"永久删除"));
   Check(!File.Exists(first.Path)&&Get<List<FileChoice>>(deleteTest,"rows").Count==copies-1&&duplicates.WastedTotal==before-first.Size,
    "分组清理后成员数和释放量同步");
   deleteTest.Close();
  }
  static void ConfirmFixtureDelete() {
   QueueDialog("确认清理",w=>Click(ButtonAt(w,"确认永久删除")));
  }
  static void QueueDialog(string title,Action<Window> test) {
   var timer=new DispatcherTimer(DispatcherPriority.ApplicationIdle) { Interval=TimeSpan.FromMilliseconds(15) }; int tries=0;
   timer.Tick+=delegate {
    var d=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w.IsLoaded&&w.IsVisible&&w.Title==title);
    if (d==null && ++tries<130) return;
    timer.Stop(); if (d!=null) { d.Opacity=0; test(d); }
    else { log.Add("FAIL expected dialog: " + title); WriteLog(); Application.Current.Shutdown(1); }
   }; timer.Start();
  }
  static void TestProgress() {
   using(var release=new ManualResetEventSlim()) {
    QueueDialog("查找重复文件",w=> {
     try {
      Check(w.SizeToContent==SizeToContent.Height&&w.ResizeMode==ResizeMode.NoResize&&!Visuals<Button>(w).Any(),"进度紧凑，无页脚、关闭按钮或调整角");
      Capture(w,"duplicate-progress");
     } finally { release.Set(); }
    });
    int n=Ui.Progress(main,"查找重复文件","正在比较 ≥ 1 MB 的文件：先按大小归组，再核对文件内容。",delegate{if (!release.Wait(6000)) throw new TimeoutException(); return 17;},false);
    Check(n==17&&!Application.Current.Windows.OfType<Window>().Any(w=>w.Title=="查找重复文件"),"查重进度成功后自动关闭");
   }
   var all=Get<List<Account>>(main,"accounts"); var snapshot=all.ToArray(); all.Clear();
   all.Add(new Account { Path=Path.Combine(root,"inaccessible"),Kind=SourceKind.Old });
   QueueDialog("查找未完成",w=> { Check(ButtonAt(w,"重试").IsEnabled,"全部数据源不可读时提供重试"); w.Close(); });
   try { Run(main,"ScanDuplicates"); } finally { all.Clear(); all.AddRange(snapshot); }
  }
  static async Task TestDelayedDiscovery() {
   string original=Environment.GetEnvironmentVariable("WCC_PROFILE_DIR");
   using (var wait=new ManualResetEventSlim()) {
    Environment.SetEnvironmentVariable("WCC_PROFILE_DIR",Path.Combine(root,"profile-delayed"));
    var late=new MainWindow(delegate { wait.Wait(5000); return new DiscoveryLocations { Documents=documents }; },DemoIdentity) { ShowActivated=false,Opacity=0 };
    try {
     late.Show(); await Paint(late);
     Check(Get<bool>(late,"discovering")&&Get<List<Account>>(late,"accounts").Count==0,"查找未结束时界面可用且有查找状态");
     QueueDialog("添加数据源",w=> {
      var textbox=Visuals<TextBox>(w).Single(t=>AutomationProperties.GetName(t)=="数据目录");
      textbox.Text=direct; Click(ButtonAt(w,"识别并添加"));
     });
     Run(late,"AddSource");
     Check(SourceStore.Load().Count==1&&Get<Account>(late,"current").Path==direct,"可以在自动查找期间保存并选中手动目录");
     wait.Set();
     await Wait(()=>!Get<bool>(late,"discovering")&&Get<ScanResult>(late,"result")!=null);
     Check(Get<List<Account>>(late,"accounts").Count==4&&Get<Account>(late,"current").Path==direct,
      "较晚返回的自动发现追加账号，不抢走用户正在使用的账号");
    } finally { wait.Set(); late.Close(); Environment.SetEnvironmentVariable("WCC_PROFILE_DIR",original); }
   }
  }
  static void TestAddSource() {
   string documents=Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
   Check(MainWindow.DataFolderStartPath("")==documents && MainWindow.DataFolderStartPath(Path.Combine(root,"does-not-exist"))==documents,
    "空白或无效路径从 Windows 文档目录开始选择");
   Check(MainWindow.DataFolderStartPath(" \""+old+"\" ")==Path.GetFullPath(old),"有效的手输目录可作为选择器起点");
   QueueDialog("添加数据源",w=> {
    var textbox=Visuals<TextBox>(w).Single(t=>AutomationProperties.GetName(t)=="数据目录");
    Check(ButtonAt(w,"选择目录").IsVisible&&!Visuals<TextBlock>(w).Any(t=>t.Text=="显示名称"),"添加窗口支持手输和选择目录，无名称字段");
    textbox.Text=Path.Combine(root,"does-not-exist"); Click(ButtonAt(w,"识别并添加"));
    Check(Visuals<TextBlock>(w).Any(t=>t.Text.Contains("可访问目录")),"表单提示无效目录");
    textbox.Text=old+"/"; Click(ButtonAt(w,"识别并添加"));
    Check(Visuals<TextBlock>(w).Any(t=>t.Text.Contains("已添加")),"重复目录不会重复保存"); Capture(w,"add-source");
    textbox.Text=direct+"/"; Click(ButtonAt(w,"识别并添加"));
   });
   Run(main,"AddSource"); Check(SourceStore.Load().Count==4&&Get<Account>(main,"current").Path==direct,"保存直输的账号目录并自动选中");
  }
 }
}
