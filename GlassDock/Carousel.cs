using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
namespace GlassDock;
public partial class MainWindow {
 async Task CheckCarouselAsync() {
  var report=new List<string>();
  try {
   preferences.DockWidth=50;preferences.Alignment="Centre";PositionDock();UpdateLayout();UpdateCarouselLayout();
   await Task.Delay(400);UpdateLayout();UpdateCarouselLayout();await Task.Delay(100);
   report.Add(Math.Abs(IconViewport.HorizontalOffset-IconViewport.ScrollableWidth/2)<1?"PASS: débordement centré":"FAIL: centre");
   report.Add($"offset={IconViewport.HorizontalOffset:0.0}, maximum={IconViewport.ScrollableWidth:0.0}");
   IconViewport.ScrollToHorizontalOffset(0);await Task.Delay(100);
   report.Add(IconViewport.HorizontalOffset<1?"PASS: accès au début":"FAIL: début");
   carouselSlider!.Value=carouselMaximum;await Task.Delay(100);
   report.Add(Math.Abs(IconViewport.HorizontalOffset-IconViewport.ScrollableWidth)<1?"PASS: accès à la fin par slider":"FAIL: fin");
   OpenSettings(this,new RoutedEventArgs());await Task.Delay(100);
   report.Add(settingsWindow is not null?"PASS: paramètres ouverts":"FAIL: paramètres");
   settingsWindow?.Close();
   using var startupRun=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(AutoStartKey);
   using var startupApproved=Microsoft.Win32.Registry.CurrentUser.CreateSubKey(StartupApprovedKey);
   var previousRun=startupRun.GetValue("GlassDock");var previousApproval=startupApproved.GetValue("GlassDock");
   try {
    SetAutoStart(true);report.Add(AutoStartEnabled()?"PASS: démarrage automatique activé":"FAIL: démarrage activé");
    SetAutoStart(false);report.Add(!AutoStartEnabled()?"PASS: démarrage automatique désactivé":"FAIL: démarrage désactivé");
   }finally {
    if(previousRun is null)startupRun.DeleteValue("GlassDock",false);else startupRun.SetValue("GlassDock",previousRun);
    if(previousApproval is null)startupApproved.DeleteValue("GlassDock",false);else startupApproved.SetValue("GlassDock",previousApproval);
   }
  }catch(Exception error){report.Add(error.ToString());}
  System.IO.File.WriteAllLines(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(savePath)!,"carousel-test.txt"),report);Close();
 }
 Slider? carouselSlider;
 DispatcherTimer? carouselTimer;
 double carouselMaximum,carouselTarget,carouselPosition=-1;
 string carouselAlignment="";
 bool carouselSync;
 void InitializeCarousel() {
  carouselSlider=new Slider {Minimum=0,Height=8,VerticalAlignment=VerticalAlignment.Bottom,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,0,0,1),IsMoveToPointEnabled=true,Visibility=Visibility.Collapsed,Focusable=false};
  carouselSlider.Template=(ControlTemplate)XamlReader.Parse("""
   <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider"><Grid><Border Height="3" CornerRadius="2" Background="#405E718A" VerticalAlignment="Center"/><Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}"><Track.DecreaseRepeatButton><RepeatButton Opacity="0" Focusable="False" Command="Slider.DecreaseLarge"/></Track.DecreaseRepeatButton><Track.IncreaseRepeatButton><RepeatButton Opacity="0" Focusable="False" Command="Slider.IncreaseLarge"/></Track.IncreaseRepeatButton><Track.Thumb><Thumb Width="26" Height="5"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border CornerRadius="3" Background="#FF84ACDF"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track></Grid></ControlTemplate>
   """);
  DockLayout.Children.Add(carouselSlider);Panel.SetZIndex(carouselSlider,3);
  carouselSlider.ValueChanged+=(_,_)=>{if(carouselSync)return;carouselTarget=carouselSlider.Value;IconViewport.ScrollToHorizontalOffset(carouselTarget);};
  IconViewport.ScrollChanged+=(_,_)=>{UpdateCarouselLayout();UpdateCarouselEdges();};
  IconViewport.MouseMove+=(_,e)=>{
   if(carouselMaximum<=0||dragging||IsWindowPreviewOpen||Mouse.LeftButton==MouseButtonState.Pressed)return;
   var x=e.GetPosition(IconViewport).X;var edge=Math.Min(56,IconViewport.ActualWidth/4);
   if(x<edge)carouselTarget=0;else if(x>IconViewport.ActualWidth-edge)carouselTarget=carouselMaximum;else{carouselTimer?.Stop();return;}
   carouselTimer?.Start();
  };
  IconViewport.MouseLeave+=(_,_)=>{carouselTimer?.Stop();UpdateCarouselLayout();};
  GlassSurface.MouseEnter+=(_,_)=>UpdateCarouselLayout();GlassSurface.MouseLeave+=(_,_)=>UpdateCarouselLayout();
  carouselTimer=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(16)};
  carouselTimer.Tick+=(_,_)=>{
   if(dragging||IsWindowPreviewOpen||!IconViewport.IsMouseOver){carouselTimer.Stop();return;}
   var delta=carouselTarget-IconViewport.HorizontalOffset;
   IconViewport.ScrollToHorizontalOffset(Math.Abs(delta)<.5?carouselTarget:IconViewport.HorizontalOffset+Math.Clamp(delta*.12,-10,10));
   if(Math.Abs(delta)<.5)carouselTimer.Stop();
  };
  Closed+=(_,_)=>carouselTimer.Stop();
 }
 void UpdateCarouselLayout() {
  if(carouselSlider is null)return;
  var maximum=Math.Max(0,ItemsPanel.DesiredSize.Width-IconViewport.Width);
  if(!double.IsFinite(maximum))return;
  bool alignmentChanged=carouselAlignment!=preferences.Alignment||carouselPosition!=preferences.Position;
  if(Math.Abs(maximum-carouselMaximum)>.5||alignmentChanged){
   var ratio=carouselMaximum>0&&!alignmentChanged?IconViewport.HorizontalOffset/carouselMaximum:preferences.Alignment switch{"Gauche"=>0,"Droite"=>1,"Personnalisée"=>preferences.Position/100,_=>.5};
   carouselMaximum=maximum;carouselAlignment=preferences.Alignment;carouselPosition=preferences.Position;carouselTarget=maximum*Math.Clamp(ratio,0,1);IconViewport.ScrollToHorizontalOffset(carouselTarget);
  }
  carouselSync=true;carouselSlider.Maximum=maximum;carouselSlider.Value=IconViewport.HorizontalOffset;carouselSync=false;
  carouselSlider.Width=IconViewport.Width;carouselSlider.Margin=new Thickness(IconViewport.Margin.Left,0,0,1);
  carouselSlider.Visibility=maximum>1&&GlassSurface.IsMouseOver?Visibility.Visible:Visibility.Collapsed;
  UpdateCarouselEdges();
 }
 void AddCarouselScale(Button button,FrameworkElement content,ScaleTransform hover) {
  var edge=new ScaleTransform(1,1);button.Resources["CarouselScale"]=edge;
  var transforms=new TransformGroup();transforms.Children.Add(edge);transforms.Children.Add(hover);content.RenderTransform=transforms;
 }
 void UpdateCarouselEdges() {
  if(dragging)return;
  foreach(var button in ItemsPanel.Children.OfType<Button>()){
   if(button.Resources["CarouselScale"] is not ScaleTransform scale)continue;
   var center=button.TranslatePoint(new Point(button.ActualWidth/2,0),IconViewport).X;
   var distance=Math.Min(center,IconViewport.ActualWidth-center);
   var t=carouselMaximum>1?Math.Clamp(distance/40,0,1):1;var smooth=t*t*(3-2*t);
   scale.ScaleX=scale.ScaleY=.25+.75*smooth;button.Opacity=.25+.75*smooth;
  }
 }
}

