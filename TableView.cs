using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace WeChatCleaner {
 internal sealed class TableColumn {
  internal readonly GridLength Width;
  internal readonly FrameworkElement Header;
  internal readonly Func<object, FrameworkElement> Cell;
  internal TableColumn(double width, FrameworkElement header, Func<object, FrameworkElement> cell) {
   Width = width < 0 ? new GridLength(-width, GridUnitType.Star) : new GridLength(width);
   Header = header; Cell = cell;
  }
  internal TableColumn(double width, string header, Func<object, FrameworkElement> cell) : this(width, TableView.Header(header), cell) { }
 }

 // Header and rows are made from the same column definitions. A virtualized
 // list owns all rows and the only vertical scroll offset. Pinned cells are
 // siblings of the clipped, horizontally translated middle cells in each row.
 internal sealed class TableView : Border {
  internal const double Gutter = 16;
  internal const double HeaderHeight = 40;
  static readonly Brush HeaderFill = Ui.Brush("#F6F9F7");
  static readonly Brush Rule = Ui.Brush("#E9F0EC");
  static readonly Brush GutterFill = Ui.Brush("#F3F7F5");
  readonly TableColumn[] columns;
  readonly int left, right;
  readonly double rowHeight, middleWidth;
  readonly TranslateTransform translation = new TranslateTransform();
  readonly Border headerGutter, overlayGutter;
  readonly ListBox body;
  readonly ScrollBar horizontal;
  readonly TextBlock empty;
  Grid headerViewport;
  ScrollViewer vertical;
  static ControlTemplate listTemplate;
  static Style rowStyle;
  static DataTemplate rowTemplate;

  internal TableView(double height, params TableColumn[] columns) : this(height, 0, 0, columns) { }
  internal TableView(double height, int pinnedLeft, int pinnedRight, params TableColumn[] columns) {
   this.columns = columns; left = pinnedLeft; right = pinnedRight; rowHeight = height;
   bool pinned = left + right > 0;
   if (pinned) middleWidth = columns.Skip(left).Take(columns.Length-left-right).Sum(c => c.Width.Value);
   UseLayoutRounding = true; SnapsToDevicePixels = true;
   CornerRadius = new CornerRadius(10); BorderBrush = Ui.Line; BorderThickness = new Thickness(1); Background = Brushes.White;

   var layout = new Grid();
   layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
   layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
   layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
   Ui.ClipCorners(layout, 9);
   Child = layout;

   var header = ViewportHost(out headerGutter);
   header.Background = HeaderFill;
   var content = ShapeRow(null, true);
   var head = new Border { Child = content, BorderBrush = Ui.Line, BorderThickness = new Thickness(0,0,0,1) };
   header.Children.Add(head);
   layout.Children.Add(header);

   BuildTemplates();
   body = new ListBox { Template = listTemplate, ItemTemplate = rowTemplate, ItemContainerStyle = rowStyle,
    BorderThickness = new Thickness(0), Padding = new Thickness(0), Background = Brushes.White, Focusable = true };
   ScrollViewer.SetCanContentScroll(body, true);
   ScrollViewer.SetHorizontalScrollBarVisibility(body, ScrollBarVisibility.Disabled);
   ScrollViewer.SetVerticalScrollBarVisibility(body, ScrollBarVisibility.Auto);
   VirtualizingPanel.SetIsVirtualizing(body, true);
   VirtualizingPanel.SetVirtualizationMode(body, VirtualizationMode.Recycling);
   VirtualizingPanel.SetScrollUnit(body, ScrollUnit.Pixel);
   AutomationProperties.SetName(body, "表格数据");
   body.Loaded += delegate { ConnectScrollViewer(); };
   body.IsVisibleChanged += delegate { if (body.IsVisible) ConnectScrollViewer(); };
   body.SizeChanged += delegate { if (body.IsVisible && vertical == null) ConnectScrollViewer(); };
   Grid.SetRow(body, 1); layout.Children.Add(body);

   empty = Ui.Label("暂无数据", 12, Ui.Muted, false);
   empty.HorizontalAlignment = HorizontalAlignment.Center; empty.TextAlignment = TextAlignment.Center;
   empty.Margin = new Thickness(18,0,18,0); empty.IsHitTestVisible = false;
   Grid.SetRow(empty, 1); layout.Children.Add(empty);

   if (pinned) {
    var overlay = ViewportHost(out overlayGutter);
    overlayGutter.Background = null;
    overlay.IsHitTestVisible = false;
    var seams = SplitFrame();
    var inner = new Grid { ClipToBounds = true };
    inner.Children.Add(Seam(true)); inner.Children.Add(Seam(false));
    Grid.SetColumn(inner,1); seams.Children.Add(inner); overlay.Children.Add(seams);
    Grid.SetRowSpan(overlay, 2); layout.Children.Add(overlay);

    horizontal = new ScrollBar { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed,
     SmallChange = 48, Background = GutterFill, Height = Gutter, MinHeight = Gutter };
    AutomationProperties.SetName(horizontal,"横向滚动表格");
    horizontal.ValueChanged += delegate { translation.X = -horizontal.Value; };
    Grid.SetRow(horizontal, 2); layout.Children.Add(horizontal);
    headerViewport.SizeChanged += delegate { UpdateHorizontal(); };
   }

   PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e) {
    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && horizontal != null && horizontal.Visibility == Visibility.Visible) {
     horizontal.Value = Math.Max(0, Math.Min(horizontal.Maximum, horizontal.Value - e.Delta)); e.Handled = true;
    } else if (vertical != null && vertical.ScrollableHeight > 0) {
     int count = SystemParameters.WheelScrollLines;
     double step = count < 0 ? vertical.ViewportHeight : rowHeight * count;
     vertical.ScrollToVerticalOffset(vertical.VerticalOffset - e.Delta / 120.0 * step);
     e.Handled = true;
    }
   };
  }

  internal event SizeChangedEventHandler RowViewportSizeChanged {
   add { body.SizeChanged += value; }
   remove { body.SizeChanged -= value; }
  }
  internal int VisibleRowCapacity {
   get {
    if (body.ActualHeight <= 0) return 0;
    // The rows have a fixed height, rounded to device pixels by WPF.
    double scale = VisualTreeHelper.GetDpi(body).DpiScaleY;
    double viewportPixels = Math.Round(body.ActualHeight * scale);
    double rowPixels = Math.Max(1,Math.Round(rowHeight * scale));
    return Math.Max(1,(int)(viewportPixels / rowPixels));
   }
  }

  void ConnectScrollViewer() {
   if (vertical != null) return;
   body.ApplyTemplate();
   vertical = body.Template.FindName("PART_TableScroll",body) as ScrollViewer;
   if (vertical == null) return;
   var binding = new Binding("ComputedVerticalScrollBarVisibility") { Source = vertical };
   headerGutter.SetBinding(VisibilityProperty,binding);
   if (overlayGutter != null) overlayGutter.SetBinding(VisibilityProperty,binding);
   UpdateHorizontal();
  }
  static Grid ViewportHost(out Border gutter) {
   var grid = new Grid();
   grid.ColumnDefinitions.Add(new ColumnDefinition());
   grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   gutter = new Border { Width = Gutter, Background = GutterFill, Visibility = Visibility.Collapsed };
   Grid.SetColumn(gutter, 1); grid.Children.Add(gutter);
   return grid;
  }
  static Border Seam(bool leftEdge) {
   var faded = new LinearGradientBrush();
   faded.StartPoint = new Point(0,0); faded.EndPoint = new Point(1,0);
   faded.GradientStops.Add(new GradientStop(Color.FromArgb(leftEdge ? (byte)26 : (byte)0, 29, 60, 46),0));
   faded.GradientStops.Add(new GradientStop(Color.FromArgb(leftEdge ? (byte)0 : (byte)26, 29, 60, 46),1));
   faded.Freeze();
   return new Border { Width = 8, Background = faded, BorderBrush = Ui.Brush("#CDDCD4"),
    BorderThickness = leftEdge ? new Thickness(1,0,0,0) : new Thickness(0,0,1,0),
    HorizontalAlignment = leftEdge ? HorizontalAlignment.Left : HorizontalAlignment.Right };
  }
  Grid SplitFrame() {
   var grid = new Grid { ClipToBounds = true };
   grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columns.Take(left).Sum(c=>c.Width.Value)) });
   grid.ColumnDefinitions.Add(new ColumnDefinition());
   grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(columns.Skip(columns.Length-right).Sum(c=>c.Width.Value)) });
   return grid;
  }
  Grid ShapeRow(object value, bool header) {
   if (left+right == 0) return Segment(value, header, 0, columns.Length);
   var grid = SplitFrame();
   grid.Children.Add(Segment(value, header, 0, left));
   var middle = new Grid { ClipToBounds = true };
   var content = Segment(value, header, left, columns.Length-left-right);
   content.Width = middleWidth; content.RenderTransform = translation;
   var surface = new Canvas { ClipToBounds = true };
   surface.Children.Add(content);
   // Canvas measures the middle at its full width, but sizes its clip with the viewport.
   content.Height = header ? HeaderHeight-1 : rowHeight-1;
   middle.Children.Add(surface);
   Grid.SetColumn(middle,1); grid.Children.Add(middle);
   var actions = Segment(value, header, columns.Length-right, right);
   Grid.SetColumn(actions,2); grid.Children.Add(actions);
   if (header) headerViewport = middle;
   return grid;
  }
  Grid Segment(object value, bool header, int start, int count) {
   var grid = new Grid { ClipToBounds = true };
   for (int i=0; i<count; i++) {
    var c = columns[start+i];
    grid.ColumnDefinitions.Add(new ColumnDefinition { Width = c.Width });
    var view = header ? c.Header : c.Cell(value);
    var cell = new Border { Child = view, ClipToBounds = true };
    Grid.SetColumn(cell,i); grid.Children.Add(cell);
   }
   return grid;
  }
  void UpdateHorizontal() {
   if (horizontal == null || headerViewport.ActualWidth <= 0) return;
   double range = Math.Max(0, middleWidth - headerViewport.ActualWidth);
   horizontal.Maximum = range;
   horizontal.ViewportSize = Math.Max(1, ActualWidth - 2);
   horizontal.LargeChange = Math.Max(48, headerViewport.ActualWidth - 48);
   horizontal.Visibility = range > 0.5 ? Visibility.Visible : Visibility.Collapsed;
  }
  internal void SetRows(IEnumerable values, string emptyMessage, bool resetScroll) {
   double offset = vertical == null ? 0 : vertical.VerticalOffset;
   var items = new List<TableValue>();
   foreach (object value in values) items.Add(new TableValue { Table = this, Value = value });
   if (items.Count > 0) items[items.Count-1].Last = true;
   body.ItemsSource = items;
   empty.Text = emptyMessage;
   empty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
   if (vertical != null) vertical.ScrollToVerticalOffset(resetScroll ? 0 : offset);
  }
  internal FrameworkElement MakeRow(TableValue item) {
   return new Border { Child = ShapeRow(item.Value, false), Height = rowHeight, Background = Brushes.Transparent,
    BorderBrush = Rule, BorderThickness = new Thickness(0,0,0,item.Last ? 0 : 1) };
  }
  internal static TextBlock Header(string title) { return Text(title, TextAlignment.Center, true, 0, Ui.Brush("#526A5E")); }
  internal static TextBlock Text(string value, TextAlignment alignment, bool emphasis, double pad, Brush brush) {
   var t = Ui.Label(value, 12, brush ?? Ui.Ink, emphasis);
   t.TextAlignment = alignment; t.TextTrimming = TextTrimming.CharacterEllipsis; t.TextWrapping = TextWrapping.NoWrap;
   t.Margin = new Thickness(pad,0,pad,0);
   return t;
  }
  internal static Button SortHeader(string title, Action click, out TextBlock mark) {
   var button = Ui.Button("", click,"QuietButton");
   button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent;
   button.Height = 32; button.MinHeight = 0; button.Padding = new Thickness(4,0,4,0);
   button.HorizontalAlignment = HorizontalAlignment.Stretch;
   var content = new Grid();
   content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
   content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
   content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(13) });
   var label = Header(title); Grid.SetColumn(label,1); content.Children.Add(label);
   mark = Ui.Label("",9,Ui.Accent,true); mark.TextAlignment = TextAlignment.Right;
   Grid.SetColumn(mark,2); content.Children.Add(mark);
   button.Content = content; button.ToolTip = "按" + title + "排序";
   AutomationProperties.SetName(button,title);
   return button;
  }
  internal static Button Link(string text, Action click) {
   var b = Ui.Button(text,click,"LinkButton"); b.HorizontalAlignment = HorizontalAlignment.Center;
   b.VerticalAlignment = VerticalAlignment.Center;
   return b;
  }

  static void BuildTemplates() {
   if (listTemplate != null) return;
   listTemplate = (ControlTemplate)XamlReader.Parse(@"
    <ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBox'>
     <ScrollViewer x:Name='PART_TableScroll' CanContentScroll='True' HorizontalScrollBarVisibility='Disabled' VerticalScrollBarVisibility='Auto' Focusable='False'>
      <ScrollViewer.Template>
       <ControlTemplate TargetType='ScrollViewer'>
        <Grid ClipToBounds='True'>
         <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/></Grid.ColumnDefinitions>
         <ScrollContentPresenter x:Name='PART_ScrollContentPresenter' CanContentScroll='{TemplateBinding CanContentScroll}' ClipToBounds='True'/>
         <ScrollBar x:Name='PART_VerticalScrollBar' Grid.Column='1' Orientation='Vertical' Width='16' MinWidth='16' Background='#F3F7F5'
          Visibility='{TemplateBinding ComputedVerticalScrollBarVisibility}' Maximum='{TemplateBinding ScrollableHeight}' Value='{Binding VerticalOffset, RelativeSource={RelativeSource TemplatedParent}, Mode=OneWay}' ViewportSize='{TemplateBinding ViewportHeight}' AutomationProperties.Name='纵向滚动表格'/>
        </Grid>
       </ControlTemplate>
      </ScrollViewer.Template>
      <ItemsPresenter/>
     </ScrollViewer>
    </ControlTemplate>");
   rowStyle = (Style)XamlReader.Parse(@"
    <Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ListBoxItem'>
     <Setter Property='Background' Value='Transparent'/>
     <Setter Property='HorizontalContentAlignment' Value='Stretch'/>
     <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ListBoxItem'>
      <Border x:Name='base' Background='{TemplateBinding Background}'><ContentPresenter/></Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='base' Property='Background' Value='#F3F8F5'/></Trigger>
       <Trigger Property='IsKeyboardFocusWithin' Value='True'><Setter TargetName='base' Property='Background' Value='#EAF5EF'/></Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate></Setter.Value></Setter>
    </Style>");
   var t = new DataTemplate(); t.VisualTree = new FrameworkElementFactory(typeof(TableRowHost)); rowTemplate = t;
  }
 }
 internal sealed class TableValue {
  public TableView Table;
  public object Value;
  public bool Last;
 }
 public sealed class TableRowHost : ContentControl {
  public TableRowHost() {
   IsTabStop = false; Focusable = false;
   HorizontalContentAlignment = HorizontalAlignment.Stretch;
   DataContextChanged += delegate {
    var item = DataContext as TableValue;
    Content = item == null ? null : item.Table.MakeRow(item);
   };
  }
 }
}
