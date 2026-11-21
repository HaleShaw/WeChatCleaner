using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Text;

namespace WeChatCleaner {

 public class MainWindow : Window {
  readonly List<SourceEntry> sources = new List<SourceEntry>();
  readonly List<Account> accounts = new List<Account>();
  Account current;
  ScanResult result;
  CancellationTokenSource cancel, startupSearch;
  bool discovering = true, started;
  readonly Func<DiscoveryLocations> discoveryLocations;
  AppSettings settings;
  DuplicateResult dupResult;
  bool duplicateBusy;
  string scanStatus = "";
  readonly Dictionary<string,ScanResult> scans = new Dictionary<string,ScanResult>(StringComparer.OrdinalIgnoreCase);
  ScanResult shownResult;

  StackPanel treePanel;
  Grid pageHost;
  Grid accountPage, duplicatesPage, settingsPage, aboutPage;
  readonly Dictionary<string,Button> navButtons = new Dictionary<string,Button>();
  readonly Dictionary<string,Grid> pageMap = new Dictionary<string,Grid>();

  TextBlock sourceTitle, sourcePath, sourceStatus, sourceIcon;
  TextBlock totalStat, countStat, selectedStat, scopeStat, hitCount, selectedCount, bottomText;
  TableView categoryTable;
  ComboBox ageBox, sizeBox, dateBox;
  Button scanBtn, clearBtn, reviewBtn;
  Border bannerBox;
  TextBlock bannerText;
  StackPanel settingsPanel;
  TableView duplicateTable;
  TextBlock dupSummary, dupCount, dupPageLabel;
  Button duplicateSearch, dupPrevious, dupNext;
  int dupPage, dupPageSize = 1, dupPageAnchor;
  string dupSortKey = "count";
  bool dupSortDesc = true;
  readonly Dictionary<string,TextBlock> dupSortMarks = new Dictionary<string,TextBlock>();
  int filterDays = 30;
  bool changingAge;
  ProgressBar progress;
  TextBlock statusText;
  Border toastBorder;
  TextBlock toastText;
  DispatcherTimer toastTimer;
  string clientInfo = "";
  DispatcherTimer clientTimer;

  static string AppVersion { get { return typeof(MainWindow).Assembly.GetName().Version.ToString(3); } }

