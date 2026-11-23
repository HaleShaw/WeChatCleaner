namespace WeChatCleaner {
 /// Central look and feel. The styles are parsed at start-up from an inline XAML resource
 /// dictionary so that every control (buttons, drop-downs, dialogs, grids) shares one visual
 /// language instead of the stock Windows theme.
 public static class Theme {
  public const string Ink = "#20332E";
  public const string Muted = "#8AA096";
  public const string Muted2 = "#A0AFA6";
  public const string Line = "#E3ECE7";
  public const string Line2 = "#EDF2EE";
  public const string Accent = "#16A267";
  public const string AccentDark = "#118754";
  public const string AccentSoft = "#EAF6EF";
  public const string Canvas = "#F4F8F6";
  public const string Panel = "#FFFFFF";
  public const string Blue = "#6E9BD8";
  public const string Gold = "#E3AE55";
  public const string Danger = "#C2543F";
  public const string DangerSoft = "#FBEDE9";

  public const string Xaml = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>

  <SolidColorBrush x:Key='InkBrush' Color='#20332E'/>
  <SolidColorBrush x:Key='MutedBrush' Color='#8AA096'/>
  <SolidColorBrush x:Key='Muted2Brush' Color='#A0AFA6'/>
  <SolidColorBrush x:Key='LineBrush' Color='#E3ECE7'/>
  <SolidColorBrush x:Key='AccentBrush' Color='#16A267'/>
  <SolidColorBrush x:Key='AccentSoftBrush' Color='#EAF6EF'/>
  <SolidColorBrush x:Key='PanelBrush' Color='#FFFFFF'/>
  <SolidColorBrush x:Key='CanvasBrush' Color='#F4F8F6'/>
  <SolidColorBrush x:Key='BlueBrush' Color='#6E9BD8'/>
  <SolidColorBrush x:Key='GoldBrush' Color='#E3AE55'/>
  <SolidColorBrush x:Key='DangerBrush' Color='#C2543F'/>

  <!-- ============ Button ============ -->
  <Style x:Key='BaseButton' TargetType='{x:Type Button}'>
   <Setter Property='Background' Value='#FFFFFF'/>
   <Setter Property='Foreground' Value='#3C5A4E'/>
   <Setter Property='BorderBrush' Value='#DCE7E0'/>
   <Setter Property='BorderThickness' Value='1'/>
   <Setter Property='Padding' Value='14,7'/>
   <Setter Property='FontSize' Value='12.5'/>
   <Setter Property='FontWeight' Value='SemiBold'/>
   <Setter Property='Cursor' Value='Hand'/>
   <Setter Property='MinHeight' Value='32'/>
   <Setter Property='SnapsToDevicePixels' Value='True'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type Button}'>
      <Border x:Name='shell' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
              BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='8' SnapsToDevicePixels='True'>
       <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
      </Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsMouseOver' Value='True'>
        <Setter TargetName='shell' Property='Background' Value='#F1F8F4'/>
        <Setter TargetName='shell' Property='BorderBrush' Value='#BFDCCF'/>
       </Trigger>
       <Trigger Property='IsPressed' Value='True'><Setter TargetName='shell' Property='Background' Value='#E4F2EA'/></Trigger>
       <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='#14794F'/></Trigger>
       <Trigger Property='IsEnabled' Value='False'><Setter TargetName='shell' Property='Opacity' Value='0.42'/></Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>
  <Style TargetType='{x:Type Button}' BasedOn='{StaticResource BaseButton}'/>

  <Style x:Key='PrimaryButton' TargetType='{x:Type Button}' BasedOn='{StaticResource BaseButton}'>
   <Setter Property='Background' Value='#128052'/>
   <Setter Property='Foreground' Value='White'/>
   <Setter Property='BorderBrush' Value='#128052'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type Button}'>
      <Border x:Name='shell' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}'
              BorderThickness='1' CornerRadius='8' SnapsToDevicePixels='True'>
       <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
      </Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shell' Property='Background' Value='#118754'/><Setter TargetName='shell' Property='BorderBrush' Value='#118754'/></Trigger>
       <Trigger Property='IsPressed' Value='True'><Setter TargetName='shell' Property='Background' Value='#0E6F47'/></Trigger>
       <Trigger Property='IsEnabled' Value='False'><Setter TargetName='shell' Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <Style x:Key='GhostButton' TargetType='{x:Type Button}' BasedOn='{StaticResource BaseButton}'>
   <Setter Property='Background' Value='Transparent'/>
   <Setter Property='BorderBrush' Value='Transparent'/>
   <Setter Property='Foreground' Value='#14915E'/>
   <Setter Property='Padding' Value='8,4'/>
   <Setter Property='MinHeight' Value='26'/>
  </Style>

  <Style x:Key='QuietButton' TargetType='{x:Type Button}' BasedOn='{StaticResource BaseButton}'>
   <Setter Property='Background' Value='#F6F9F7'/>
   <Setter Property='Foreground' Value='#6C8078'/>
   <Setter Property='BorderBrush' Value='#E3ECE7'/>
   <Setter Property='Padding' Value='13,6'/>
   <Setter Property='MinHeight' Value='30'/>
   <Setter Property='FontWeight' Value='Normal'/>
  </Style>

  <Style x:Key='LinkButton' TargetType='{x:Type Button}' BasedOn='{StaticResource GhostButton}'>
   <Setter Property='Foreground' Value='#14915E'/>
   <Setter Property='Padding' Value='4,2'/>
   <Setter Property='MinHeight' Value='24'/>
  </Style>

  <Style x:Key='DangerButton' TargetType='{x:Type Button}' BasedOn='{StaticResource BaseButton}'>
   <Setter Property='Background' Value='#FBEDE9'/>
   <Setter Property='Foreground' Value='#B8503C'/>
   <Setter Property='BorderBrush' Value='#F0D6CF'/>
  </Style>

  <Style x:Key='NavButton' TargetType='{x:Type Button}' BasedOn='{StaticResource BaseButton}'>
   <Setter Property='Background' Value='Transparent'/>
   <Setter Property='BorderBrush' Value='Transparent'/>
   <Setter Property='Foreground' Value='#526A60'/>
   <Setter Property='FontWeight' Value='Normal'/>
   <Setter Property='HorizontalContentAlignment' Value='Left'/>
   <Setter Property='Padding' Value='12,9'/>
   <Setter Property='MinHeight' Value='0'/>
   <Setter Property='Margin' Value='0,1'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type Button}'>
      <Border x:Name='shell' Background='{TemplateBinding Background}' CornerRadius='9' SnapsToDevicePixels='True'>
       <ContentPresenter HorizontalAlignment='Left' VerticalAlignment='Center' Margin='{TemplateBinding Padding}'/>
      </Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shell' Property='Background' Value='#E7F4ED'/></Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <Style x:Key='ProfileModeRadio' TargetType='{x:Type RadioButton}'>
   <Setter Property='FontSize' Value='12'/>
   <Setter Property='Foreground' Value='#294338'/>
   <Setter Property='Padding' Value='7,5,8,5'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type RadioButton}'>
      <Border x:Name='shell' Padding='{TemplateBinding Padding}' CornerRadius='7' Background='Transparent' BorderBrush='Transparent' BorderThickness='1'>
       <StackPanel Orientation='Horizontal'>
        <Grid Width='13' Height='13' VerticalAlignment='Center' Margin='0,0,6,0'>
         <Ellipse x:Name='ring' Fill='White' Stroke='#80998A' StrokeThickness='1.2'/>
         <Ellipse x:Name='dot' Fill='#128253' Width='7' Height='7' Visibility='Collapsed'/>
        </Grid>
        <ContentPresenter VerticalAlignment='Center' RecognizesAccessKey='True'/>
       </StackPanel>
      </Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shell' Property='Background' Value='#F1F7F3'/></Trigger>
       <Trigger Property='IsChecked' Value='True'>
        <Setter TargetName='shell' Property='Background' Value='#E6F4EB'/><Setter TargetName='ring' Property='Stroke' Value='#128253'/>
        <Setter TargetName='dot' Property='Visibility' Value='Visible'/>
       </Trigger>
       <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='#308A61'/></Trigger>
       <Trigger Property='IsEnabled' Value='False'><Setter TargetName='shell' Property='Opacity' Value='0.52'/></Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>
  <!-- ============ ComboBox ============ -->
  <Style TargetType='{x:Type ComboBoxItem}'>
   <Setter Property='Padding' Value='10,7'/>
   <Setter Property='Foreground' Value='#3C5A4E'/>
   <Setter Property='FontSize' Value='12.5'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type ComboBoxItem}'>
      <Border x:Name='shell' Background='Transparent' CornerRadius='6' Margin='2,1' Padding='{TemplateBinding Padding}'>
       <ContentPresenter/>
      </Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shell' Property='Background' Value='#EDF7F2'/></Trigger>
       <Trigger Property='IsSelected' Value='True'>
        <Setter TargetName='shell' Property='Background' Value='#E2F3E9'/>
        <Setter Property='Foreground' Value='#11784E'/>
        <Setter Property='FontWeight' Value='SemiBold'/>
       </Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <Style x:Key='BaseComboBox' TargetType='{x:Type ComboBox}'>
   <Setter Property='Background' Value='White'/>
   <Setter Property='Foreground' Value='#3C5A4E'/>
   <Setter Property='BorderBrush' Value='#DCE7E0'/>
   <Setter Property='BorderThickness' Value='1'/>
   <Setter Property='FontSize' Value='12.5'/>
   <Setter Property='MinHeight' Value='32'/>
   <Setter Property='Padding' Value='10,5'/>
   <Setter Property='Cursor' Value='Hand'/>
   <Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Disabled'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type ComboBox}'>
      <Grid>
       <ToggleButton x:Name='Toggle' Focusable='false' ClickMode='Press'
                     IsChecked='{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}'>
        <ToggleButton.Template>
         <ControlTemplate TargetType='{x:Type ToggleButton}'>
          <Border x:Name='shell' Background='White' BorderBrush='#DCE7E0' BorderThickness='1' CornerRadius='8' SnapsToDevicePixels='True'>
           <Grid>
            <Grid.ColumnDefinitions><ColumnDefinition Width='*'/><ColumnDefinition Width='26'/></Grid.ColumnDefinitions>
            <ContentPresenter x:Name='sel' Grid.Column='0' Margin='11,5,2,5'
                              Content='{Binding SelectionBoxItem, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}'
                              ContentTemplate='{Binding SelectionBoxItemTemplate, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}'
                              VerticalAlignment='Center' HorizontalAlignment='Left'/>
            <Path Grid.Column='1' Data='M 0 0 L 4.5 5 L 9 0' Stroke='#7C9488' StrokeThickness='1.6'
                  StrokeStartLineCap='Round' StrokeEndLineCap='Round' HorizontalAlignment='Center' VerticalAlignment='Center'/>
           </Grid>
          </Border>
          <ControlTemplate.Triggers>
           <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='#9CCFB6'/></Trigger>
           <Trigger Property='IsChecked' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='#16A267'/></Trigger>
           <Trigger Property='IsEnabled' Value='False'><Setter TargetName='shell' Property='Opacity' Value='0.5'/></Trigger>
           <DataTrigger Binding='{Binding IsEditable, RelativeSource={RelativeSource AncestorType={x:Type ComboBox}}}' Value='True'>
            <Setter TargetName='sel' Property='Visibility' Value='Collapsed'/>
           </DataTrigger>
          </ControlTemplate.Triggers>
         </ControlTemplate>
        </ToggleButton.Template>
       </ToggleButton>
       <TextBox x:Name='PART_EditableTextBox' Visibility='Collapsed' Margin='11,0,30,0'
                Background='Transparent' BorderThickness='0' Padding='0' FontSize='12.5'
                Foreground='#2F4A3E' CaretBrush='#16A267' VerticalAlignment='Stretch'
                Text='{Binding Text, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
        <TextBox.Template>
         <ControlTemplate TargetType='{x:Type TextBox}'>
          <ScrollViewer x:Name='PART_ContentHost' VerticalAlignment='Center' Focusable='False'
                        HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/>
         </ControlTemplate>
        </TextBox.Template>
       </TextBox>
       <Popup x:Name='PART_Popup' AllowsTransparency='true' Focusable='false' IsOpen='{TemplateBinding IsDropDownOpen}'
              Placement='Bottom' VerticalOffset='5' PopupAnimation='Fade'>
        <Grid>
         <Border Background='White' BorderBrush='#E3ECE7' BorderThickness='1' CornerRadius='10'
                 MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='340' Padding='3'>
          <ScrollViewer VerticalScrollBarVisibility='Auto'>
           <ItemsPresenter/>
          </ScrollViewer>
         </Border>
        </Grid>
       </Popup>
      </Grid>
     <ControlTemplate.Triggers>
      <Trigger Property='IsEditable' Value='True'>
       <Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/>
      </Trigger>
     </ControlTemplate.Triggers>
    </ControlTemplate>
   </Setter.Value>
  </Setter>
 </Style>
 <Style TargetType='{x:Type ComboBox}' BasedOn='{StaticResource BaseComboBox}'/>

  <!-- ============ CheckBox ============ -->
  <Style TargetType='{x:Type CheckBox}'>
   <Setter Property='Foreground' Value='#3C5A4E'/>
   <Setter Property='FontSize' Value='12.5'/>
   <Setter Property='Cursor' Value='Hand'/>
   <Setter Property='VerticalContentAlignment' Value='Center'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type CheckBox}'>
      <StackPanel Orientation='Horizontal' Background='Transparent'>
       <Border x:Name='box' Width='16' Height='16' CornerRadius='4' Background='White' BorderBrush='#C9DBD2' BorderThickness='1'
               VerticalAlignment='Center'>
        <Grid>
         <Path x:Name='tick' Data='M 2.5 6 L 5.5 9 L 11 2.5' Stroke='White' StrokeThickness='2'
               StrokeStartLineCap='Round' StrokeEndLineCap='Round' Visibility='Collapsed' HorizontalAlignment='Center' VerticalAlignment='Center'/>
         <Border x:Name='mixed' Width='8' Height='2' Background='White' CornerRadius='1' Visibility='Collapsed'/>
        </Grid>
       </Border>
       <ContentPresenter x:Name='label' Margin='8,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/>
      </StackPanel>
      <ControlTemplate.Triggers>
       <Trigger Property='IsChecked' Value='True'>
        <Setter TargetName='box' Property='Background' Value='#16A267'/>
        <Setter TargetName='box' Property='BorderBrush' Value='#16A267'/>
        <Setter TargetName='tick' Property='Visibility' Value='Visible'/>
       </Trigger>
       <Trigger Property='IsChecked' Value='{x:Null}'>
        <Setter TargetName='box' Property='Background' Value='#148052'/>
        <Setter TargetName='box' Property='BorderBrush' Value='#148052'/>
        <Setter TargetName='mixed' Property='Visibility' Value='Visible'/>
       </Trigger>
       <Trigger Property='HasContent' Value='False'><Setter TargetName='label' Property='Visibility' Value='Collapsed'/></Trigger>
       <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='#14794F'/></Trigger>
       <Trigger Property='IsMouseOver' Value='True'>
        <Setter TargetName='box' Property='BorderBrush' Value='#16A267'/>
       </Trigger>
       <Trigger Property='IsEnabled' Value='False'>
        <Setter Property='Opacity' Value='0.45'/>
       </Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <!-- ============ TextBox ============ -->
  <Style TargetType='{x:Type TextBox}'>
   <Setter Property='Background' Value='White'/>
   <Setter Property='Foreground' Value='#2F4A3E'/>
   <Setter Property='BorderBrush' Value='#DCE7E0'/>
   <Setter Property='BorderThickness' Value='1'/>
   <Setter Property='FontSize' Value='12.5'/>
   <Setter Property='Padding' Value='10,2'/>
   <Setter Property='MinHeight' Value='32'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type TextBox}'>
      <Border x:Name='shell' Background='White' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8'>
       <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/>
      </Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='#B9D8C8'/></Trigger>
       <Trigger Property='IsKeyboardFocusWithin' Value='True'>
        <Setter TargetName='shell' Property='BorderBrush' Value='#16A267'/>
        <Setter TargetName='shell' Property='Background' Value='#FBFEFC'/>
       </Trigger>
       <Trigger Property='IsEnabled' Value='False'><Setter TargetName='shell' Property='Opacity' Value='0.55'/></Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <Style TargetType='{x:Type PasswordBox}'>
   <Setter Property='Background' Value='White'/>
   <Setter Property='Foreground' Value='#2F4A3E'/>
   <Setter Property='BorderBrush' Value='#DCE7E0'/>
   <Setter Property='BorderThickness' Value='1'/>
   <Setter Property='FontSize' Value='12.5'/>
   <Setter Property='Padding' Value='10,2'/>
   <Setter Property='MinHeight' Value='32'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type PasswordBox}'>
      <Border x:Name='shell' Background='White' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' CornerRadius='8'>
       <ScrollViewer x:Name='PART_ContentHost' Margin='{TemplateBinding Padding}' VerticalAlignment='Center' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Hidden'/>
      </Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shell' Property='BorderBrush' Value='#B9D8C8'/></Trigger>
       <Trigger Property='IsKeyboardFocusWithin' Value='True'>
        <Setter TargetName='shell' Property='BorderBrush' Value='#16A267'/>
        <Setter TargetName='shell' Property='Background' Value='#FBFEFC'/>
       </Trigger>
       <Trigger Property='IsEnabled' Value='False'><Setter TargetName='shell' Property='Opacity' Value='0.55'/></Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <Style TargetType='{x:Type ScrollBar}'>
   <Setter Property='Background' Value='#F1F6F3'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type ScrollBar}'>
      <Grid Background='{TemplateBinding Background}'>
       <Track x:Name='PART_Track' Orientation='{TemplateBinding Orientation}' IsDirectionReversed='True' Focusable='False'>
        <Track.DecreaseRepeatButton><RepeatButton x:Name='decrease' Command='ScrollBar.PageUpCommand' Opacity='0' Focusable='False'/></Track.DecreaseRepeatButton>
        <Track.Thumb>
         <Thumb>
          <Thumb.Template>
           <ControlTemplate TargetType='{x:Type Thumb}'>
            <Border x:Name='bar' Background='#A9C2B6' CornerRadius='6' Margin='3'/>
            <ControlTemplate.Triggers>
             <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bar' Property='Background' Value='#87A899'/></Trigger>
             <Trigger Property='IsDragging' Value='True'><Setter TargetName='bar' Property='Background' Value='#6F9384'/></Trigger>
            </ControlTemplate.Triggers>
           </ControlTemplate>
          </Thumb.Template>
         </Thumb>
        </Track.Thumb>
        <Track.IncreaseRepeatButton><RepeatButton x:Name='increase' Command='ScrollBar.PageDownCommand' Opacity='0' Focusable='False'/></Track.IncreaseRepeatButton>
       </Track>
      </Grid>
      <ControlTemplate.Triggers>
       <Trigger Property='Orientation' Value='Horizontal'>
        <Setter TargetName='PART_Track' Property='IsDirectionReversed' Value='False'/>
        <Setter TargetName='decrease' Property='Command' Value='ScrollBar.PageLeftCommand'/>
        <Setter TargetName='increase' Property='Command' Value='ScrollBar.PageRightCommand'/>
       </Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
   <Style.Triggers>
    <Trigger Property='Orientation' Value='Vertical'>
     <Setter Property='Width' Value='18'/>
     <Setter Property='MinWidth' Value='18'/>
    </Trigger>
    <Trigger Property='Orientation' Value='Horizontal'>
     <Setter Property='Height' Value='18'/>
     <Setter Property='MinHeight' Value='18'/>
    </Trigger>
   </Style.Triggers>
  </Style>

  <Style TargetType='{x:Type ProgressBar}'>
   <Setter Property='Height' Value='6'/>
   <Setter Property='Foreground' Value='#16A267'/>
   <Setter Property='Background' Value='#E7F0EA'/>
   <Setter Property='BorderThickness' Value='0'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type ProgressBar}'>
      <Border Background='{TemplateBinding Background}' CornerRadius='3'>
       <Grid>
        <Border x:Name='PART_Track' Background='{TemplateBinding Background}' CornerRadius='3'/>
        <Border x:Name='PART_Indicator' Background='{TemplateBinding Foreground}' CornerRadius='3' HorizontalAlignment='Left'/>
        <Border x:Name='busy' Visibility='Collapsed' CornerRadius='3'>
         <Border.Background>
          <LinearGradientBrush StartPoint='0,0' EndPoint='1,0' SpreadMethod='Repeat'>
           <LinearGradientBrush.RelativeTransform><TranslateTransform X='-1'/></LinearGradientBrush.RelativeTransform>
           <GradientStop Color='#E7F0EA' Offset='0'/><GradientStop Color='#E7F0EA' Offset='0.10'/>
           <GradientStop Color='#168655' Offset='0.31'/><GradientStop Color='#E7F0EA' Offset='0.53'/><GradientStop Color='#E7F0EA' Offset='1'/>
          </LinearGradientBrush>
         </Border.Background>
        </Border>
       </Grid>
      </Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsIndeterminate' Value='True'>
        <Setter TargetName='PART_Indicator' Property='Opacity' Value='0'/><Setter TargetName='busy' Property='Visibility' Value='Visible'/>
        <Trigger.EnterActions><BeginStoryboard Name='busyMotion'><Storyboard RepeatBehavior='Forever'>
         <DoubleAnimation Storyboard.TargetName='busy' Storyboard.TargetProperty='(Border.Background).(Brush.RelativeTransform).(TranslateTransform.X)' From='-1' To='0' Duration='0:0:1.35'/>
        </Storyboard></BeginStoryboard></Trigger.EnterActions>
        <Trigger.ExitActions><StopStoryboard BeginStoryboardName='busyMotion'/></Trigger.ExitActions>
       </Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <!-- ============ Context menu ============ -->
  <Style TargetType='{x:Type ContextMenu}'>
   <Setter Property='Background' Value='White'/>
   <Setter Property='BorderBrush' Value='#E3ECE7'/>
   <Setter Property='Padding' Value='4'/>
   <Setter Property='MinWidth' Value='0'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type ContextMenu}'>
      <Grid>
       <Border Background='White' CornerRadius='11' BorderBrush='#E3ECE7' BorderThickness='1' Padding='4'>
        <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
       </Border>
      </Grid>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <Style TargetType='{x:Type MenuItem}'>
   <Setter Property='Foreground' Value='#3C5A4E'/>
   <Setter Property='FontSize' Value='12.5'/>
   <Setter Property='Padding' Value='13,9'/>
   <Setter Property='Cursor' Value='Hand'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type MenuItem}'>
      <Border x:Name='shell' Background='Transparent' CornerRadius='7' Padding='{TemplateBinding Padding}'>
       <Grid>
        <Grid.ColumnDefinitions>
         <ColumnDefinition Width='Auto'/>
         <ColumnDefinition Width='*'/>
         <ColumnDefinition Width='Auto'/>
        </Grid.ColumnDefinitions>
        <ContentPresenter ContentSource='Icon' Grid.Column='0' VerticalAlignment='Center' Margin='0,0,9,0'/>
        <ContentPresenter ContentSource='Header' Grid.Column='1' VerticalAlignment='Center' RecognizesAccessKey='True'/>
       </Grid>
      </Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsHighlighted' Value='True'>
        <Setter TargetName='shell' Property='Background' Value='#EAF6EF'/>
        <Setter Property='Foreground' Value='#11784E'/>
       </Trigger>
       <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <!-- ============ Caption close button ============ -->
  <Style x:Key='CaptionCloseButton' TargetType='{x:Type Button}'>
   <Setter Property='Background' Value='Transparent'/>
   <Setter Property='Foreground' Value='#5C6E65'/>
   <Setter Property='FontSize' Value='14'/>
   <Setter Property='Width' Value='44'/>
   <Setter Property='Height' Value='32'/>
   <Setter Property='Cursor' Value='Hand'/>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type Button}'>
      <Border x:Name='shell' Background='{TemplateBinding Background}' CornerRadius='7'>
       <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
       <Trigger Property='IsMouseOver' Value='True'>
        <Setter TargetName='shell' Property='Background' Value='#E81123'/>
        <Setter Property='Foreground' Value='White'/>
       </Trigger>
      </ControlTemplate.Triggers>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <Style TargetType='{x:Type ToolTip}'>
   <Setter Property='Template'>
    <Setter.Value>
     <ControlTemplate TargetType='{x:Type ToolTip}'>
      <Border Background='#263D32' CornerRadius='6' Padding='10,7'>
       <ContentPresenter TextBlock.Foreground='White' TextBlock.FontSize='12'/>
      </Border>
     </ControlTemplate>
    </Setter.Value>
   </Setter>
  </Style>

  <Style TargetType='{x:Type Separator}'>
   <Setter Property='Background' Value='#EDF2EE'/>
   <Setter Property='Height' Value='1'/>
   <Setter Property='Template'>
    <Setter.Value><ControlTemplate TargetType='{x:Type Separator}'><Border Background='{TemplateBinding Background}'/></ControlTemplate></Setter.Value>
   </Setter>
  </Style>
 </ResourceDictionary>";
 }
}
