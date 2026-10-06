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
  <Style TargetType="ComboBox"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Background" Value="{DynamicResource Card}"/><Setter Property="Padding" Value="9"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox"><Grid><ToggleButton Background="{TemplateBinding Background}" BorderThickness="0" IsChecked="{Binding IsDropDownOpen,RelativeSource={RelativeSource TemplatedParent},Mode=TwoWay}"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="{TemplateBinding Background}" CornerRadius="8"><Grid Margin="10"><TextBlock Text="⌄" HorizontalAlignment="Right" Foreground="{DynamicResource Ink}"/></Grid></Border></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter Margin="10,6,28,6" Content="{TemplateBinding SelectionBoxItem}" IsHitTestVisible="False"/><Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="True"><Border Background="{DynamicResource Card}" Padding="6" CornerRadius="8" MinWidth="80"><ScrollViewer><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType="ComboBoxItem"><Setter Property="Foreground" Value="{DynamicResource Ink}"/><Setter Property="Padding" Value="10,6"/></Style>
  </ResourceDictionary>
  """);window.Resources.MergedDictionaries.Clear();window.Resources.MergedDictionaries.Add(styles);
 }
 Border Card(UIElement content)=>new() {Child=content,Background=new SolidColorBrush(Dark?Color.FromRgb(38,43,55):Colors.White),CornerRadius=new CornerRadius(13),Padding=new Thickness(16),Margin=new Thickness(0,0,0,12)};
 void OpenSettings(object sender,RoutedEventArgs e) {
  if(settingsWindow is not null){settingsWindow.Activate();return;}
  var window=new Window {Title="Paramètres",Width=Math.Min(440,SystemParameters.WorkArea.Width-24),Height=Math.Min(610,SystemParameters.WorkArea.Height-24),MinWidth=300,MinHeight=360,WindowStartupLocation=WindowStartupLocation.CenterScreen};settingsWindow=window;ApplyWindowTheme(window);
  var content=new StackPanel {Margin=new Thickness(18)};window.Content=new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalContentAlignment=HorizontalAlignment.Stretch};
  var appearance=new StackPanel();var opacityLabel=new TextBlock {Text=$"Opacité · {preferences.Opacity:0} %",Margin=new Thickness(0,0,0,10)};appearance.Children.Add(opacityLabel);
  var opacity=new Slider {Minimum=20,Maximum=100,Value=preferences.Opacity};opacity.ValueChanged+=(_,_)=>{preferences.Opacity=opacity.Value;opacityLabel.Text=$"Opacité · {opacity.Value:0} %";ApplyOpacity();SavePreferences();};appearance.Children.Add(opacity);content.Children.Add(Card(appearance));
  var placement=new StackPanel();var widthLabel=new TextBlock {Text=$"Largeur · {preferences.DockWidth:0} %",Margin=new Thickness(0,0,0,8)};placement.Children.Add(widthLabel);
  var width=new Slider {Minimum=50,Maximum=100,Value=Math.Clamp(preferences.DockWidth,50,100),Margin=new Thickness(0,0,0,16)};width.ValueChanged+=(_,_)=>{preferences.DockWidth=width.Value;widthLabel.Text=$"Largeur · {width.Value:0} %";PositionDock();SavePreferences();};placement.Children.Add(width);
  placement.Children.Add(new TextBlock {Text="Bouton Windows",Margin=new Thickness(0,0,0,8)});
  var start=new ComboBox {ItemsSource=new[]{"À gauche du dock","Avec les applications","Masqué"},SelectedItem=preferences.StartMode,Margin=new Thickness(0,0,0,16)};start.SelectionChanged+=(_,_)=>{if(start.SelectedItem is string mode){preferences.StartMode=mode;PositionDock();SavePreferences();}};placement.Children.Add(start);
  placement.Children.Add(new TextBlock {Text="Alignement des applications",Margin=new Thickness(0,0,0,8)});
  var align=new ComboBox {ItemsSource=new[]{"Gauche","Centre","Droite","Personnalisée"},SelectedItem=preferences.Alignment};placement.Children.Add(align);
  var position=new Slider {Minimum=0,Maximum=100,Value=preferences.Position,Visibility=preferences.Alignment=="Personnalisée"?Visibility.Visible:Visibility.Collapsed,Margin=new Thickness(0,12,0,0)};placement.Children.Add(position);
  align.SelectionChanged+=(_,_)=>{if(align.SelectedItem is string alignment){preferences.Alignment=alignment;position.Visibility=alignment=="Personnalisée"?Visibility.Visible:Visibility.Collapsed;ApplyIconAlignment();SavePreferences();}};
  position.ValueChanged+=(_,_)=>{preferences.Position=position.Value;ApplyIconAlignment();SavePreferences();};content.Children.Add(Card(placement));
  var startup=new CheckBox {Content="Démarrer automatiquement avec Windows",IsChecked=AutoStartEnabled(),Margin=new Thickness(0,2,0,2)};
  startup.Click+=(_,_)=>{try{SetAutoStart(startup.IsChecked==true);}catch(Exception error){startup.IsChecked=AutoStartEnabled();MessageBox.Show(window,error.Message,"Démarrage automatique");}};content.Children.Add(Card(startup));
  var quit=new Button {Content="Quitter et rétablir la barre Windows"};quit.Click+=(_,_)=>Close();content.Children.Add(quit);
  window.Closed+=(_,_)=>settingsWindow=null;ApplyGlassFrame(window);window.Show();
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
   var centered=Math.Abs(Left-(SystemParameters.PrimaryScreenWidth-Width)/2)<.5;
   var inBounds=Width<=full&&Left>=0&&Left+Width<=SystemParameters.PrimaryScreenWidth;
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


