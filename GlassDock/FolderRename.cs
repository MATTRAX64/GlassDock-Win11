using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace GlassDock;
public partial class MainWindow {
 ContextMenu BuildFolderRenameMenu(DockItem folder) {
  var menu=new ContextMenu {Padding=new Thickness(0),MinWidth=0,Placement=System.Windows.Controls.Primitives.PlacementMode.Custom};
  menu.CustomPopupPlacementCallback=(popup,target,offset)=>new[]{new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point((target.Width-popup.Width)/2,-popup.Height-8),System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)};
  menu.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
   <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ContextMenu"><Border Background="{TemplateBinding Background}" CornerRadius="9"><StackPanel IsItemsHost="True"/></Border></ControlTemplate>
   """);
  menu.Opened+=(_,_)=> {
   menu.Background=new SolidColorBrush(Dark?Color.FromRgb(43,48,58):Color.FromRgb(232,237,244));
   menu.Items.Clear();
   var header=new Grid {Margin=new Thickness(12,5,7,5)};
   header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});header.ColumnDefinitions.Add(new ColumnDefinition {Width=GridLength.Auto});
   var title=new TextBlock {Text=folder.Name,FontSize=14,Foreground=Ink,VerticalAlignment=VerticalAlignment.Center,Cursor=Cursors.IBeam,MaxWidth=190,TextTrimming=TextTrimming.CharacterEllipsis};header.Children.Add(title);
   var close=new Button {Width=18,Height=18,Margin=new Thickness(7,0,0,0),Padding=new Thickness(0),Focusable=false,ToolTip="Fermer"};
   close.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
    <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button"><Border CornerRadius="9" Background="Transparent" BorderBrush="#304F5867" BorderThickness="1"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border></ControlTemplate>
    """);
   close.Content=new System.Windows.Shapes.Path {Data=Geometry.Parse("M2,2 L10,10 M10,2 L2,10"),Stroke=Ink,StrokeThickness=1.5,Width=12,Height=12};
   Grid.SetColumn(close,1);header.Children.Add(close);
   var row=new MenuItem {Header=header,StaysOpenOnClick=true,Padding=new Thickness(0)};
   row.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse("""
    <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="MenuItem"><ContentPresenter ContentSource="Header"/></ControlTemplate>
    """);menu.Items.Add(row);
   TextBox? editor=null;bool finished=false;
   void Finish(bool save) {
    if(finished)return;finished=true;
    var name=editor?.Text.Trim();
    if(save&&!string.IsNullOrEmpty(name)&&name!=folder.Name) {
     folder.Name=name;Save();
     if(ReferenceEquals(openedFolder,folder)&&folderWindow is not null){folderWindow.Title=name;BuildFolderGrid();}
    }
    menu.IsOpen=false;
   }
   close.Click+=(_,_)=>Finish(true);
   title.MouseLeftButtonDown+=(_,click)=> {
    click.Handled=true;if(editor is not null)return;
    editor=new TextBox {Text=folder.Name,FontSize=14,Foreground=Ink,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),MinWidth=40,MaxWidth=190,MaxLength=120,VerticalContentAlignment=VerticalAlignment.Center};
    header.Children.Remove(title);header.Children.Add(editor);
    editor.PreviewKeyDown+=(_,key)=>{if(key.Key==Key.Enter){key.Handled=true;Finish(true);}else if(key.Key==Key.Escape){key.Handled=true;Finish(false);}};
    editor.LostKeyboardFocus+=(_,_)=>Finish(true);
    editor.Focus();editor.SelectAll();
   };
   RoutedEventHandler? closed=null;closed=(_,_)=>{menu.Closed-=closed;Finish(true);};menu.Closed+=closed;
   menu.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(()=>CenterAppMenu(menu)));
  };
  return menu;
 }
 void EnableFolderTitleRename(Window window,DockItem folder) {
  if(!glassFrames.TryGetValue(window,out var frame))return;
  frame.Title.Cursor=Cursors.IBeam;frame.Title.ToolTip="Cliquer pour renommer le dossier";
  TextBox? editor=null;
  frame.Title.MouseLeftButtonDown+=(_,e)=>{
   e.Handled=true;if(editor is not null)return;
   editor=new TextBox {Text=folder.Name,FontSize=14,Foreground=Ink,Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),MinWidth=80,MaxWidth=190,MaxLength=120,TextAlignment=TextAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center};
   frame.Title.Visibility=Visibility.Collapsed;frame.Header.Children.Add(editor);
   void Finish(bool save) {
    if(editor is null)return;
    var current=editor;editor=null;var name=current.Text.Trim();
    frame.Header.Children.Remove(current);frame.Title.Visibility=Visibility.Visible;
    if(save&&name.Length>0&&name!=folder.Name){folder.Name=name;window.Title=name;frame.Title.Text=name;Save();}
    PositionFolderPopup();
   }
   editor.KeyDown+=(_,key)=>{if(key.Key==Key.Enter){key.Handled=true;Finish(true);}else if(key.Key==Key.Escape){key.Handled=true;Finish(false);}};
   editor.LostKeyboardFocus+=(_,_)=>Finish(true);
   editor.Focus();editor.SelectAll();
  };
 }
}
