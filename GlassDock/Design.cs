using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
namespace GlassDock;
public partial class MainWindow {
 void StyleWindow(Window window) {
  window.Resources["Ink"]=Ink;window.Resources["Card"]=new SolidColorBrush(Dark?Color.FromRgb(42,47,59):Colors.White);window.Resources["Hover"]=new SolidColorBrush(Dark?Color.FromRgb(56,64,80):Color.FromRgb(227,237,250));
  var styles=(ResourceDictionary)XamlReader.Parse("""
  <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="Button"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="BorderThickness" Value="0"/><Setter Property="Cursor" Value="Hand"/><Setter Property="Padding" Value="14,10"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border Background="{TemplateBinding Background}" CornerRadius="9" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter Property="Background" Value="{DynamicResource Hover}"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType="CheckBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="FontSize" Value="14"/></Style>
  <Style TargetType="ComboBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="Padding" Value="9"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid><ToggleButton Background="{TemplateBinding Background}" BorderThickness="0" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{TemplateBinding Background}" CornerRadius="8"><Grid Margin="10"><TextBlock Text="⌄" HorizontalAlignment="Right" Foreground="{DynamicResource Ink}"/></Grid></Border></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter Margin="10,6,28,6" Content="{TemplateBinding SelectionBoxItem}" ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}" IsHitTestVisible="False"/><Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True"><Border Background="{DynamicResource Card}" Padding="6" CornerRadius="8" MinWidth="80"><ScrollViewer><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Padding" Value="10,6"/></Style>
  </ResourceDictionary>
  """);window.Resources.MergedDictionaries.Clear();window.Resources.MergedDictionaries.Add(styles);
 }
 Border Card(UIElement content)=>new() {Child=content,Background=new SolidColorBrush(Dark?Color.FromRgb(38,43,55):Colors.White),CornerRadius=new CornerRadius(13),Padding=new Thickness(16),Margin=new Thickness(0,0,0,12)};
 static string? localizedSettingsName;
 static string WindowsSettingsName() {
  if(localizedSettingsName is not null)return localizedSettingsName;
  object? shell=null,folder=null,item=null;
  try {
   shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
   folder=((dynamic)shell!).NameSpace("shell:AppsFolder");
   item=((dynamic)folder!).ParseName("windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel");
   var name=(string?)((dynamic)item!).Name;
   if(!string.IsNullOrWhiteSpace(name))return localizedSettingsName=name;
  }catch { }
  finally {foreach(var value in new[]{item,folder,shell})if(value is not null&&System.Runtime.InteropServices.Marshal.IsComObject(value))System.Runtime.InteropServices.Marshal.ReleaseComObject(value);}
  return localizedSettingsName=System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName=="fr"?"Paramètres":"Settings";
 }
 public record SettingsChoice(string Value,string Glyph);
 TextBlock SettingsGlyph(string glyph,string description)=>new() {Text=glyph,FontFamily=new FontFamily("Segoe Fluent Icons"),FontSize=22,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};
 void OpenSettings(object sender,RoutedEventArgs e) {
  if(settingsWindow is not null){settingsWindow.Activate();return;}
  var window=new Window {Title=WindowsSettingsName()+" - GlassDock",Width=Math.Min(400,SystemParameters.WorkArea.Width-24),SizeToContent=SizeToContent.Height,MaxHeight=SystemParameters.WorkArea.Height-24,MinWidth=300,MinHeight=0,WindowStartupLocation=WindowStartupLocation.CenterScreen};settingsWindow=window;ApplyWindowTheme(window);
  window.AddHandler(ToolTipService.ToolTipOpeningEvent,new ToolTipEventHandler((_,tip)=>tip.Handled=true),true);
  var content=new StackPanel {Margin=new Thickness(12,8,12,8)};window.Content=new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch};
  Grid Row(string glyph,string description,FrameworkElement control) {
   var row=new Grid {Margin=new Thickness(0,3,0,3)};
   row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(30)});row.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});
   FrameworkElement icon=SettingsGlyph(glyph,description);
   if(description=="Opacité") {
    var drawing=new Grid {Width=22,Height=22};
    drawing.Children.Add(new System.Windows.Shapes.Ellipse {Stroke=Ink,StrokeThickness=1.5});
    drawing.Children.Add(new System.Windows.Shapes.Path {Data=Geometry.Parse("M11,1 A10,10 0 0 0 11,21 Z"),Fill=Ink});icon=drawing;
   }else if(description=="Largeur")icon=new System.Windows.Shapes.Path {Data=Geometry.Parse("M1,11 L23,11 M6,6 L1,11 L6,16 M18,6 L23,11 L18,16"),Stroke=Ink,StrokeThickness=1.6,Width=24,Height=22};
   else if(description=="Taille globale")icon=new System.Windows.Shapes.Path {Data=Geometry.Parse("M3,9 L3,3 L9,3 M3,3 L10,10 M15,21 L21,21 L21,15 M21,21 L14,14"),Stroke=Ink,StrokeThickness=1.6,Width=24,Height=24};
   else if(description=="Bouton Windows") {
    var logo=new Grid {Width=20,Height=20};
    logo.RowDefinitions.Add(new RowDefinition());logo.RowDefinitions.Add(new RowDefinition());logo.ColumnDefinitions.Add(new ColumnDefinition());logo.ColumnDefinitions.Add(new ColumnDefinition());
    for(int i=0;i<4;i++){var square=new System.Windows.Shapes.Rectangle {Fill=Ink,Margin=new Thickness(1)};Grid.SetRow(square,i/2);Grid.SetColumn(square,i%2);logo.Children.Add(square);}icon=logo;
   }else if(description=="Démarrer automatiquement avec Windows") {
    icon=new System.Windows.Shapes.Path {Data=Geometry.Parse("M3,9 L3,23 L25,23 L25,9 Z M14,17 L14,1 M8,7 L14,1 L20,7"),Stroke=Ink,StrokeThickness=1.7,Width=28,Height=24};
   }
   icon.VerticalAlignment=VerticalAlignment.Center;icon.HorizontalAlignment=HorizontalAlignment.Center;
   row.Children.Add(icon);Grid.SetColumn(control,1);control.Margin=new Thickness(12,0,0,0);row.Children.Add(control);return row;
  }
  FrameworkElement SliderValue(Slider slider,TextBlock value) {
   var panel=new Grid();panel.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(1,GridUnitType.Star)});panel.ColumnDefinitions.Add(new ColumnDefinition {Width=new GridLength(54)});
   slider.VerticalAlignment=VerticalAlignment.Center;panel.Children.Add(slider);value.HorizontalAlignment=HorizontalAlignment.Right;value.VerticalAlignment=VerticalAlignment.Center;value.FontSize=14;Grid.SetColumn(value,1);panel.Children.Add(value);return panel;
  }
  var appearance=new StackPanel();
  var opacityLabel=new TextBlock {Text=$"{preferences.Opacity:0} %"};
  var opacity=new Slider {Minimum=20,Maximum=100,Value=preferences.Opacity};opacity.ValueChanged+=(_,_)=>{preferences.Opacity=opacity.Value;opacityLabel.Text=$"{opacity.Value:0} %";ApplyOpacity();SavePreferences();};
  appearance.Children.Add(Row("\uE790","Opacité",SliderValue(opacity,opacityLabel)));
  appearance.Children.Add(new Border {Height=1,Background=Ink,Opacity=.08,Margin=new Thickness(0,10,0,10)});
  var widthLabel=new TextBlock {Text=$"{preferences.DockWidth:0} %"};
  var width=new Slider {Minimum=50,Maximum=100,Value=Math.Clamp(preferences.DockWidth,50,100)};width.ValueChanged+=(_,_)=>{preferences.DockWidth=width.Value;widthLabel.Text=$"{width.Value:0} %";PositionDock();SavePreferences();};
  appearance.Children.Add(Row("\uE740","Largeur",SliderValue(width,widthLabel)));
  appearance.Children.Add(new Border {Height=1,Background=Ink,Opacity=.08,Margin=new Thickness(0,10,0,10)});
  var scaleLabel=new TextBlock {Text=$"{NormalizeDockScale(preferences.DockScale):0} %"};
  var scaleSlider=new Slider {Minimum=25,Maximum=200,TickFrequency=25,IsSnapToTickEnabled=true,SmallChange=25,LargeChange=25,Value=NormalizeDockScale(preferences.DockScale)};
  scaleSlider.ValueChanged+=(_,_)=> {
   preferences.DockScale=NormalizeDockScale(scaleSlider.Value);scaleLabel.Text=$"{preferences.DockScale:0} %";
   PositionDock();SavePreferences();
  };
  appearance.Children.Add(Row("\uE740","Taille globale",SliderValue(scaleSlider,scaleLabel)));content.Children.Add(Card(appearance));
  ComboBox Choices((string Value,string Glyph)[] values,string selected,string description) {
   var template=(DataTemplate)XamlReader.Parse("""
    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"><TextBlock Text="{Binding Glyph}" FontFamily="Segoe Fluent Icons" FontSize="22" HorizontalAlignment="Center" VerticalAlignment="Center"/></DataTemplate>
    """);
   var combo=new ComboBox {MinHeight=34,ItemsSource=values.Select(x=>new SettingsChoice(x.Value,x.Glyph)).ToArray(),ItemTemplate=template,SelectedValuePath="Value",SelectedValue=selected};
   return combo;
  }
  var placement=new StackPanel();
  var start=Choices(new[]{("À gauche du dock","\uE8E4"),("Avec les applications","\uE8E3"),("Masqué","\uED1A")},preferences.StartMode,"Bouton Windows");
  start.SelectionChanged+=(_,_)=>{if(start.SelectedValue is string mode){preferences.StartMode=mode;PositionDock();SavePreferences();}};placement.Children.Add(Row("\uE782","Bouton Windows",start));
  var align=Choices(new[]{("Gauche","\uE8E4"),("Centre","\uE8E3"),("Droite","\uE8E2"),("Personnalisée","\uE713")},preferences.Alignment,"Alignement des applications");
  var alignmentRow=Row("\uE8A9","Alignement des applications",align);alignmentRow.Margin=new Thickness(0,10,0,3);placement.Children.Add(alignmentRow);
  var position=new Slider {Minimum=0,Maximum=100,Value=preferences.Position,Visibility=preferences.Alignment=="Personnalisée"?Visibility.Visible:Visibility.Collapsed,Margin=new Thickness(50,12,0,0)};placement.Children.Add(position);
  align.SelectionChanged+=(_,_)=>{if(align.SelectedValue is string alignment){preferences.Alignment=alignment;position.Visibility=alignment=="Personnalisée"?Visibility.Visible:Visibility.Collapsed;ApplyIconAlignment();SavePreferences();}};
  position.ValueChanged+=(_,_)=>{preferences.Position=position.Value;ApplyIconAlignment();SavePreferences();};content.Children.Add(Card(placement));
  window.Closed+=(_,_)=>settingsWindow=null;ApplyGlassFrame(window);
  if(glassFrames.TryGetValue(window,out var frame)) {
   frame.Header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   frame.Header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});Grid.SetColumn(frame.Close,3);frame.Close.ToolTip=null;
   var startupIcon=new Grid {Width=22,Height=22};
   var monitor=new System.Windows.Shapes.Path {Data=Geometry.Parse("M3,2 L19,2 Q21,2 21,4 L21,14 Q21,16 19,16 L3,16 Q1,16 1,14 L1,4 Q1,2 3,2 M11,16 L11,20 M7,20 L15,20"),StrokeThickness=1.4,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
   var power=new System.Windows.Shapes.Path {Data=Geometry.Parse("M11,5 L11,9 M8.5,7 A4,4 0 1 0 13.5,7"),StrokeThickness=1.4,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
   startupIcon.Children.Add(monitor);startupIcon.Children.Add(power);
   var startup=new Button {Content=startupIcon,Width=30,Height=30,Padding=new Thickness(0),Margin=new Thickness(0,0,6,0),Background=Brushes.Transparent};
   startup.Template=(ControlTemplate)XamlReader.Parse("""
    <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button"><Border x:Name="Face" CornerRadius="8" Background="{TemplateBinding Background}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Face" Property="Opacity" Value="0.8"/></Trigger><Trigger Property="IsPressed" Value="True"><Setter TargetName="Face" Property="Opacity" Value="0.55"/></Trigger></ControlTemplate.Triggers></ControlTemplate>
    """);
   void RefreshStartup() {
    var enabled=AutoStartEnabled();var color=enabled?new SolidColorBrush(Dark?Color.FromRgb(140,192,255):Color.FromRgb(24,105,210)):Ink;
    monitor.Stroke=color;power.Stroke=color;
    startup.Background=enabled?new SolidColorBrush(Dark?Color.FromArgb(45,82,155,255):Color.FromArgb(28,30,110,225)):Brushes.Transparent;
    startupIcon.Opacity=enabled?1:.6;
   }
   startup.Click+=(_,_)=>{try{SetAutoStart(!AutoStartEnabled());}catch(Exception error){MessageBox.Show(window,error.Message,WindowsSettingsName());}RefreshStartup();};
   RefreshStartup();Grid.SetColumn(startup,1);frame.Header.Children.Add(startup);
   var quit=new Button {Content=SettingsGlyph("\uE7E8",""),Width=30,Height=30,Padding=new Thickness(0),Margin=new Thickness(0,0,6,0),Background=Brushes.Transparent};
   quit.Click+=(_,_)=>Close();Grid.SetColumn(quit,2);frame.Header.Children.Add(quit);
   quit.Template=startup.Template;frame.Close.Template=startup.Template;
   frame.Close.Width=30;frame.Close.Height=30;frame.Close.Padding=new Thickness(0);frame.Close.Margin=new Thickness(0);
   if(quit.Content is TextBlock quitIcon)quitIcon.FontSize=19;
   frame.Header.Margin=new Thickness(16,12,12,12);
   frame.Title.FontSize=16;frame.Title.FontWeight=FontWeights.Medium;
   foreach(var card in content.Children.OfType<Border>()){card.Padding=new Thickness(12);card.CornerRadius=new CornerRadius(12);card.Margin=new Thickness(0,0,0,10);}
   if(content.Children.OfType<Border>().LastOrDefault() is Border lastCard)lastCard.Margin=new Thickness(0);
  }
  window.Show();
 }
 void OpenOverflow(object sender,RoutedEventArgs e) {
  var menu=new ContextMenu {PlacementTarget=sender as UIElement,Placement=System.Windows.Controls.Primitives.PlacementMode.Top,Background=new SolidColorBrush(Dark?Color.FromRgb(38,43,55):Colors.White),Foreground=Ink};
  menu.Items.Add(new MenuItem {Header="Applications ouvertes",IsEnabled=false});foreach(var app in FindWindows()){var entry=new MenuItem {Header=app.Name,Icon=WindowIcon(app)};entry.Click+=(_,_)=>Activate(app.Handle);menu.Items.Add(entry);}menu.Items.Add(new Separator());var native=new MenuItem {Header="Afficher les icônes masquées Windows"};native.Click+=(_,_)=>{Restore();ReplaceToggle.IsChecked=false;PositionDock();Native.keybd_event(0x5B,0,0,UIntPtr.Zero);Native.keybd_event(0x42,0,0,UIntPtr.Zero);Native.keybd_event(0x42,0,2,UIntPtr.Zero);Native.keybd_event(0x5B,0,2,UIntPtr.Zero);MessageBox.Show(this,"La barre Windows est rétablie pour accéder à toutes ses icônes masquées. Tu peux réactiver GlassDock depuis les réglages.","Zone de notification Windows");};menu.Items.Add(native);menu.IsOpen=true;
 }
 void ApplyIconAlignment() {
  if(IconViewport is null||ItemsPanel is null||SystemButtons is null||StartButton is null)return;
  ItemsPanel.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));SystemButtons.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
  var available=Math.Max(0,DockLayout.ActualWidth>0?DockLayout.ActualWidth:Width-46);
  var hidden=preferences.StartMode=="Masqué";var follows=preferences.StartMode=="Avec les applications";
  var minimum=hidden||follows?0:55.0;var startSpace=follows?52.0:0;
  var rightEdge=Math.Max(0,available-SystemButtons.DesiredSize.Width-12);
  var width=Math.Min(ItemsPanel.DesiredSize.Width,Math.Max(0,rightEdge-minimum-startSpace));IconViewport.Width=width;
  var groupWidth=width+startSpace;var maximum=Math.Max(minimum,rightEdge-groupWidth);
  var left=preferences.Alignment switch {"Gauche"=>minimum,"Droite"=>maximum,"Personnalisée"=>minimum+(maximum-minimum)*Math.Clamp(preferences.Position,0,100)/100,_=>Math.Clamp((available-groupWidth)/2,minimum,maximum)};
  IconViewport.Margin=new Thickness(left+startSpace,0,0,0);
  StartButton.Visibility=hidden?Visibility.Collapsed:Visibility.Visible;
  StartButton.Margin=new Thickness(follows?left+3:4.5,0,0,0);UpdateCarouselLayout();
 }
 void DockLayoutChanged(object sender,SizeChangedEventArgs e)=>ApplyIconAlignment();
 void ScrollDockApps(object sender,MouseWheelEventArgs e) {if(IconViewport.ScrollableWidth>0){IconViewport.ScrollToHorizontalOffset(IconViewport.HorizontalOffset-e.Delta);e.Handled=true;}}
 string CheckResponsiveDockWidth() {
  var original=preferences.DockWidth;
  try {
   preferences.DockWidth=100;PositionDock();UpdateLayout();var full=Width;
   preferences.DockWidth=50;PositionDock();UpdateLayout();
   var screen=ScreenBounds();var dpi=DpiScale();var screenWidth=(screen.Right-screen.Left)/dpi;
   var centered=Math.Abs(Left-(screen.Left/dpi+(screenWidth-Width)/2))<.5;
   var inBounds=Width<=full&&Left>=screen.Left/dpi&&Left+Width<=screen.Right/dpi;
   var systemLeft=SystemButtons.TranslatePoint(new Point(),DockLayout).X;
   var noOverlap=IconViewport.Margin.Left+IconViewport.ActualWidth<=systemLeft+.5;
   var overflow=ItemsPanel.DesiredSize.Width>IconViewport.ActualWidth+1;
   var scrollWorks=!overflow||IconViewport.ScrollableWidth>0;
   return centered&&inBounds&&noOverlap&&scrollWorks
    ?$"PASS: largeur responsive {Width:0}/{full:0} DIP, contrôles visibles, défilement={overflow}"
    :$"FAIL: dock responsive (centre={centered}, écran={inBounds}, chevauchement={!noOverlap}, défilement={scrollWorks})";
  }finally{preferences.DockWidth=original;PositionDock();UpdateLayout();}
 }
 void SnapshotWindow(Window window,string name) {window.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(window);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var stream=System.IO.File.Create(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(savePath)!,name));encoder.Save(stream);}
}



