using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace GlassDock;
public partial class MainWindow {
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