  public MainWindow() : this(DiscoveryLocations.Windows) { }
  internal MainWindow(Func<DiscoveryLocations> discoveryLocations) {
   this.discoveryLocations = discoveryLocations;
   Title = "WeChat Cleaner";
   var workArea = SystemParameters.WorkArea;
   Width = Math.Min(1400,workArea.Width); Height = Math.Min(870,workArea.Height);
   MinWidth = Math.Min(925,workArea.Width); MinHeight = Math.Min(515,workArea.Height);
   UseLayoutRounding = true; SnapsToDevicePixels = true;
   WindowStartupLocation = WindowStartupLocation.CenterScreen;
   Background = Ui.Canvas;
   Icon = BrandIcon.Source;
   settings = AppSettings.Load();
   filterDays = settings.Days;
   try {
    if (Application.Current != null && Application.Current.FindResource("GhostButton") == null)
     Application.Current.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Theme.Xaml));
    if (FindResource("GhostButton") == null)
     Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Theme.Xaml));
   } catch (Exception ex) { MessageBox.Show("主题加载失败：" + ex.Message, "WeChat Cleaner"); }
   Build();
   sources.AddRange(SourceStore.Load());
   RefreshTree();
   RefreshOverview();
   RefreshDuplicatesPage();
   Loaded += delegate {
    if (started) return;
    started = true; clientTimer.Start(); SearchAtStartup();
   };
   Closed += delegate {
    if (startupSearch != null) startupSearch.Cancel();
    namingGeneration++; CancelScan(); clientTimer.Stop(); toastTimer.Stop();
   };
  }

  #region building blocks
  Border Panel(UIElement content, Thickness pad) {
   return new Border {
    Child = content, Background = Brushes.White, CornerRadius = new CornerRadius(12),
    BorderBrush = Ui.Brush("#E5EDE8"), BorderThickness = new Thickness(1), Padding = pad
   };
  }
  TextBlock SectionLabel(string text) {
   var t = Ui.Label(text, 11, Ui.Muted2, true);
   t.FontWeight = FontWeights.Bold;
   return t;
  }
  #endregion

  #region layout
  void Build() {
   var root = new Grid { Background = Ui.Canvas };
   root.ColumnDefinitions.Add(new ColumnDefinition{ Width = new GridLength(226) });
   root.ColumnDefinitions.Add(new ColumnDefinition{ Width = new GridLength(1, GridUnitType.Star) });
   Content = root;

   root.Children.Add(BuildSidebar());
   pageHost = new Grid();
   Grid.SetColumn(pageHost, 1);
   root.Children.Add(pageHost);

   accountPage = BuildAccountPage();
   duplicatesPage = BuildDuplicatesPage();
   settingsPage = BuildSettingsPage();
   aboutPage = BuildAboutPage();
   pageMap["account"] = accountPage;
   pageMap["duplicates"] = duplicatesPage;
   pageMap["settings"] = settingsPage;
   pageMap["about"] = aboutPage;
   foreach (var kv in pageMap) pageHost.Children.Add(kv.Value);
   ShowPage("account");

   toastBorder = new Border {
    Background = Ui.Brush("#263D32"), CornerRadius = new CornerRadius(8), Padding = new Thickness(14,10,14,10),
    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
    Margin = new Thickness(0,0,24,24), Visibility = Visibility.Collapsed, Opacity = 0
   };
   toastText = Ui.Label("", 12, Brushes.White, false);
   toastBorder.Child = toastText;
   root.Children.Add(toastBorder);
   toastTimer = new DispatcherTimer();
   toastTimer.Interval = TimeSpan.FromSeconds(3);
   toastTimer.Tick += delegate { toastTimer.Stop(); toastBorder.Visibility = Visibility.Collapsed; };

   // the "is the client running" probe touches the process list, so it runs off the UI thread
   clientTimer = new DispatcherTimer();
   clientTimer.Interval = TimeSpan.FromSeconds(10);
   clientTimer.Tick += delegate { RefreshClientInfo(); };
  }

  Border BuildSidebar() {
   var side = new Border { Background = Ui.Canvas, BorderBrush = Ui.Brush("#E4ECE7"), BorderThickness = new Thickness(0,0,1,0) };
   var grid = new Grid { Margin = new Thickness(16,18,14,14) };
   grid.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });   // brand
   grid.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });   // workbench
   grid.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });   // separator
   grid.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });   // sources header
   grid.RowDefinitions.Add(new RowDefinition{ Height = new GridLength(1, GridUnitType.Star) }); // tree
   grid.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });   // footer
   side.Child = grid;

   // ---- brand ----
   var brand = new Grid { Margin = new Thickness(4,0,4,18) };
   brand.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   brand.ColumnDefinitions.Add(new ColumnDefinition{ Width = new GridLength(1, GridUnitType.Star) });
   var logo = BrandIcon.CreateImage(36);
   logo.Margin = new Thickness(0,0,11,0);
   brand.Children.Add(logo);
   var brandText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
   brandText.Children.Add(Ui.Label("WeChat Cleaner", 13.5, Ui.Ink, true));
   var brandSub = Ui.Label("微信 / 企业微信存储清理", 10.5, Ui.Muted2, false);
   brandSub.Margin = new Thickness(0,2,0,0);
   brandText.Children.Add(brandSub);
   Grid.SetColumn(brandText, 1);
   brand.Children.Add(brandText);
   grid.Children.Add(brand);

   // ---- workbench ----
   var navHost = new StackPanel();
   navHost.Children.Add(SectionLabel("工作台"));
   var navStack = new StackPanel { Margin = new Thickness(0,6,0,0) };
   navStack.Children.Add(NavButton("settings", "⚙", "清理设置"));
   navStack.Children.Add(NavButton("duplicates", "▧", "重复文件"));
   navHost.Children.Add(navStack);
   grid.Children.Add(navHost);

   var sep = new Separator { Margin = new Thickness(6,16,6,12) };
   grid.Children.Add(sep);

   // ---- data sources ----
   var head = new Grid { Margin = new Thickness(2,0,2,7) };
   head.ColumnDefinitions.Add(new ColumnDefinition{ Width = new GridLength(1, GridUnitType.Star) });
   head.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   var hl = SectionLabel("数据源");
   hl.Margin = new Thickness(8,0,0,0);
   head.Children.Add(hl);
   var add = Ui.Button("＋", delegate { AddSource(); }, "GhostButton");
   add.FontSize = 17; add.Width = 28; add.Height = 28; add.Padding = new Thickness(0);
   add.VerticalAlignment = VerticalAlignment.Center;
   add.ToolTip = "添加数据源"; AutomationProperties.SetName(add,"添加数据源");
   Grid.SetColumn(add, 1);
   head.Children.Add(add);
   grid.Children.Add(head);

   treePanel = new StackPanel();
   var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
   scroll.Content = treePanel;
   grid.Children.Add(scroll);

   var footStack = new StackPanel();
   footStack.Children.Add(NavButton("about", "ⓘ", "关于"));
   grid.Children.Add(footStack);

   // place the rows
   Grid.SetRow(brand, 0);
   Grid.SetRow(navHost, 1);
   Grid.SetRow(sep, 2);
   Grid.SetRow(head, 3);
   Grid.SetRow(scroll, 4);
   Grid.SetRow(footStack, 5);
   return side;
  }

  Button NavButton(string key, string icon, string text) {
   var content = new StackPanel { Orientation = Orientation.Horizontal };
   var ic = Ui.Label(icon, 15, Ui.Muted, false);
   ic.Width = 22; ic.TextAlignment = TextAlignment.Center; ic.VerticalAlignment = VerticalAlignment.Center;
   content.Children.Add(ic);
   var tx = Ui.Label(text, 13, Ui.Ink, false);
   tx.Margin = new Thickness(6,0,0,0);
   content.Children.Add(tx);
   var b = new Button { Content = content, Style = (Style)FindResource("NavButton") };
   b.Click += delegate { ShowPage(key); };
   AutomationProperties.SetName(b,text);
   navButtons[key] = b;
   return b;
  }
  #endregion

  #region account page
  Grid BuildAccountPage() {
   var g = new Grid { Margin = new Thickness(22,14,22,0) };
   g.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });   // 0 account bar
   g.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });   // 1 banner
   g.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });   // 2 stats
   g.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });   // 3 filters
   g.RowDefinitions.Add(new RowDefinition{ Height = new GridLength(1, GridUnitType.Star) }); // 4 table
   g.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });   // 5 bottom bar

   // ---------------- account bar ----------------
   var top = new Grid { Margin = new Thickness(0,0,0,12) };
   top.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   top.ColumnDefinitions.Add(new ColumnDefinition{ Width = new GridLength(1, GridUnitType.Star) });
   top.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });

   var iconBox = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(12), Background = Ui.AccentSoft, Margin = new Thickness(0,0,12,0), VerticalAlignment = VerticalAlignment.Center };
   sourceIcon = Ui.Label("微", 17, Ui.Accent, true);
   sourceIcon.TextAlignment = TextAlignment.Center;
   sourceIcon.HorizontalAlignment = HorizontalAlignment.Center;
   sourceIcon.VerticalAlignment = VerticalAlignment.Center;
   iconBox.Child = sourceIcon;
   top.Children.Add(iconBox);

   var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
   sourceTitle = Ui.Label("", 14.5, Ui.Ink, true);
   sourceTitle.TextTrimming = TextTrimming.CharacterEllipsis;
   info.Children.Add(sourceTitle);
   sourcePath = Ui.Label("", 11.5, Ui.Muted, false);
   sourcePath.Margin = new Thickness(0,4,0,0);
   sourcePath.TextTrimming = TextTrimming.CharacterEllipsis;
   sourcePath.TextWrapping = TextWrapping.NoWrap;
   info.Children.Add(sourcePath);
   Grid.SetColumn(info, 1);
   top.Children.Add(info);

   var topRight = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
   var pill = new Border { Background = Ui.AccentSoft, CornerRadius = new CornerRadius(7), Padding = new Thickness(10,6,10,6), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,10,0) };
   sourceStatus = Ui.Label("", 11, Ui.Brush("#45866A"), false);
   pill.Child = sourceStatus;
   topRight.Children.Add(pill);
   scanBtn = Ui.Button("◌　开始扫描", delegate { StartScan(); }, "PrimaryButton");
   scanBtn.MinHeight = 34;
   topRight.Children.Add(scanBtn);
   Grid.SetColumn(topRight, 2);
   top.Children.Add(topRight);
   Grid.SetRow(top, 0);
   g.Children.Add(top);

   // ---------------- banner ----------------
   bannerBox = new Border { Background = Ui.Brush("#EDF8F2"), CornerRadius = new CornerRadius(9), Padding = new Thickness(12,7,12,7), Margin = new Thickness(0,0,0,9), Visibility = Visibility.Collapsed };
   bannerText = Ui.Label("", 11.5, Ui.Brush("#4B7862"), false);
   bannerText.TextWrapping = TextWrapping.NoWrap; bannerText.TextTrimming = TextTrimming.CharacterEllipsis;
   bannerBox.Child = bannerText;
   Grid.SetRow(bannerBox, 1);
   g.Children.Add(bannerBox);

   // ---------------- statistics (one slim line instead of three tall cards) ----------------
   var stats = new Border { Background = Brushes.White, BorderBrush = Ui.Brush("#E5EDE8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Margin = new Thickness(0,0,0,10) };
   var statsRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20,0,20,0) };
   var s0 = StatInline("总占用", out totalStat);
   countStat = StatCaption(); s0.Children.Add(countStat);
   var s2 = StatInline("筛选命中", out scopeStat);
   hitCount = StatCaption(); s2.Children.Add(hitCount);
   var s3 = StatInline("已选待处理", out selectedStat);
   selectedCount = Ui.Label("", 11.5, Ui.Muted2, false);
   selectedCount.Margin = new Thickness(8,0,0,0);
   selectedCount.VerticalAlignment = VerticalAlignment.Center;
   s3.Children.Add(selectedCount);
   statsRow.Children.Add(s0);
   statsRow.Children.Add(StatDivider());
   statsRow.Children.Add(s2);
   statsRow.Children.Add(StatDivider());
   statsRow.Children.Add(s3);
   stats.Child = statsRow;
   Grid.SetRow(stats, 2);
   g.Children.Add(stats);

   // ---------------- filters ----------------
   var filters = new Grid { Margin = new Thickness(0,0,0,10) };
   filters.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   filters.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   filters.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   filters.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   filters.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   filters.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   filters.ColumnDefinitions.Add(new ColumnDefinition{ Width = new GridLength(1, GridUnitType.Star) });
   var fl = Ui.Label("筛选条件", 12, Ui.Brush("#81958B"), false);
   fl.Margin = new Thickness(0,0,10,0);
   fl.VerticalAlignment = VerticalAlignment.Center;
   filters.Children.Add(fl);
   ageBox = new ComboBox { Width = 131, Height = 32, IsEditable = true, IsTextSearchEnabled = false };
   foreach (int d in new[]{30,90,180,365}) ageBox.Items.Add(AgeText(d));
   ageBox.Items.Add("所有时间"); ageBox.Items.Add("自定义…");
   DisplayAge(filterDays);
   ageBox.Margin = new Thickness(0,0,10,0);
   ageBox.VerticalAlignment = VerticalAlignment.Center;
   ageBox.ToolTip = "选择预设，或在「自定义」中输入天数";
   AutomationProperties.SetName(ageBox,"时间范围");
   ageBox.SelectionChanged += delegate {
    if (changingAge) return;
    string choice = ageBox.SelectedItem as string;
    if (choice == "自定义…") { Ui.FocusEdit(ageBox); return; }
    int days;
    if (TryDays(choice,out days)) { filterDays = days; ApplyFilter(); }
   };
   Ui.ApplyOnExit(ageBox,CommitAge);
   Grid.SetColumn(ageBox, 1);
   filters.Children.Add(ageBox);
   sizeBox = new ComboBox { Width = 115, Height = 32 };
   foreach (string s in new string[]{"大小不限","至少 10 MB","至少 100 MB","至少 1 GB"}) sizeBox.Items.Add(s);
   sizeBox.SelectedIndex = settings.MinBytes == 10L*1024*1024 ? 1 : settings.MinBytes == 100L*1024*1024 ? 2 : settings.MinBytes == 1024L*1024*1024 ? 3 : 0;
   sizeBox.Margin = new Thickness(0,0,10,0);
   sizeBox.VerticalAlignment = VerticalAlignment.Center;
   AutomationProperties.SetName(sizeBox,"文件大小");
   sizeBox.SelectionChanged += delegate { ApplyFilter(); };
   Grid.SetColumn(sizeBox, 2);
   filters.Children.Add(sizeBox);
   dateBox = new ComboBox { Width = 115, Height = 32 };
   dateBox.Items.Add("按修改时间");
   dateBox.Items.Add("按创建时间");
   dateBox.SelectedIndex = settings.UseCreated ? 1 : 0;
   dateBox.VerticalAlignment = VerticalAlignment.Center;
   AutomationProperties.SetName(dateBox,"日期字段");
   dateBox.SelectionChanged += delegate { ApplyFilter(); };
   Grid.SetColumn(dateBox, 3);
   filters.Children.Add(dateBox);
   // the clear button belongs to the filters, but is separated from them by a hairline
   var fSep = new Border { Width = 1, Height = 18, Background = Ui.Brush("#DCE7E0"), Margin = new Thickness(20,0,16,0), VerticalAlignment = VerticalAlignment.Center };
   Grid.SetColumn(fSep, 4);
   filters.Children.Add(fSep);
   clearBtn = Ui.Button("清空选择", delegate { ClearSelection(); }, "QuietButton");
   clearBtn.VerticalAlignment = VerticalAlignment.Center;
   clearBtn.Padding = new Thickness(13,6,13,6);
   clearBtn.MinHeight = 30;
   clearBtn.ToolTip = "取消所有分类的勾选";
   Grid.SetColumn(clearBtn, 5);
   filters.Children.Add(clearBtn);
   var fRight = new Grid { VerticalAlignment = VerticalAlignment.Center, ClipToBounds = true, Margin = new Thickness(10,0,0,0) };
   progress = new ProgressBar { Height = 4, VerticalAlignment = VerticalAlignment.Bottom, Visibility = Visibility.Collapsed };
   fRight.Children.Add(progress);
   statusText = Ui.Label("", 11.5, Ui.Muted, false);
   statusText.TextTrimming = TextTrimming.CharacterEllipsis; statusText.TextWrapping = TextWrapping.NoWrap;
   statusText.TextAlignment = TextAlignment.Right; statusText.Margin = new Thickness(0,0,0,3);
   statusText.VerticalAlignment = VerticalAlignment.Center;
   fRight.Children.Add(statusText);
   Grid.SetColumn(fRight, 6);
   filters.Children.Add(fRight);
   Grid.SetRow(filters, 3);
   g.Children.Add(filters);

   // ---------------- category table ----------------
   var content = new Grid { Margin = new Thickness(0,0,0,12) };
   BuildCategoryPanel(content);
   Grid.SetRow(content, 4);
   g.Children.Add(content);

   // ---------------- bottom bar ----------------
   var bottom = new Border { Background = Brushes.White, BorderBrush = Ui.Brush("#E7EEE9"), BorderThickness = new Thickness(0,1,0,0), Margin = new Thickness(-22,0,-22,0), Padding = new Thickness(22,9,22,9) };
   var bgrid = new Grid();
   bgrid.ColumnDefinitions.Add(new ColumnDefinition{ Width = new GridLength(1, GridUnitType.Star) });
   bgrid.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   bottomText = Ui.Label("已选择 0 个分类 · 0 B", 12.5, Ui.Accent, true);
   bottomText.VerticalAlignment = VerticalAlignment.Center;
   bgrid.Children.Add(bottomText);
   reviewBtn = Ui.Button("清理", delegate { ConfirmClean(); }, "PrimaryButton");
   reviewBtn.IsEnabled = false;
   reviewBtn.MinHeight = 34;
   Grid.SetColumn(reviewBtn, 1);
   bgrid.Children.Add(reviewBtn);
   bottom.Child = bgrid;
   Grid.SetRow(bottom, 5);
   g.Children.Add(bottom);
   return g;
  }

  /// One inline statistic: small label + strong value, separated from the next by a hairline.
  StackPanel StatInline(string label, out TextBlock value) {
   var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 42 };
   var l = Ui.Label(label, 11.5, Ui.Muted, false);
   l.Margin = new Thickness(0,0,8,0);
   l.VerticalAlignment = VerticalAlignment.Center;
   sp.Children.Add(l);
   value = Ui.Label("—", 14, Ui.Ink, true);
   value.VerticalAlignment = VerticalAlignment.Center;
   sp.Children.Add(value);
   return sp;
  }

  TextBlock StatCaption() {
   var label = Ui.Label("",11,Ui.Muted,false); label.Margin = new Thickness(7,0,0,0); return label;
  }
  Border StatDivider() {
   return new Border { Width = 1, Background = Ui.Brush("#EDF2EE"), Margin = new Thickness(16,0,16,0), Height = 20, VerticalAlignment = VerticalAlignment.Center };
  }

  void BuildCategoryPanel(Grid parent) {
   categoryTable = new TableView(55,
    new TableColumn(54,"选择",o=>CategoryCheck((Category)o)),
    new TableColumn(-1.15,"数据类型",o=>CategoryName((Category)o)),
    new TableColumn(101,"总占用",o=>CategoryValue((Category)o,"TotalText",TextAlignment.Right,false,Ui.Ink)),
    new TableColumn(122,"筛选后",o=>CategoryMatches((Category)o)),
    new TableColumn(-1.15,"说明",o=>CategoryRisk((Category)o)),
    new TableColumn(75,"操作",o=>TableView.Link("查看 ›",delegate { OpenDetail((Category)o); })));
   AutomationProperties.SetName(categoryTable,"数据类型表格"); parent.Children.Add(categoryTable);
  }
  CheckBox CategoryCheck(Category c) {
   var box = Ui.CenterCheck("选择 " + c.Name); box.IsEnabled = c.CanSelect;
   box.SetBinding(CheckBox.IsCheckedProperty,new Binding("Selected") { Source=c, Mode=BindingMode.TwoWay });
   box.Click += delegate { c.Selected=box.IsChecked==true; UpdateSelection(); }; return box;
  }
  FrameworkElement CategoryName(Category c) {
   var p = new StackPanel { Margin = new Thickness(12,0,8,0), VerticalAlignment = VerticalAlignment.Center, ToolTip = c.RelativePath };
   p.Children.Add(TableView.Text(c.Name,TextAlignment.Left,true,0,Ui.Ink));
   var path = TableView.Text(c.RelativePath,TextAlignment.Left,false,0,Ui.Muted);
   path.FontSize = 11; path.Margin = new Thickness(0,3,0,0); p.Children.Add(path);
   return p;
  }
  TextBlock CategoryValue(Category c, string property, TextAlignment align, bool strong, Brush brush) {
   var text = TableView.Text("",align,strong,13,brush);
   text.SetBinding(TextBlock.TextProperty,new Binding(property) { Source=c }); return text;
  }
  FrameworkElement CategoryMatches(Category c) {
   var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
   p.Children.Add(CategoryValue(c,"MatchedText",TextAlignment.Right,true,c.Protected?Ui.Muted:Ui.Accent));
   var count = CategoryValue(c,"MatchedCountText",TextAlignment.Right,false,Ui.Muted);
   count.FontSize = 11; count.Margin = new Thickness(13,3,13,0); p.Children.Add(count); return p;
  }
  FrameworkElement CategoryRisk(Category c) {
   string fill = c.Protected ? "#EDF2F6" : c.Risk==RiskLevel.Recoverable ? "#E5F4EB" : "#FFF1E5";
   string ink = c.Protected ? "#455B71" : c.Risk==RiskLevel.Recoverable ? "#126547" : "#93481E";
   var p = new StackPanel { Margin = new Thickness(12,0,8,0), VerticalAlignment = VerticalAlignment.Center, ToolTip = c.Description };
   p.Children.Add(new Border { Background=Ui.Brush(fill), CornerRadius=new CornerRadius(5), HorizontalAlignment=HorizontalAlignment.Left,
    Padding=new Thickness(7,3,7,3), Child=Ui.Label("●  " + c.RiskText,10.5,Ui.Brush(ink),true) });
   var text = TableView.Text(c.Description,TextAlignment.Left,false,0,Ui.Muted);
   text.FontSize=11; text.Margin=new Thickness(0,3,0,0); p.Children.Add(text); return p;
  }
  #endregion

  #region duplicates page
  Grid WorkbenchPage(string title, TextBlock subtitle, FrameworkElement action = null) {
   var g = new Grid { Margin = new Thickness(22,16,22,14) };
   g.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
   g.RowDefinitions.Add(new RowDefinition());
   var head = new Grid { Margin = new Thickness(0,0,0,12) };
   head.ColumnDefinitions.Add(new ColumnDefinition());
   var titles = new StackPanel();
   titles.Children.Add(Ui.Label(title,21,Ui.Ink,true));
   subtitle.Margin = new Thickness(0,5,10,0); titles.Children.Add(subtitle);
   head.Children.Add(titles);
   if (action != null) {
    head.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
    action.VerticalAlignment = VerticalAlignment.Center;
    Grid.SetColumn(action,1); head.Children.Add(action);
   }
   g.Children.Add(head);
   return g;
  }
  Grid BuildDuplicatesPage() {
   dupSummary = Ui.Label("跨数据源比较文件内容",12,Ui.Muted,false);
   duplicateSearch = Ui.Button("查找重复文件",ScanDuplicates,"PrimaryButton");
   var g = WorkbenchPage("重复文件",dupSummary,duplicateSearch);
   g.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });

   duplicateTable = new TableView(38,
    new TableColumn(48,"序号",o=>TableView.Text(((DuplicateRow)o).Number.ToString(),TextAlignment.Center,false,0,Ui.Muted)),
    new TableColumn(-1.35,DuplicateHeader("文件名","name"),o=>GroupText(((DuplicateRow)o).Group.Files[0].Name,true)),
    new TableColumn(-1,"所在账号",o=>GroupText(GroupAccounts(((DuplicateRow)o).Group),false)),
    new TableColumn(104,DuplicateHeader("大小","size"),o=>TableView.Text(Engine.Size(((DuplicateRow)o).Group.Size),TextAlignment.Right,false,14,Ui.Ink)),
    new TableColumn(74,DuplicateHeader("份数","count"),o=>TableView.Text(((DuplicateRow)o).Group.Files.Count + " 份",TextAlignment.Right,false,14,Ui.Muted)),
    new TableColumn(110,"操作",o=>TableView.Link("查看分组 ›",delegate { ShowDuplicateGroup(((DuplicateRow)o).Group); })));
   AutomationProperties.SetName(duplicateTable,"重复分组表格");
   duplicateTable.RowViewportSizeChanged += delegate(object sender, SizeChangedEventArgs e) {
    if (e.HeightChanged && duplicateTable.IsVisible) UpdateDuplicatePageSize();
   };
   Grid.SetRow(duplicateTable,1); g.Children.Add(duplicateTable);

   var footer = new Grid { Margin=new Thickness(0,10,0,0) };
   footer.ColumnDefinitions.Add(new ColumnDefinition()); footer.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   dupCount=Ui.Label("共 0 组",12,Ui.Muted,false); dupCount.Margin=new Thickness(1,0,0,0); footer.Children.Add(dupCount);
   dupPrevious=Ui.Button("上一页",delegate { MoveDuplicatePage(-1); },null);
   dupNext=Ui.Button("下一页",delegate { MoveDuplicatePage(1); },null);
   dupPageLabel=Ui.Label("1 / 1 页",12,Ui.Muted,false); dupPageLabel.Margin=new Thickness(12,0,12,0);
   var page=new StackPanel { Orientation=Orientation.Horizontal };
   page.Children.Add(dupPrevious); page.Children.Add(dupPageLabel); page.Children.Add(dupNext);
   Grid.SetColumn(page,1); footer.Children.Add(page); Grid.SetRow(footer,2); g.Children.Add(footer);
   return g;
  }
  static TextBlock GroupText(string value, bool bold) {
   var t = TableView.Text(value,TextAlignment.Left,bold,12,bold?Ui.Ink:Ui.Muted); t.ToolTip=value; return t;
  }
  sealed class DuplicateRow { public int Number; public DuplicateGroup Group; }
  #endregion

  #region settings page
  Grid BuildSettingsPage() {
   var g = WorkbenchPage("清理设置",Ui.Label("设置扫描范围和处理方式。",12,Ui.Muted,false));

   var body = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), BorderBrush = Ui.Brush("#E5EDE8"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top };
   settingsPanel = new StackPanel();
   Ui.ClipCorners(settingsPanel,11);
   body.Child = settingsPanel;
   Grid.SetRow(body, 1);
   g.Children.Add(body);
   BuildSettingsRows();
   return g;
  }

  void BuildSettingsRows() {
   settingsPanel.Children.Clear();
   var mode = new ComboBox { Width=155 };
   mode.Items.Add("移至回收站"); mode.Items.Add("直接删除");
   mode.SelectedIndex = settings.PermanentDelete ? 1 : 0;
   AutomationProperties.SetName(mode,"清理方式");
   TextBlock modeDesc;
   settingsPanel.Children.Add(PanelRow("清理方式",ModeDescription(),mode,out modeDesc));
   mode.SelectionChanged += delegate {
    if (mode.SelectedIndex<0) return;
    settings.PermanentDelete = mode.SelectedIndex==1; settings.Save(); modeDesc.Text=ModeDescription();
   };
   var keep = new ComboBox { Width=155, IsEditable=true, IsTextSearchEnabled=false };
   foreach (int d in new[]{30,90,180,365}) keep.Items.Add(AgeText(d));
   if (settings.Days>0 && !keep.Items.Contains(AgeText(settings.Days))) keep.Items.Add(AgeText(settings.Days));
   keep.Items.Add("所有时间"); keep.Items.Add("自定义…"); keep.SelectedItem=AgeText(settings.Days);
   bool changing = false;
   Action<string> saveDays = delegate(string value) {
    if (changing) return;
    int d;
    if (TryDays(value,out d) && d!=settings.Days) {
     settings.Days=d; settings.Save(); DisplayAge(d); ApplyFilter();
    }
    changing=true;
    string label=AgeText(settings.Days);
    if (!keep.Items.Contains(label)) keep.Items.Insert(keep.Items.Count-1,label);
    keep.SelectedItem=label; keep.Text=label; changing=false;
   };
   keep.SelectionChanged += delegate { if ((string)keep.SelectedItem=="自定义…") Ui.FocusEdit(keep); else if (keep.SelectedIndex>=0) saveDays(keep.SelectedItem as string); };
   Ui.ApplyOnExit(keep,delegate { saveDays(Ui.EditText(keep)); });
   AutomationProperties.SetName(keep,"默认保留时间");
   settingsPanel.Children.Add(PanelRow("默认保留时间","应用到账号页的时间筛选；也可在账号页临时更改。",keep));
   var fixedRule = Ui.Label("始终开启",12,Ui.Accent,true);
   settingsPanel.Children.Add(PanelRow("跳过目录联接","扫描和清理时跳过符号链接与目录联接，防止跨出账号目录。",fixedRule));
  }
  string ModeDescription() {
   return settings.PermanentDelete ? "文件会被永久删除。每次清理前仍会再次确认。" : "默认方式。可在 Windows 回收站中找回已回收的文件。";
  }
  Border PanelRow(string title, string desc, UIElement control) {
   TextBlock ignored;
   return PanelRow(title, desc, control, out ignored);
  }

  Border PanelRow(string title, string desc, UIElement control, out TextBlock descOut) {
   var g = new Grid { Margin = new Thickness(20,14,20,14) };
   g.ColumnDefinitions.Add(new ColumnDefinition{ Width = new GridLength(1, GridUnitType.Star) });
   g.ColumnDefinitions.Add(new ColumnDefinition{ Width = GridLength.Auto });
   var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,20,0) };
   left.Children.Add(Ui.Label(title, 13, Ui.Ink, true));
   var d = Ui.Label(desc, 11.5, Ui.Muted, false);
   descOut = d;
   d.Margin = new Thickness(0,4,0,0);
   left.Children.Add(d);
   g.Children.Add(left);
   if (control != null) {
    var fe = control as FrameworkElement;
    if (fe != null) fe.VerticalAlignment = VerticalAlignment.Center;
    Grid.SetColumn(control, 1);
    g.Children.Add(control);
   }
   return new Border { Child = g, BorderBrush = Ui.Brush("#EFF4F0"), BorderThickness = new Thickness(0,1,0,0) };
  }
  #endregion

  #region about page
  Grid BuildAboutPage() {
   var g = WorkbenchPage("关于",Ui.Label("WeChat Cleaner · 微信 / 企业微信存储清理工具",12,Ui.Muted,false));

   var card = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), BorderBrush = Ui.Brush("#E5EDE8"), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Top };
   // the card fills the page, the text inside stays in a readable column
   var inner = new StackPanel { Margin = new Thickness(30,28,30,28), MaxWidth = 960, HorizontalAlignment = HorizontalAlignment.Left };

   var brand = new StackPanel { Orientation = Orientation.Horizontal };
   var logo = BrandIcon.CreateImage(54);
   logo.VerticalAlignment = VerticalAlignment.Top;
   brand.Children.Add(logo);
   var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16,0,0,0) };
   names.Children.Add(Ui.Label("WeChat Cleaner", 18, Ui.Ink, true));
   var ver = Ui.Label("版本 " + AppVersion, 12, Ui.Accent, true);
   ver.Margin = new Thickness(0,5,0,0);
   names.Children.Add(ver);
   brand.Children.Add(names);
   inner.Children.Add(brand);

   var author = Ui.Label("作者：HaleShaw", 12, Ui.Muted, false);
   author.Margin = new Thickness(0,18,0,0);
   inner.Children.Add(author);

   const string repository = "https://github.com/HaleShaw/WeChatCleaner";
   var repositoryLine = Ui.Label("GitHub 仓库：", 12, Ui.Muted, false);
   repositoryLine.Margin = new Thickness(0,6,0,0);
   var repositoryLink = new Hyperlink(new Run(repository)) {
    NavigateUri = new Uri(repository), Foreground = Ui.Accent
   };
   repositoryLink.RequestNavigate += (sender, e) => {
    e.Handled = true;
    try { Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true }); }
    catch (Exception) { Toast("无法打开 GitHub 仓库，请检查默认浏览器设置。"); }
   };
   repositoryLine.Inlines.Add(repositoryLink);
   inner.Children.Add(repositoryLine);

   inner.Children.Add(AboutBlock("它能做什么", new string[]{
    "支持旧版微信 3.x、新版微信 4.x 与企业微信。可从现有本地资料识别昵称和企业名称。",
    "按文件类型统计占用，可按时间与大小筛选，把不需要的聊天附件和缓存移入回收站。",
    "跨账号查找内容完全相同的重复文件，逐条预览后再决定保留哪一份。",
    "支持绿色版与自定义存储目录，不写入注册表。",
   }));
   inner.Children.Add(AboutBlock("隐私与安全", new string[]{
    "文件扫描与统计在本机完成。",
    "应用不会上传文件或扫描结果。",
    "不读取微信进程内存、不注入微信进程，只读取磁盘上的数据文件。",
    "默认移入 Windows 回收站；也可以在「清理设置」里改为直接删除。",
    "删除前会逐个复核路径边界、文件大小与修改时间。",
   }));
   var note = Ui.Label("本工具只统计与清理文件，不修改微信本身的数据格式。", 11, Ui.Muted2, false);
   note.Margin = new Thickness(0,20,0,0);
   inner.Children.Add(note);

   card.Child = inner;
   var scroll = new ScrollViewer { Content=card, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };
   Grid.SetRow(scroll, 1);
   g.Children.Add(scroll);
   return g;
  }

  /// One titled block of bullet points on the about page.
  StackPanel AboutBlock(string title, string[] lines) {
   var sp = new StackPanel { Margin = new Thickness(0,24,0,0) };
   sp.Children.Add(Ui.Label(title, 13, Ui.Ink, true));
   foreach (string line in lines) {
    var t = Ui.Label("·　" + line, 12, Ui.Muted, false);
    t.Margin = new Thickness(0,8,0,0);
    t.TextWrapping = TextWrapping.Wrap;
    sp.Children.Add(t);
   }
   return sp;
  }
  #endregion

  #region pages
  void ShowPage(string name) {
   foreach (var kv in pageMap) kv.Value.Visibility = (kv.Key==name) ? Visibility.Visible : Visibility.Collapsed;
   foreach (var kv in navButtons) {
    bool active = kv.Key==name;
    kv.Value.Background = active ? Ui.Brush("#E2F3E9") : Brushes.Transparent;
    var panel = kv.Value.Content as StackPanel;
    if (panel != null) {
     var icon = panel.Children[0] as TextBlock;
     var label = panel.Children[1] as TextBlock;
     if (icon != null) icon.Foreground = active ? Ui.Accent : Ui.Muted;
     if (label != null) { label.Foreground = active ? Ui.Brush("#11784E") : Ui.Ink; label.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal; }
    }
   }
  }
  #endregion

  #region sources & accounts
  static string AccountKey(Account account) { return account==null ? null : ((int)account.Kind) + "|" + account.Path; }
  static bool SamePath(string a,string b) { return String.Equals(a,b,StringComparison.OrdinalIgnoreCase); }

  async void SearchAtStartup() {
   var search = new CancellationTokenSource(); startupSearch = search;
   var token = search.Token;
   var saved = sources.ToArray();
   List<Account> found;
   try {
    found = await Task.Run(delegate {
     DiscoveryLocations locations = null;
     try { if (discoveryLocations!=null) locations=discoveryLocations(); }
     catch (Exception ex) { DebugLog("Windows 目录查找失败："+ex.GetType().Name); }
     return SourceDiscovery.Find(saved,locations,token);
    });
   } catch (Exception ex) {
    DebugLog("启动查找数据源失败："+ex.GetType().Name);
    found = new List<Account>();
   } finally { startupSearch=null; search.Dispose(); }
   if (token.IsCancellationRequested || !IsLoaded) return;

   discovering=false;
   var seen=new HashSet<string>(accounts.Select(AccountKey),StringComparer.OrdinalIgnoreCase);
   foreach (var account in found) if (seen.Add(AccountKey(account))) accounts.Add(account);
   bool selectInitial = current==null;
   if (selectInitial) {
    current=accounts.FirstOrDefault(a=>SamePath(AccountKey(a),settings.LastAccount)) ?? accounts.FirstOrDefault();
    if (current!=null) scans.TryGetValue(AccountKey(current),out result);
   }
   RefreshTree(); RefreshOverview();
   StartNameResolution(); RefreshClientInfo();
   if (selectInitial) ScanIfNeeded();
  }

  void RefreshTree() {
   treePanel.Children.Clear();
   var groups = accounts.Select(a=>a.Kind).Distinct().OrderBy(k=>k).ToList();
   if (groups.Count == 0) {
    var empty = Ui.Label(discovering ? "正在查找本机数据源…" : "未发现账号，点击 ＋ 手动添加", 11.5, Ui.Muted2, false);
    empty.Margin = new Thickness(10,6,0,0);
    treePanel.Children.Add(empty);
    return;
   }
   foreach (var kind in groups) {
    var list = accounts.Where(a=>a.Kind==kind).ToList();
    var gh = new Grid { Margin = new Thickness(10,12,10,6) };
    gh.Children.Add(SectionLabel("▾  " + list[0].Group));
    var cnt = Ui.Label(list.Count + " 个账号", 10.5, Ui.Muted2, false);
    cnt.HorizontalAlignment = HorizontalAlignment.Right;
    gh.Children.Add(cnt);
    treePanel.Children.Add(gh);
    foreach (var a in list) {
     var content = new Grid();
     content.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });
     content.RowDefinitions.Add(new RowDefinition{ Height = GridLength.Auto });
     var top = new StackPanel { Orientation = Orientation.Horizontal };
     var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = a.Kind==SourceKind.Work ? Ui.Brush("#BE852B") : Ui.Accent, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0,0,9,0) };
     top.Children.Add(dot);
     var nm = Ui.Label(a.DisplayName, 12.5, Ui.Ink, false);
     nm.TextTrimming = TextTrimming.CharacterEllipsis;
     top.Children.Add(nm);
     Grid.SetRow(top, 0);
     content.Children.Add(top);
     var sub = Ui.Label(a.SubLabel, 10.5, Ui.Muted2, false);
     sub.Margin = new Thickness(17,2,0,0);
     sub.TextTrimming = TextTrimming.CharacterEllipsis;
     Grid.SetRow(sub, 1);
     content.Children.Add(sub);

     var btn = new Button { Content = content, Style = (Style)FindResource("NavButton") };
     btn.Padding = new Thickness(10,7,10,7);
     btn.Margin = new Thickness(0,0,0,1);
     btn.HorizontalContentAlignment = HorizontalAlignment.Left;
     bool isCurrent = SamePath(AccountKey(current),AccountKey(a));
     if (isCurrent) btn.Background = Ui.Brush("#E7F4ED");
     btn.ToolTip = a.Path + "\n右键可以打开目录、复制路径";
     btn.ContextMenu = BuildAccountMenu(a);
     btn.Click += delegate { SelectAccount(a); };
     treePanel.Children.Add(btn);
    }
   }
  }

  ContextMenu BuildAccountMenu(Account a) {
   var menu = new ContextMenu();
   menu.Items.Add(MenuAction("↗", "打开所在文件夹", delegate {
    try { Process.Start("explorer.exe", "\"" + a.Path + "\""); } catch (Exception ex) { Toast(ex.Message); }
   }));
   menu.Items.Add(MenuAction("⧉", "复制完整路径", delegate {
    try { Clipboard.SetText(a.Path); Toast("已复制完整路径"); } catch (Exception ex) { Toast(ex.Message); }
   }));
   return menu;
  }
  MenuItem MenuAction(string icon, string header, Action click) {
   var mi = new MenuItem { Header = header, Icon = icon }; mi.Click += delegate { click(); }; return mi;
  }
  void SelectAccount(Account a) {
   var key = AccountKey(a);
   bool changed = !SamePath(AccountKey(current),key);
   if (changed) {
    CancelScan(); scanStatus = ""; clientInfo = "";
    current = a; scans.TryGetValue(key,out result);
    settings.LastAccount=key; settings.Save();
   }
   ShowPage("account");
   ApplyFilter(); RefreshTree(); RefreshOverview(); RefreshClientInfo();
   if (changed) ScanIfNeeded();
  }
  void AddSource() {
   var body = new StackPanel();
   var typeBox = new ComboBox { Width=230, HorizontalAlignment=HorizontalAlignment.Left };
   typeBox.Items.Add("个人微信 · 旧版"); typeBox.Items.Add("个人微信 · 新版"); typeBox.Items.Add("企业微信");
   typeBox.SelectedIndex=0; AutomationProperties.SetName(typeBox,"来源类型");
   body.Children.Add(Ui.Label("来源类型",12,Ui.Ink,true));
   typeBox.Margin=new Thickness(0,6,0,0); body.Children.Add(typeBox);
   body.Children.Add(FieldLabel("数据目录"));
   var row=new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
   var pathBox=new TextBox { VerticalAlignment=VerticalAlignment.Center }; AutomationProperties.SetName(pathBox,"数据目录"); row.Children.Add(pathBox);
   ModalWindow modal = null;
   var browse=Ui.Button("▱  选择目录",delegate {
    try {
     using (var dlg = new System.Windows.Forms.FolderBrowserDialog()) {
      dlg.Description="选择微信或企业微信的数据目录"; dlg.ShowNewFolderButton=false;
      dlg.SelectedPath=DataFolderStartPath(pathBox.Text);
      if (dlg.ShowDialog(new FolderOwner(modal))==System.Windows.Forms.DialogResult.OK) pathBox.Text=dlg.SelectedPath;
     }
    } catch (Exception ex) { modal.Notice("无法打开目录选择器：" + ex.Message); }
   },null);
   var folder=new StackPanel { Orientation=Orientation.Horizontal };
   folder.Children.Add(new System.Windows.Shapes.Path { Data=Geometry.Parse("M1,3 L6,3 8,5 15,5 15,13 1,13 Z"),
    Stroke=Ui.Accent, StrokeThickness=1.3, StrokeLineJoin=PenLineJoin.Round, Width=16, Height=14,
    VerticalAlignment=VerticalAlignment.Center, Margin=new Thickness(0,0,6,0) });
   folder.Children.Add(Ui.Label("选择目录",12,Ui.Ink,true)); browse.Content=folder;
   AutomationProperties.SetName(browse,"选择目录");
   browse.Margin=new Thickness(8,0,0,0); Grid.SetColumn(browse,1); row.Children.Add(browse); body.Children.Add(row);
   var tip=Ui.Label("可手动输入或选择目录。目录下的多个账号会自动展开。",11.5,Ui.Muted,false);
   tip.Margin=new Thickness(0,8,0,0); body.Children.Add(tip);
   var error=Ui.Label("",11.5,Ui.Danger,false); error.Margin=new Thickness(0,8,0,0); body.Children.Add(error);
   var cancel=Ui.Button("取消",null,null); cancel.IsCancel=true;
   var ok=Ui.Button("识别并添加",null,"PrimaryButton"); ok.IsDefault=true;
   modal=new ModalWindow(this,"添加数据源",body,Ui.ButtonRow(cancel,ok),560,320,true);
   Ui.FocusFirst(modal,pathBox);
   ok.Click += delegate {
    try {
     string text=(pathBox.Text??"").Trim().Trim('"');
     if (text.Length==0) { error.Text="请先输入或选择数据目录。"; return; }
     if (!IsFullyQualified(text) || !Directory.Exists(text)) { error.Text="请输入可访问目录的完整路径。"; return; }
     string path=Engine.NormalPath(text);
     var kind=(SourceKind)typeBox.SelectedIndex;
     if (sources.Any(s=>s.Kind==kind&&SamePath(Engine.NormalPath(s.Path),path))) { error.Text="该数据源已添加。"; return; }
     var found=Engine.Discover(path,kind);
     if (found.Count==0) { error.Text="未找到该类型的账号。请检查来源类型及目录。"; return; }
     var existing=new HashSet<string>(accounts.Select(AccountKey),StringComparer.OrdinalIgnoreCase);
     var added=found.Where(a=>!existing.Contains(AccountKey(a))).ToList();
     var source=new SourceEntry { Kind=kind,Path=path }; sources.Add(source);
     if (!SourceStore.Save(sources)) { sources.Remove(source); error.Text="无法保存数据源配置。"; return; }
     accounts.AddRange(added);
     modal.DialogResult=true;
     SelectAccount(added.Count>0 ? added[0] : accounts.First(a=>SamePath(AccountKey(a),AccountKey(found[0]))));
     if (added.Count>0) StartNameResolution();
     Toast(added.Count==0 ? "已保存数据源" : "已添加 " + added.Count + " 个账号");
    } catch (Exception ex) { error.Text="无法添加数据源：" + ex.Message; }
   };
   modal.ShowDialog();
  }
  internal static string DataFolderStartPath(string enteredPath) {
   string path=(enteredPath??"").Trim().Trim('"');
   if (Directory.Exists(path) && IsFullyQualified(path)) return Path.GetFullPath(path);
   // Use the Windows known folder, which can be redirected to another drive.
   return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
  }
  static bool IsFullyQualified(string path) {
   var root=Path.GetPathRoot(path.Replace('/','\\'));
   return !String.IsNullOrEmpty(root) && (root.StartsWith("\\\\",StringComparison.Ordinal) || root.EndsWith("\\",StringComparison.Ordinal) && root.Length==3);
  }
  sealed class FolderOwner : System.Windows.Forms.IWin32Window {
   readonly Window window;
   internal FolderOwner(Window owner) { window=owner; }
   public IntPtr Handle { get { return new System.Windows.Interop.WindowInteropHelper(window).Handle; } }
  }

  TextBlock FieldLabel(string text) {
   var t = Ui.Label(text, 12, Ui.Ink, true);
   t.Margin = new Thickness(0,16,0,6);
   return t;
  }
  #endregion

  #region scanning
  void ScanIfNeeded() {
   if (current!=null && result==null && cancel==null) StartScan();
  }
  async void StartScan() {
   if (current==null) { AddSource(); return; }
   if (cancel!=null) { CancelScan(); return; }
   var acct=current; var key=AccountKey(acct);
   var myScan=new CancellationTokenSource(); cancel=myScan;
   progress.IsIndeterminate=true; UpdateScanUi("正在扫描…");
   if (result==null) RefreshOverview();
   try {
    var r=await Task.Run(delegate {
     return Engine.Scan(acct,delegate { return myScan.IsCancellationRequested; },delegate(int n) {
      Dispatcher.BeginInvoke(new Action(delegate { if (cancel==myScan) UpdateScanUi("已扫描 " + n + " 个文件"); }));
     });
    });
    if (cancel!=myScan || !SamePath(AccountKey(current),key)) return;
    cancel=null;
    if (r.Cancelled) { UpdateScanUi("扫描已取消"); return; }
    if (result!=null) {
     var selected=new HashSet<string>(result.Categories.Where(c=>c.Selected).Select(c=>c.RelativePath),StringComparer.OrdinalIgnoreCase);
     var excluded=new HashSet<string>(result.Categories.SelectMany(c=>c.Files).Where(f=>f.Excluded).Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
     foreach (var c in r.Categories) { c.Selected=selected.Contains(c.RelativePath); foreach (var f in c.Files) f.Excluded=excluded.Contains(f.Path); }
    }
    result=r; scans[key]=r; ApplyFilter(); RefreshOverview();
    UpdateScanUi("扫描完成"+(r.Errors.Count==0?"":" · 跳过或无法读取 " + r.Errors.Count + " 处"));
    if (r.Errors.Count>0) statusText.ToolTip=String.Join("\n",r.Errors.Take(8).ToArray());
   } catch (Exception ex) {
    if (cancel==myScan) { cancel=null; UpdateScanUi("扫描失败"); if (result==null) RefreshOverview(); Toast("扫描失败：" + ex.Message); }
   } finally { myScan.Dispose(); }
  }
  void CancelScan() {
   if (cancel==null) return;
   var c=cancel; cancel=null; c.Cancel(); UpdateScanUi("扫描已取消");
   if (result==null && IsLoaded) RefreshOverview();
  }
  void UpdateScanUi(string status) {
   scanStatus=status; statusText.Text=status; statusText.ToolTip=status;
   scanBtn.Content=cancel!=null ? "■  停止扫描" : current==null ? "＋  添加数据源" : result==null ? "开始扫描" : "↻  重新扫描";
   progress.Visibility=cancel==null ? Visibility.Collapsed : Visibility.Visible;
  }
  static string AgeText(int days) { return days==0 ? "所有时间" : "超过 " + days + " 天"; }
  static bool TryDays(string text,out int days) {
   text=(text??"").Trim();
   if (text=="所有时间") { days=0; return true; }
   return Int32.TryParse(text.Replace("超过","").Replace("天","").Trim(),out days) && days>0 && days<=36500;
  }
  void DisplayAge(int days) {
   changingAge=true; filterDays=days;
   string label=AgeText(days);
   if (!ageBox.Items.Contains(label)) ageBox.Items.Insert(ageBox.Items.Count-1,label);
   ageBox.SelectedItem=label; ageBox.Text=label; changingAge=false;
  }
  void CommitAge() {
   int days;
   string text=Ui.EditText(ageBox);
   if (TryDays(text,out days) && days!=filterDays) { DisplayAge(days); ApplyFilter(); }
   else { if (!TryDays(text,out days) && text!="自定义…") Toast("请输入 1 到 36500 之间的天数"); DisplayAge(filterDays); }
  }
  void ApplyFilter() {
   if (result==null) return;
   long min=0;
   if (sizeBox.SelectedIndex==1) min=10L*1024*1024;
   else if (sizeBox.SelectedIndex==2) min=100L*1024*1024;
   else if (sizeBox.SelectedIndex==3) min=1024L*1024*1024;
   Engine.Filter(result,filterDays,min,dateBox.SelectedIndex==1);
   UpdateStatistics();
  }
  #endregion

  #region overview refresh
  void RefreshOverview() {
   if (current==null) {
    sourceIcon.Text="—"; sourceTitle.Text=discovering ? "正在查找数据源" : "尚未找到数据源";
    sourcePath.Text=discovering ? "正在识别本机微信和企业微信目录，也可以手动添加。" : "点击左侧 ＋ 添加微信或企业微信的数据目录";
    sourcePath.ToolTip=null;
    sourceStatus.Text=discovering ? "查找中" : "待添加";
   } else {
    sourceIcon.Text=current.Kind==SourceKind.Work ? "企" : "微";
    sourceTitle.Text=current.ShortGroup + " · " + current.DisplayName + (String.IsNullOrEmpty(current.CorpName)?"":" · " + current.CorpName);
    sourceTitle.ToolTip=sourceTitle.Text; sourcePath.Text=current.Path;
    sourcePath.ToolTip=current.Path + "\n昵称：" + (current.HasName ? current.Nickname : "未识别，使用目录标识");
    sourceStatus.Text=current.HasName ? "目录已确认" : "昵称未识别";
   }
   var colors=(Border)sourceStatus.Parent;
   colors.Background=Ui.Brush(current!=null&&!current.HasName ? "#FFF5E5" : "#EDF8F1");
   sourceStatus.Foreground=Ui.Brush(current!=null&&!current.HasName ? "#855116" : "#285E49");
   if (shownResult!=result || result==null) {
    shownResult=result;
    categoryTable.SetRows(result==null ? Enumerable.Empty<Category>() : result.Categories,
     current==null ? (discovering ? "找到账号后将自动扫描" : "添加数据源后将自动扫描") :
      (cancel!=null ? "正在读取该账号的文件…" : "点击「开始扫描」查看该账号的存储占用"),true);
   }
   UpdateStatistics(); UpdateScanUi(scanStatus); UpdateBanner();
  }
  void UpdateStatistics() {
   totalStat.Text=result==null ? "—" : Engine.Size(result.TotalBytes);
   countStat.Text=result==null ? "" : "· " + result.TotalCount + " 个文件";
   scopeStat.Text=result==null ? "—" : Engine.Size(result.MatchedBytes);
   hitCount.Text=result==null ? "" : "· " + result.MatchedCount + " 个文件";
   UpdateSelection();
  }
  async void RefreshClientInfo() {
   var acct=current;
   if (acct==null) { clientInfo=""; return; }
   string info=await Task.Run(delegate { try { return Engine.ClientRunningInfo(acct.Kind); } catch { return ""; } });
   if (!IsLoaded || !SamePath(AccountKey(acct),AccountKey(current))) return;
   clientInfo=info; UpdateBanner();
  }
  void UpdateBanner() {
   bool visible=current!=null && !String.IsNullOrEmpty(clientInfo);
   bannerBox.Visibility=visible ? Visibility.Visible : Visibility.Collapsed;
   if (visible) {
    bannerText.Text="●  " + Engine.ClientName(current.Kind) + "正在运行，清理前建议先退出客户端。";
    bannerText.ToolTip=clientInfo;
   }
  }
  void UpdateSelection() {
   var selected=result==null ? new List<Category>() : result.Categories.Where(c=>c.Selected && !c.Protected).ToList();
   var pending=result==null ? new List<Entry>() : result.Pending;
   long bytes=pending.Sum(f=>f.Size); int files=pending.Count;
   selectedStat.Text=Engine.Size(bytes); selectedCount.Text=files==0 ? "" : "· " + files + " 个文件";
   bottomText.Text=selected.Count==0 ? "勾选需要清理的分类后，点击「清理」" :
    "已选择 " + selected.Count + " 个分类 · " + files + " 个文件 · 预计处理 " + Engine.Size(bytes);
   clearBtn.IsEnabled=selected.Count>0; reviewBtn.IsEnabled=files>0;
  }
  void ClearSelection() {
   if (result==null) return;
   foreach (var c in result.Categories) c.Selected=false;
   UpdateSelection();
  }
  #endregion

  #region details & cleanup
  void OpenDetail(Category c) {
   if (result==null || current==null) return;
   CancelScan();
   new DetailDialog(this,current,c,settings.PageSize,settings.PermanentDelete,
    delegate(int n) { settings.PageSize=n; settings.Save(); },
    delegate(IEnumerable<string> paths) { RemoveDuplicatePaths(paths); ApplyFilter(); },OnCleanup).ShowDialog();
   UpdateSelection();
  }
  void ConfirmClean() {
   if (result==null || current==null) return;
   CancelScan();
   var pending=result.Pending;
   if (pending.Count==0) { Toast("没有待清理的文件"); return; }
   var report=CleanupUi.Run(this,pending,settings.PermanentDelete,OnCleanup);
   if (report!=null) Toast(report.Summary);
  }
  void OnCleanup(Engine.RecycleReport report) {
   if (report.Removed.Count==0) return;
   CancelScan();
   var removed=new HashSet<string>(report.Removed.Select(f=>f.Path),StringComparer.OrdinalIgnoreCase);
   foreach (var r in scans.Values) foreach (var c in r.Categories) c.Files.RemoveAll(f=>removed.Contains(f.Path));
   RemoveDuplicatePaths(removed);
   ApplyFilter(); UpdateScanUi("清理后已更新统计");
  }
  void RemoveDuplicatePaths(IEnumerable<string> paths) {
   if (dupResult==null) return;
   var removed=new HashSet<string>(paths,StringComparer.OrdinalIgnoreCase);
   if (removed.Count==0) return;
   // Remove members before pruning groups so an open group window sees the change too.
   foreach (var group in dupResult.Groups) group.Files.RemoveAll(f=>removed.Contains(f.Path));
   dupResult.Groups.RemoveAll(g=>g.Files.Count<2);
   RefreshDuplicatesPage(false);
  }
  #endregion

  #region duplicates
  void ScanDuplicates() {
   if (duplicateBusy) return;
   if (accounts.Count==0) { Toast("请先添加数据源"); return; }
   var snapshot=accounts.ToArray(); duplicateBusy=true; duplicateSearch.IsEnabled=false;
   try {
    bool retry;
    do {
     retry=false;
     try {
      dupResult=Ui.Progress(this,"查找重复文件","正在比较 ≥ 1 MB 的文件：先按大小归组，再核对文件内容。",delegate {
       var files=new List<Entry>(); var issues=new List<string>(); int accessible=0;
       foreach (var a in snapshot) {
        if (!Directory.Exists(a.Path) || !Engine.LinkFree(a.Path)) { issues.Add("不可访问：" + a.Path); continue; }
        accessible++;
        var r=Engine.Scan(a,delegate { return false; },delegate(int n) {});
        issues.AddRange(r.Errors);
        foreach (var c in r.Categories) if (!c.Protected) files.AddRange(c.Files);
       }
       if (accessible==0) throw new IOException("没有可以读取的账号目录。请检查数据源或磁盘是否可用。");
       var res=DuplicateScanner.Scan(files,delegate { return false; },delegate(int n) {},1024L*1024);
       res.Errors.AddRange(issues); return res;
      },false);
      dupPageAnchor=0; dupSortKey="count"; dupSortDesc=true;
      RefreshDuplicatesPage(true);
      Toast("找到 " + dupResult.Groups.Count + " 组重复文件");
     } catch (Exception ex) {
      var close=Ui.Button("关闭",null,null); close.IsCancel=true;
      var again=Ui.Button("重试",null,"PrimaryButton");
      var dialog=new ModalWindow(this,"查找未完成",Ui.Label(ex.Message,13,Ui.Ink,false),Ui.ButtonRow(close,again),520,245,true);
      again.Click+=delegate { dialog.DialogResult=true; };
      retry=dialog.ShowDialog()==true;
     }
    } while (retry);
   } finally { duplicateBusy=false; duplicateSearch.IsEnabled=true; }
  }
  FrameworkElement DuplicateHeader(string text,string key) {
   TextBlock mark;
   var button=TableView.SortHeader(text,delegate {
    if (key==dupSortKey) dupSortDesc=!dupSortDesc; else { dupSortKey=key; dupSortDesc=key!="name"; }
    dupPageAnchor=0; RefreshDuplicatesPage(true);
   },out mark);
   dupSortMarks[key]=mark; return button;
  }
  static int CompareGroups(DuplicateGroup a,DuplicateGroup b,string key) {
   if (key=="count") return a.Files.Count.CompareTo(b.Files.Count);
   if (key=="size") return a.Size.CompareTo(b.Size);
   return StringComparer.CurrentCultureIgnoreCase.Compare(a.Files[0].Name,b.Files[0].Name);
  }
  void UpdateDuplicatePageSize() {
   int capacity=duplicateTable.VisibleRowCapacity;
   if (capacity==0 || capacity==dupPageSize) return;
   dupPageSize=capacity;
   if (dupResult!=null) RefreshDuplicatesPage(true);
  }
  void MoveDuplicatePage(int step) {
   dupPageAnchor=Math.Max(0,dupPage+step)*dupPageSize;
   RefreshDuplicatesPage(true);
  }
  void RefreshDuplicatesPage() { RefreshDuplicatesPage(true); }
  void RefreshDuplicatesPage(bool resetScroll) {
   var groups=dupResult==null ? new List<DuplicateGroup>() : dupResult.Groups.ToList();
   groups.Sort(delegate(DuplicateGroup a,DuplicateGroup b) {
    int primary=CompareGroups(a,b,dupSortKey);
    if (primary!=0) return dupSortDesc ? -primary : primary;
    int size=b.Size.CompareTo(a.Size); if (size!=0) return size;
    return StringComparer.OrdinalIgnoreCase.Compare(a.Files[0].Path,b.Files[0].Path);
   });
   foreach (var pair in dupSortMarks) pair.Value.Text=pair.Key==dupSortKey ? (dupSortDesc ? "▼" : "▲") : "";
   int pages=groups.Count==0 ? 1 : 1+(groups.Count-1)/dupPageSize;
   // The anchor only moves on navigation, not during successive resize events.
   dupPageAnchor=Math.Min(dupPageAnchor,Math.Max(0,groups.Count-1));
   dupPage=dupPageAnchor/dupPageSize;
   int start=dupPage*dupPageSize;
   dupCount.Text="共 " + groups.Count + " 组"; dupPageLabel.Text=(dupPage+1)+" / " + pages + " 页";
   dupPrevious.IsEnabled=dupPage>0; dupNext.IsEnabled=dupPage<pages-1;
   if (dupResult!=null) {
    dupSummary.Text="比较 " + dupResult.Scanned + " 个文件 · 候选 " + dupResult.Candidates + " 个 · 最多可释放 " + Engine.Size(dupResult.WastedTotal) +
     (dupResult.Errors.Count==0 ? "" : " · 跳过或无法读取 " + dupResult.Errors.Count + " 处");
    dupSummary.ToolTip=dupResult.Errors.Count==0 ? null : String.Join("\n",dupResult.Errors.Take(8).ToArray());
   }
   duplicateTable.SetRows(groups.Skip(start).Take(dupPageSize).Select((g,i)=>new DuplicateRow { Group=g,Number=start+i+1 }),
    dupResult==null ? "点击「查找重复文件」开始比较" : "没有发现内容完全相同的文件",resetScroll);
  }
  string OwnerName(Entry file) {
   var a=accounts.FirstOrDefault(x=>x.Kind==file.OwnerKind&&SamePath(x.Path,file.OwnerRoot));
   return a==null ? Path.GetFileName(file.OwnerRoot) : a.DisplayName + (String.IsNullOrEmpty(a.CorpName) ? "" : " · " + a.CorpName);
  }
  string GroupAccounts(DuplicateGroup group) {
   return String.Join("、",group.Files.Select(OwnerName).Distinct().ToArray());
  }
  void ShowDuplicateGroup(DuplicateGroup group) {
   new DuplicateGroupDialog(this,group,settings.PermanentDelete,OwnerName,OnCleanup).ShowDialog();
  }
  #endregion

  #region name resolution
  int namingGeneration;
  async void StartNameResolution() {
   var snapshot=accounts.ToArray(); int generation=++namingGeneration;
   var resolved=await Task.Run(delegate {
    var names=new Dictionary<Account,Identity>();
    foreach (var account in snapshot) {
     try { names[account]=NameResolver.Resolve(account); } catch (Exception ex) { DebugLog("名称识别失败：" + ex.GetType().Name); }
    }
    return names;
   });
   if (generation!=namingGeneration || !IsLoaded) return;
   foreach (var kv in resolved) {
    if (kv.Value==null) continue;
    kv.Key.Nickname=kv.Value.Nickname; kv.Key.CorpName=kv.Value.CorpName; kv.Key.NameSource=kv.Value.Source;
   }
   RefreshTree(); RefreshOverview(); RefreshDuplicatesPage(false);
  }
  #endregion

  #region diagnostics
  internal static void DebugLog(string message) {
   try {
    string dir = SourceStore.Folder;
    System.IO.Directory.CreateDirectory(dir);
    string file = System.IO.Path.Combine(dir, "debug.log");
    try { if (new FileInfo(file).Length > 256*1024) File.WriteAllText(file, ""); } catch {}
    System.IO.File.AppendAllText(file, DateTime.Now.ToString("HH:mm:ss") + " " + message + Environment.NewLine, Encoding.UTF8);
   } catch {}
  }
  #endregion

  #region toast
  void Toast(string message) {
   toastText.Text = message;
   toastBorder.Visibility = Visibility.Visible;
   toastBorder.Opacity = 1;
   toastTimer.Stop();
   toastTimer.Start();
  }
  #endregion
 }
}
