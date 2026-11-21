using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace WeChatCleaner {
 public class ModalWindow : Window {
  readonly DialogToast notice = new DialogToast();
  readonly TextBlock subtitle;
  public ModalWindow(Window owner, string title, UIElement body, UIElement footer, double width, double height, bool showClose)
   : this(owner, title, body, footer, width, height, showClose, false, true) { }

  public ModalWindow(Window owner, string title, UIElement body, UIElement footer, double width, double height, bool showClose, bool wideFooter, bool bodyScrolls) {
   Title = title;
   if (owner != null && owner.IsLoaded) Owner = owner;
   WindowStyle = WindowStyle.None;
   AllowsTransparency = true; Background = Brushes.Transparent; ShowInTaskbar = false;
   UseLayoutRounding = true; SnapsToDevicePixels = true;
   ResizeMode = ResizeMode.NoResize;
   WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
   Fit(owner, width, height, 400, 180);
   if (!showClose && footer == null) { SizeToContent = SizeToContent.Height; MinHeight = 0; }

   var grid = new Grid();
   grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
   grid.RowDefinitions.Add(new RowDefinition());
   if (footer != null) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

   var head = new Grid { Background = Brushes.Transparent };
   head.ColumnDefinitions.Add(new ColumnDefinition());
   head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   var titles = new StackPanel { Margin = new Thickness(20,14,16,13), VerticalAlignment = VerticalAlignment.Center };
   var titleText = Ui.Label(title, 17, Ui.Ink, true);
   titleText.TextWrapping = TextWrapping.NoWrap; titleText.TextTrimming = TextTrimming.CharacterEllipsis;
   titles.Children.Add(titleText);
   subtitle = Ui.Label("", 11.5, Ui.Muted, false);
   subtitle.Margin = new Thickness(0,4,0,0); subtitle.TextTrimming = TextTrimming.CharacterEllipsis;
   subtitle.TextWrapping = TextWrapping.NoWrap; subtitle.Visibility = Visibility.Collapsed;
   titles.Children.Add(subtitle); head.Children.Add(titles);
   if (showClose) {
    var close = Ui.Button("✕", Close, "CaptionCloseButton");
    close.Margin = new Thickness(0,3,3,0); close.VerticalAlignment = VerticalAlignment.Top;
    close.ToolTip = "关闭"; AutomationProperties.SetName(close, "关闭窗口");
    Grid.SetColumn(close,1); head.Children.Add(close);
   }
   head.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) {
    if (e.ButtonState == MouseButtonState.Pressed) try { DragMove(); } catch (InvalidOperationException) { }
   };
   grid.Children.Add(new Border { Child = head, BorderBrush = Ui.Line, BorderThickness = new Thickness(0,0,0,1) });

   UIElement content = body;
   if (bodyScrolls) content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
   var bodyBorder = new Border { Child = content, Padding = new Thickness(20,14,20,footer == null ? 22 : 12) };
   Grid.SetRow(bodyBorder,1); grid.Children.Add(bodyBorder);
   if (footer != null) {
    var foot = new Border { Child = footer, BorderBrush = Ui.Line, BorderThickness = new Thickness(0,1,0,0),
     Padding = new Thickness(20,10,20,12), Background = Ui.Brush("#FCFDFC") };
    if (!wideFooter) ((FrameworkElement)footer).HorizontalAlignment = HorizontalAlignment.Right;
    Grid.SetRow(foot,2); grid.Children.Add(foot);
   }
   var overlay = new Grid();
   overlay.Children.Add(grid); overlay.Children.Add(notice);
   Ui.ClipCorners(overlay,13);
   Content = new Border { Child = overlay, Background = Brushes.White, BorderBrush = Ui.Brush("#D1DED7"),
    CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Margin = new Thickness(8) };
   if (showClose) KeyDown += delegate(object sender, KeyEventArgs e) {
    if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; Close(); }
   };
   Closed += delegate { notice.Stop(); };
  }
  public void SetSubtitle(string text) {
   subtitle.Text = text; subtitle.ToolTip = text;
   subtitle.Visibility = String.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
  }
  public void Notice(string text) { notice.Show(text); }
  protected void Fit(Window owner, double width, double height, double minWidth, double minHeight) {
   var screen = owner != null && owner.IsLoaded ? System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(owner).Handle) : System.Windows.Forms.Screen.PrimaryScreen;
   var source = owner == null ? null : PresentationSource.FromVisual(owner);
   var work = screen.WorkingArea;
   Matrix dpi = source == null ? Matrix.Identity : source.CompositionTarget.TransformFromDevice;
   double w = Math.Max(320,work.Width * dpi.M11 - 20), h = Math.Max(180,work.Height * dpi.M22 - 20);
   MaxWidth = w; MaxHeight = h;
   MinWidth = Math.Min(minWidth,w); MinHeight = Math.Min(minHeight,h);
   Width = Math.Min(width,w); Height = Math.Min(height,h);
  }
  protected void AllowResize() {
   ResizeMode = ResizeMode.CanResize;
   var chrome = new System.Windows.Shell.WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(9),
    GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false };
   System.Windows.Shell.WindowChrome.SetWindowChrome(this, chrome);
  }
 }

 internal sealed class DialogToast : Border {
  readonly TextBlock text;
  readonly DispatcherTimer timer;
  internal DialogToast() {
   Background = Ui.Brush("#244437"); CornerRadius = new CornerRadius(8); Padding = new Thickness(15,9,15,9);
   HorizontalAlignment = HorizontalAlignment.Center; VerticalAlignment = VerticalAlignment.Bottom;
   Margin = new Thickness(18,0,18,101); MaxWidth = 560; IsHitTestVisible = false; Visibility = Visibility.Collapsed;
   text = Ui.Label("",12,Brushes.White,false); Child = text;
   timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.8) };
   timer.Tick += delegate { Stop(); };
  }
  internal void Show(string message) { text.Text = message; Visibility = Visibility.Visible; timer.Stop(); timer.Start(); }
  internal void Stop() { timer.Stop(); Visibility = Visibility.Collapsed; }
 }

 public static class Ui {
  public static Brush Ink { get { return Brush("#20332E"); } }
  public static Brush Muted { get { return Brush("#5E756A"); } }
  public static Brush Muted2 { get { return Brush("#687D72"); } }
  public static Brush Accent { get { return Brush("#14794F"); } }
  public static Brush AccentSoft { get { return Brush("#EAF6EF"); } }
  public static Brush Line { get { return Brush("#E0EAE4"); } }
  public static Brush Canvas { get { return Brush("#F4F8F6"); } }
  public static Brush Danger { get { return Brush("#B33E32"); } }
  public static Brush Brush(string hex) {
   var brush = (Brush)new BrushConverter().ConvertFromString(hex);
   if (brush.CanFreeze) brush.Freeze();
   return brush;
  }
  public static TextBlock Label(string text, double size, Brush color, bool bold) {
   return new TextBlock { Text = text, FontSize = size, Foreground = color,
    FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
  }
  public static Button Button(string text, Action click, string styleKey) {
   var b = new Button { Content = text };
   if (styleKey != null) b.Style = (Style)Application.Current.FindResource(styleKey);
   if (click != null) b.Click += delegate { click(); };
   return b;
  }
  public static Button ActionButton(string text, string tip, Action click) {
   var b = Button(text,click,"LinkButton");
   b.ToolTip = tip; b.FontSize = 11.5; b.Padding = new Thickness(3,2,3,2); b.Margin = new Thickness(0,0,2,0);
   b.VerticalAlignment = VerticalAlignment.Center;
   return b;
  }
  internal static void ClipCorners(FrameworkElement panel, double radius) {
   var clip = new RectangleGeometry { RadiusX = radius, RadiusY = radius };
   panel.Clip = clip;
   panel.SizeChanged += delegate { clip.Rect = new Rect(0,0,panel.ActualWidth,panel.ActualHeight); };
  }
  internal static CheckBox CenterCheck(string label) {
   var cb = new CheckBox { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
   AutomationProperties.SetName(cb,label); cb.ToolTip = label;
   return cb;
  }
  internal static MenuItem MenuItem(string text, Action action) {
   var m = new MenuItem { Header = text }; m.Click += delegate { action(); }; return m;
  }
  internal static Button MenuButton(string title, params Control[] items) {
   var menu = new ContextMenu { Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
   foreach (var item in items) menu.Items.Add(item);
   var button = Button(title + " ▾", null, null);
   button.Padding = new Thickness(9,4,9,4);
   button.ContextMenu = menu;
   button.Click += delegate { menu.PlacementTarget = button; menu.IsOpen = true; };
   return button;
  }
  internal static StackPanel ButtonRow(params Button[] buttons) {
   var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
   foreach (Button b in buttons) { if (panel.Children.Count > 0) b.Margin = new Thickness(8,0,0,0); panel.Children.Add(b); }
   return panel;
  }
  internal static void FocusFirst(Window window, Control first) {
   window.Loaded += delegate { window.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate { first.Focus(); Keyboard.Focus(first); })); };
  }
  internal static string EditText(ComboBox combo) {
   var text=combo.Template==null ? null : combo.Template.FindName("PART_EditableTextBox",combo) as TextBox;
   return text==null ? combo.Text : text.Text;
  }
  internal static void FocusEdit(ComboBox combo) {
   combo.Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(delegate {
    if (combo.SelectedItem as string != "自定义…") return;
    var text = combo.Template.FindName("PART_EditableTextBox",combo) as TextBox;
    if (text != null) { text.Focus(); text.SelectAll(); }
   }));
  }
  internal static void ApplyOnExit(ComboBox combo, Action apply) {
   combo.LostKeyboardFocus += delegate {
    combo.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate {
     if (!combo.IsKeyboardFocusWithin && !combo.IsDropDownOpen) apply();
    }));
   };
   combo.PreviewKeyDown += delegate(object sender, KeyEventArgs e) {
    if (e.Key == Key.Enter && !combo.IsDropDownOpen) { apply(); e.Handled = true; }
   };
  }
  internal static T Progress<T>(Window owner, string title, string message, Func<T> run, bool sta) {
   var body = new StackPanel();
   body.Children.Add(Label(message,12.5,Ink,false));
   body.Children.Add(new ProgressBar { Height = 6, Margin = new Thickness(0,16,0,0), IsIndeterminate = true });
   var dialog = new ModalWindow(owner,title,body,null,520,180,false,false,false);
   bool finished = false;
   T result = default(T);
   Exception failure = null;
   dialog.Closing += delegate(object sender, CancelEventArgs e) { if (!finished) e.Cancel = true; };
   dialog.Loaded += async delegate {
    try {
     if (sta) {
      var task = new TaskCompletionSource<T>();
      var thread = new Thread(delegate() { try { task.SetResult(run()); } catch (Exception ex) { task.SetException(ex); } });
      thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start();
      result = await task.Task;
     } else result = await Task.Run(run);
    } catch (Exception ex) { failure = ex; }
    finally { finished = true; dialog.Close(); }
   };
   dialog.ShowDialog();
   if (failure != null) throw new InvalidOperationException(failure.Message,failure);
   return result;
  }
 }

 internal static class CleanupUi {
  internal static Engine.RecycleReport Run(Window owner, IEnumerable<Entry> entries, bool permanent, Action<Engine.RecycleReport> update) {
   var files = entries.GroupBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
   if (files.Count == 0) return null;
   string mode = permanent ? "直接删除" : "移至回收站";
   var body = new StackPanel();
   body.Children.Add(Ui.Label("将处理 " + files.Count + " 个文件 · " + Engine.Size(files.Sum(f=>f.Size)),15,Ui.Ink,true));
   if (files.Count == 1) {
    var name = TableView.Text(files[0].Name,TextAlignment.Left,false,0,Ui.Ink);
    name.ToolTip = files[0].Path; name.Margin = new Thickness(0,8,0,0); body.Children.Add(name);
   }
   var message = permanent ? "文件将被永久删除，无法从回收站找回。" : "文件将移至 Windows 回收站。无法安全回收时，会跳过或报告失败。";
   body.Children.Add(new Border { Background = Ui.Brush(permanent ? "#FBEDE9" : "#EDF6F0"), CornerRadius = new CornerRadius(8),
    Padding = new Thickness(11,9,11,9), Margin = new Thickness(0,14,0,0), Child = Ui.Label("清理方式：" + mode + "\n" + message,12,permanent ? Ui.Danger : Ui.Ink,false) });
   var cancel = Ui.Button("取消",null,null); cancel.IsCancel = true;
   var ok = Ui.Button(permanent ? "确认永久删除" : "确认移至回收站",null,permanent ? "DangerButton" : "PrimaryButton");
   var dlg = new ModalWindow(owner,"确认清理",body,Ui.ButtonRow(cancel,ok),530,files.Count == 1 ? 285 : 260,true);
   ok.Click += delegate { dlg.DialogResult = true; };
   Ui.FocusFirst(dlg,cancel);
   if (dlg.ShowDialog() != true) return null;
   Engine.RecycleReport report;
   try {
    report = Ui.Progress(owner,"正在清理",mode + " · 共 " + files.Count + " 个文件",delegate { return Engine.DeleteFiles(files,permanent,null); },true);
   } catch (Exception ex) {
    MessageBox.Show(owner,ex.Message,"清理失败",MessageBoxButton.OK,MessageBoxImage.Warning); return null;
   }
   if (update != null) update(report);
   if (report.Errors.Count > 0) {
    var details = new StackPanel();
    details.Children.Add(Ui.Label(report.Summary,13,Ui.Ink,true));
    foreach (string issue in report.Errors.Take(30)) {
     var label = Ui.Label(issue,11.5,Ui.Muted,false); label.Margin = new Thickness(0,8,0,0); details.Children.Add(label);
    }
    if (report.Errors.Count > 30) details.Children.Add(Ui.Label("还有 " + (report.Errors.Count-30) + " 条未显示。",12,Ui.Muted,false));
    var close = Ui.Button("关闭",null,null); close.IsCancel = true;
    new ModalWindow(owner,"清理结果",details,Ui.ButtonRow(close),630,Math.Min(470,220+report.Errors.Count*36),true).ShowDialog();
   }
   return report;
  }
 }

 internal static class FileActions {
  internal static void Open(Window owner, Entry file) {
   try {
    string ext = (file.Type ?? "").ToLowerInvariant();
    if (Array.IndexOf(new[]{".exe",".dll",".msi",".lnk",".url",".scr",".cmd",".bat",".com",".ps1",".hta",".vbs",".js",".reg",".wsf"},ext) >= 0) {
     MessageBox.Show(owner,"此文件可能执行程序或脚本。可通过「打开路径」在资源管理器中查看。","无法安全预览",MessageBoxButton.OK,MessageBoxImage.Information); return;
    }
    if (!File.Exists(file.Path)) throw new IOException("文件已不存在，请刷新列表。");
    Process.Start(file.Path);
   } catch (Exception ex) { MessageBox.Show(owner,ex.Message,"无法查看文件",MessageBoxButton.OK,MessageBoxImage.Warning); }
  }
  internal static void Reveal(Window owner, Entry file) {
   try {
    if (!File.Exists(file.Path)) throw new IOException("文件已不存在，请刷新列表。");
    Process.Start("explorer.exe", "/select,\"" + file.Path + "\"");
   } catch (Exception ex) { MessageBox.Show(owner,ex.Message,"无法打开路径",MessageBoxButton.OK,MessageBoxImage.Warning); }
  }
  internal static void Copy(Window owner, Entry file, Action<string> notice) {
   try { Clipboard.SetText(file.Path); notice("已复制完整路径"); }
   catch (Exception ex) { MessageBox.Show(owner,ex.Message,"复制路径失败",MessageBoxButton.OK,MessageBoxImage.Warning); }
  }
  internal static void CopyTo(Entry file, string destination) {
   if (String.Equals(Engine.NormalPath(file.Path),Engine.NormalPath(destination),StringComparison.OrdinalIgnoreCase))
    throw new IOException("请另选保存位置，避免覆盖源文件。");
   File.Copy(file.Path,destination,true);
  }
  internal static void Save(Window owner, Entry file, Action<string> notice) {
   var dlg = new Microsoft.Win32.SaveFileDialog { FileName = file.Name, Title = "另存为", OverwritePrompt = true };
   if (dlg.ShowDialog(owner) != true) return;
   try { CopyTo(file,dlg.FileName); notice("已保存：" + Path.GetFileName(dlg.FileName)); }
   catch (Exception ex) { MessageBox.Show(owner,ex.Message,"另存为失败",MessageBoxButton.OK,MessageBoxImage.Warning); }
  }
 }

 internal sealed class FileChoice : INotifyPropertyChanged {
  internal Entry File;
  internal int Number;
  bool included;
  public bool Included {
   get { return included; }
   set { if (included == value) return; included = value; var h = PropertyChanged; if (h != null) h(this,new PropertyChangedEventArgs("Included")); }
  }
  internal CheckBox Check(bool enabled, string prefix) {
   var check = Ui.CenterCheck(prefix + File.Name);
   check.IsEnabled = enabled;
   check.SetBinding(CheckBox.IsCheckedProperty,new Binding("Included") { Source = this, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
   return check;
  }
  public event PropertyChangedEventHandler PropertyChanged;
 }
}
