using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace GlassDock;

public partial class MainWindow {
 readonly Dictionary<Window,GlassFrameState> glassFrames=new();

 sealed class GlassFrameState {
  public required Grid Root;
  public required Grid Face;
  public required Border Shell;
  public required Border Rim;
  public required Border Glint;
  public required Border Separator;
  public required ContentControl Body;
  public required TextBlock Title;
  public required TextBlock Eyebrow;
  public required Grid Header;
  public required Button Close;
  public required ResourceDictionary Styles;
  public double ClipRadius=22;
  public bool Compact;
  public Border? CaptionPill;
 }

 // Call after assigning the window's content, before its first Show/ShowDialog.
 // On folder rebuilds the same chrome is retained and only its body is replaced.
 void ApplyGlassFrame(Window window) {
  if(glassFrames.TryGetValue(window,out var existing)) {
   if(!ReferenceEquals(window.Content,existing.Root)) {
    var content=window.Content;
    window.Content=null;
    existing.Body.Content=PrepareGlassContent(window,content);
    window.Content=existing.Root;
   }
   RefreshGlassFrameTheme(window);
   return;
  }

  var original=window.Content;
  window.Content=null;
  // AllowsTransparency cannot be changed once the native window has been created.
  if(new WindowInteropHelper(window).Handle==IntPtr.Zero) {
   window.WindowStyle=WindowStyle.None;
   window.AllowsTransparency=true;
  }
  window.Background=Brushes.Transparent;
  window.ResizeMode=ResizeMode.NoResize;
  window.ShowInTaskbar=false;
  window.UseLayoutRounding=true;
  window.SnapsToDevicePixels=true;
  window.MaxHeight=Math.Min(window.MaxHeight,SystemParameters.WorkArea.Height-32);
  window.MaxWidth=Math.Min(window.MaxWidth,SystemParameters.WorkArea.Width-32);

  var root=new Grid {Margin=new Thickness(12),Background=Brushes.Transparent};
  var face=new Grid();
  face.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
  face.RowDefinitions.Add(new RowDefinition {Height=new GridLength(1,GridUnitType.Star)});
  var shell=new Border {
   CornerRadius=new CornerRadius(24),BorderThickness=new Thickness(1),Padding=new Thickness(1),Child=face,
   Effect=new DropShadowEffect {Color=Colors.Black,BlurRadius=26,ShadowDepth=9,Opacity=.28}
  };
  root.Children.Add(shell);
  var glint=new Border {CornerRadius=new CornerRadius(24),IsHitTestVisible=false};
  root.Children.Add(glint);
  var rim=new Border {CornerRadius=new CornerRadius(23),BorderThickness=new Thickness(1),Margin=new Thickness(1),IsHitTestVisible=false};
  root.Children.Add(rim);

  var header=new Grid {Margin=new Thickness(22,15,14,14),Background=Brushes.Transparent};
  header.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
  header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
  var eyebrow=new TextBlock {Text="GLASSDOCK",Visibility=Visibility.Collapsed};
  var title=new TextBlock {Text=GlassCaption(window),FontSize=21,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis};
  header.Children.Add(title);
  var close=new Button {Width=32,Height=32,Padding=new Thickness(0),Background=Brushes.Transparent,ToolTip="Fermer",Focusable=false};
  close.Content=new System.Windows.Shapes.Path {Data=Geometry.Parse("M2,2 L10,10 M10,2 L2,10"),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,Width=12,Height=12,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
  ((System.Windows.Shapes.Path)close.Content).SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"Ink");
  close.Click+=(_,_)=>window.Close();Grid.SetColumn(close,1);header.Children.Add(close);face.Children.Add(header);
  header.MouseLeftButtonDown+=(_,e)=> {
   if(glassFrames.TryGetValue(window,out var frame)&&frame.Compact)return;
   if(IsGlassControl(e.OriginalSource as DependencyObject))return;
   if(e.ButtonState==MouseButtonState.Pressed)try {window.DragMove();e.Handled=true;}catch(InvalidOperationException){}
  };
  var separator=new Border {Height=1,VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false};
  face.Children.Add(separator);
  var body=new ContentControl {Content=PrepareGlassContent(window,original),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
  Grid.SetRow(body,1);face.Children.Add(body);
  var styles=CreateGlassControlStyles();
  var state=new GlassFrameState {Root=root,Face=face,Shell=shell,Rim=rim,Glint=glint,Separator=separator,Body=body,Title=title,Eyebrow=eyebrow,Header=header,Close=close,Styles=styles};
  glassFrames.Add(window,state);
  window.Content=root;
  face.SizeChanged+=(_,_)=>face.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,face.ActualWidth),Math.Max(0,face.ActualHeight)),state.ClipRadius,state.ClipRadius);
  window.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){window.Close();e.Handled=true;}};
  window.Closed+=(_,_)=>glassFrames.Remove(window);
  window.Loaded+=(_,_)=> {
   root.RenderTransformOrigin=new Point(.5,.5);
   var scale=new ScaleTransform(1,1);var shift=new TranslateTransform();var transform=new TransformGroup();transform.Children.Add(scale);transform.Children.Add(shift);root.RenderTransform=transform;
   var ease=new CubicEase {EasingMode=EasingMode.EaseOut};var duration=TimeSpan.FromMilliseconds(180);
   scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.985,1,duration){EasingFunction=ease});
   scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.985,1,duration){EasingFunction=ease});
   shift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(6,0,duration){EasingFunction=ease});
  };
  RefreshGlassFrameTheme(window);
 }

 void ReframeGlassWindow(Window window)=>ApplyGlassFrame(window);

 void UseCompactGlassFrame(Window window) {
  ApplyGlassFrame(window);
  if(!glassFrames.TryGetValue(window,out var state))return;
  if(!state.Compact) {
   state.Compact=true;
   state.Face.Children.Remove(state.Header);
   state.Root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
   state.Root.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});
   foreach(UIElement child in state.Root.Children)Grid.SetRow(child,1);
   state.CaptionPill=new Border {Child=state.Header,CornerRadius=new CornerRadius(9),HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,0,0,14),MaxWidth=240};
   Grid.SetRow(state.CaptionPill,0);state.Root.Children.Add(state.CaptionPill);
  }
  state.Root.Margin=new Thickness(6);
  state.Shell.CornerRadius=new CornerRadius(24);state.Rim.CornerRadius=new CornerRadius(23);state.ClipRadius=22;
  state.Header.Margin=new Thickness(12,5,7,5);
  state.Close.Visibility=Visibility.Visible;state.Close.Width=18;state.Close.Height=18;state.Close.Margin=new Thickness(7,0,0,0);
  state.Separator.Visibility=Visibility.Collapsed;
  state.Title.FontSize=14;state.Title.FontWeight=FontWeights.Normal;state.Title.TextAlignment=TextAlignment.Center;state.Title.HorizontalAlignment=HorizontalAlignment.Stretch;
  state.Title.Margin=new Thickness(0);
  RefreshGlassFrameTheme(window);
  state.Face.Clip=new RectangleGeometry(new Rect(0,0,Math.Max(0,state.Face.ActualWidth),Math.Max(0,state.Face.ActualHeight)),22,22);
 }

 // Keep the chrome attached during page changes so its Loaded animation and text
 // layout are not restarted when replacing the folder's grid.
 void SetGlassWindowBody(Window window,object? content) {
  if(glassFrames.TryGetValue(window,out var state)&&ReferenceEquals(window.Content,state.Root))state.Body.Content=PrepareGlassContent(window,content);
  else window.Content=content;
 }

 void RefreshGlassFrameTheme(Window window) {
  if(!glassFrames.TryGetValue(window,out var state))return;
  window.Background=Brushes.Transparent;
  window.Foreground=Ink;
  window.Resources["Ink"]=Ink;
  window.Resources["GlassAccent"]=new SolidColorBrush(Dark?Color.FromRgb(112,177,255):Color.FromRgb(0,103,218));
  window.Resources["Card"]=new SolidColorBrush(Dark?Color.FromRgb(38,45,58):Colors.White);
  window.Resources["Hover"]=new SolidColorBrush(Dark?Color.FromRgb(51,61,77):Color.FromRgb(230,238,250));
  window.Resources["GlassStroke"]=new SolidColorBrush(Dark?Color.FromArgb(24,255,255,255):Color.FromArgb(52,82,106,143));
  window.Resources["GlassPopup"]=new SolidColorBrush(Dark?Color.FromRgb(36,42,54):Color.FromRgb(244,249,255));
  window.Resources["GlassScrollTrack"]=new SolidColorBrush(Dark?Color.FromRgb(28,33,43):Color.FromRgb(244,247,252));
  window.Resources["GlassScrollThumb"]=new SolidColorBrush(Dark?Color.FromRgb(83,94,113):Color.FromRgb(169,182,201));
  window.Resources["GlassScrollHover"]=new SolidColorBrush(Dark?Color.FromRgb(120,134,154):Color.FromRgb(119,139,165));
  if(!window.Resources.MergedDictionaries.Contains(state.Styles))window.Resources.MergedDictionaries.Add(state.Styles);
  state.Shell.Background=new SolidColorBrush(Dark?Color.FromRgb(28,33,43):Color.FromRgb(244,247,252));
  if(state.Compact) {
   state.Shell.Background=new LinearGradientBrush(Dark?Color.FromRgb(63,70,82):Color.FromRgb(220,226,237),Dark?Color.FromRgb(39,45,55):Color.FromRgb(242,245,250),90);
   if(state.CaptionPill is not null)state.CaptionPill.Background=new SolidColorBrush(Dark?Color.FromRgb(43,48,58):Color.FromRgb(232,237,244));
  }
  state.Shell.BorderBrush=new LinearGradientBrush(Dark?Color.FromArgb(120,231,240,255):Color.FromArgb(240,255,255,255),Dark?Color.FromArgb(26,160,191,229):Color.FromArgb(100,130,154,190),90);
  state.Rim.BorderBrush=new LinearGradientBrush(Dark?Color.FromArgb(18,255,255,255):Color.FromArgb(65,255,255,255),Colors.Transparent,90);
  state.Glint.Background=null;state.Glint.Visibility=Visibility.Collapsed;
  state.Separator.Background=new SolidColorBrush(Dark?Color.FromArgb(22,255,255,255):Color.FromArgb(35,90,113,145));
  state.Title.Foreground=Ink;state.Eyebrow.Foreground=Ink;state.Title.Text=GlassCaption(window);
  RefreshGlassCards(state.Body.Content as DependencyObject);
 }

 object? PrepareGlassContent(Window window,object? content) {
  // Existing panels already contain a heading; the caption moves into the glass header.
  var container=content as DependencyObject;
  if(content is ScrollViewer scroll)container=scroll.Content as DependencyObject;
  if(container is Panel panel) {
   var heading=panel.Children.OfType<TextBlock>().FirstOrDefault();
   if(heading is null&&panel.Children.OfType<StackPanel>().FirstOrDefault() is StackPanel header)heading=header.Children.OfType<TextBlock>().FirstOrDefault();
   if(heading is not null&&(heading.Text==GlassCaption(window)||heading.Text==window.Title))heading.Visibility=Visibility.Collapsed;
  }
  return content;
 }

 void RefreshGlassCards(DependencyObject? element) {
  if(element is null)return;
  if(element is Border card&&(card.CornerRadius.TopLeft==13||card.CornerRadius.TopLeft==16)&&card.Padding.Left==16) {
   card.Background=(Brush)card.FindResource("Card");
   card.BorderThickness=new Thickness(1);
   card.BorderBrush=(Brush)card.FindResource("GlassStroke");
   card.CornerRadius=new CornerRadius(16);
  }
  foreach(var child in LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>())RefreshGlassCards(child);
 }

 static string GlassCaption(Window window) {
  var title=window.Title;
  if(title.Contains("Réglages",StringComparison.OrdinalIgnoreCase))return "Réglages";
  return string.IsNullOrWhiteSpace(title)?"GlassDock":title;
 }

 static bool IsGlassControl(DependencyObject? element) {
  while(element is not null) {
   if(element is ButtonBase or TextBoxBase or Selector or Thumb)return true;
   element=element is Visual?VisualTreeHelper.GetParent(element):LogicalTreeHelper.GetParent(element);
  }
  return false;
 }

 static ResourceDictionary CreateGlassControlStyles()=>(ResourceDictionary)XamlReader.Parse("""
 <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="Button">
   <Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="BorderBrush" Value="{DynamicResource GlassStroke}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="12,9"/><Setter Property="FontSize" Value="13"/><Setter Property="Cursor" Value="Hand"/><Setter Property="HorizontalContentAlignment" Value="Center"/><Setter Property="VerticalContentAlignment" Value="Center"/>
   <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Surface" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="11" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="{TemplateBinding VerticalContentAlignment}" RecognizesAccessKey="True"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Surface" Property="Background" Value="{DynamicResource Hover}"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Surface" Property="Opacity" Value=".65"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Surface" Property="BorderBrush" Value="{DynamicResource GlassAccent}"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="CheckBox">
   <Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="FontSize" Value="13"/><Setter Property="Cursor" Value="Hand"/>
   <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="CheckBox"><Grid Background="Transparent"><Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions><Border x:Name="Track" Width="38" Height="22" Background="{DynamicResource Card}" BorderBrush="{DynamicResource GlassStroke}" BorderThickness="1" CornerRadius="11" VerticalAlignment="Center"><Border x:Name="Knob" Width="16" Height="16" HorizontalAlignment="Left" Margin="2" Background="{DynamicResource Ink}" CornerRadius="8"/></Border><ContentPresenter Grid.Column="1" Margin="12,2,0,2" VerticalAlignment="Center" RecognizesAccessKey="True"/></Grid><ControlTemplate.Triggers><Trigger Property="IsChecked" Value="True"><Setter TargetName="Track" Property="Background" Value="{DynamicResource GlassAccent}"/><Setter TargetName="Track" Property="BorderBrush" Value="{DynamicResource GlassAccent}"/><Setter TargetName="Knob" Property="HorizontalAlignment" Value="Right"/><Setter TargetName="Knob" Property="Background" Value="White"/></Trigger><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Track" Property="Opacity" Value=".8"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value=".4"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="ComboBox">
   <Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="BorderBrush" Value="{DynamicResource GlassStroke}"/><Setter Property="FontSize" Value="13"/><Setter Property="Padding" Value="12,9"/><Setter Property="MinHeight" Value="36"/><Setter Property="HorizontalContentAlignment" Value="Left"/>
   <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid><ToggleButton Focusable="False" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border x:Name="Face" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="10"><Path Data="M0,0 L4,4 L8,0" Width="8" Height="4" Stroke="{DynamicResource Ink}" StrokeThickness="1.4" StrokeStartLineCap="Round" StrokeEndLineCap="Round" HorizontalAlignment="Right" VerticalAlignment="Center" Margin="0,0,12,0"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource Hover}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}" Margin="12,8,32,8" VerticalAlignment="Center" IsHitTestVisible="False"/><Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" AllowsTransparency="True" Placement="Bottom" Focusable="False" PopupAnimation="Fade"><Border Background="{DynamicResource GlassPopup}" BorderBrush="{DynamicResource GlassStroke}" BorderThickness="1" CornerRadius="12" Padding="5" MinWidth="{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}"><ScrollViewer MaxHeight="280"><ItemsPresenter KeyboardNavigation.DirectionalNavigation="Contained"/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Padding" Value="10,8"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBoxItem"><Border x:Name="Face" CornerRadius="7" Background="Transparent" Padding="{TemplateBinding Padding}"><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property="IsHighlighted" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource Hover}"/></Trigger><Trigger Property="IsSelected" Value="True"><Setter TargetName="Face" Property="Background" Value="{DynamicResource Card}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style x:Key="GlassSliderBase" TargetType="RepeatButton"><Setter Property="Focusable" Value="False"/><Setter Property="Background" Value="{DynamicResource GlassStroke}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton"><Border Background="{TemplateBinding Background}" Height="4" CornerRadius="2"/></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType="Slider"><Setter Property="Height" Value="26"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider"><Grid Margin="0,4"><Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="{TemplateBinding IsDirectionReversed}"><Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Style="{StaticResource GlassSliderBase}" Background="{DynamicResource GlassAccent}"/></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Style="{StaticResource GlassSliderBase}"/></Track.IncreaseRepeatButton><Track.Thumb><Thumb Width="18" Height="18" Cursor="Hand"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border x:Name="Knob" CornerRadius="9" Background="White" BorderBrush="{DynamicResource GlassAccent}" BorderThickness="4"><Border.Effect><DropShadowEffect BlurRadius="5" ShadowDepth="1" Opacity=".18"/></Border.Effect></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Knob" Property="BorderThickness" Value="3"/></Trigger><Trigger Property="IsDragging" Value="True"><Setter TargetName="Knob" Property="BorderThickness" Value="2"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType="TextBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="BorderBrush" Value="{DynamicResource GlassStroke}"/><Setter Property="BorderThickness" Value="1"/><Setter Property="Padding" Value="12,9"/><Setter Property="CaretBrush" Value="{DynamicResource Ink}"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TextBox"><Border x:Name="Face" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="10" Padding="{TemplateBinding Padding}"><ScrollViewer x:Name="PART_ContentHost"/></Border><ControlTemplate.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="Face" Property="BorderBrush" Value="{DynamicResource GlassAccent}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style x:Key="GlassScrollPage" TargetType="RepeatButton"><Setter Property="Focusable" Value="False"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="RepeatButton"><Border Background="Transparent"/></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType="ScrollBar">
   <Setter Property="Width" Value="8"/><Setter Property="Background" Value="{DynamicResource GlassScrollTrack}"/><Setter Property="Focusable" Value="False"/>
   <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ScrollBar"><Grid Background="{TemplateBinding Background}"><Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" ViewportSize="{TemplateBinding ViewportSize}" Orientation="{TemplateBinding Orientation}" IsDirectionReversed="True"><Track.DecreaseRepeatButton><RepeatButton x:Name="Decrease" Command="ScrollBar.PageUpCommand" Style="{StaticResource GlassScrollPage}"/></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton x:Name="Increase" Command="ScrollBar.PageDownCommand" Style="{StaticResource GlassScrollPage}"/></Track.IncreaseRepeatButton><Track.Thumb><Thumb x:Name="Handle" MinHeight="24" Margin="1"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border x:Name="Grip" Background="{DynamicResource GlassScrollThumb}" CornerRadius="3"/><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Grip" Property="Background" Value="{DynamicResource GlassScrollHover}"/></Trigger><Trigger Property="IsDragging" Value="True"><Setter TargetName="Grip" Property="Background" Value="{DynamicResource GlassScrollHover}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid><ControlTemplate.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/><Setter TargetName="Decrease" Property="Command" Value="ScrollBar.PageLeftCommand"/><Setter TargetName="Increase" Property="Command" Value="ScrollBar.PageRightCommand"/><Setter TargetName="Handle" Property="MinHeight" Value="0"/><Setter TargetName="Handle" Property="MinWidth" Value="24"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
   <Style.Triggers><Trigger Property="Orientation" Value="Horizontal"><Setter Property="Width" Value="Auto"/><Setter Property="Height" Value="8"/></Trigger></Style.Triggers>
  </Style>
 </ResourceDictionary>
 """);
}
