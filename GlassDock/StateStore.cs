using System.IO;
using System.Text.Json;
namespace GlassDock;
internal static class StateStore {
 internal static void Write<T>(string path,T value) {
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {
   using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
    JsonSerializer.Serialize(stream,value,new JsonSerializerOptions{WriteIndented=true});stream.Flush(true);
   }
   if(File.Exists(path)){
    bool valid=true;try{using var previous=JsonDocument.Parse(File.ReadAllText(path));}catch(JsonException){valid=false;}
    if(valid)File.Replace(temp,path,path+".bak");
    else{File.Move(path,path+".damaged-"+Guid.NewGuid().ToString("N"));File.Move(temp,path);}
   }else File.Move(temp,path);
  }finally{if(File.Exists(temp))File.Delete(temp);}
 }
 internal static T? Read<T>(string path) {
  foreach(var candidate in new[]{path,path+".bak"})try{
   if(File.Exists(candidate)){var value=JsonSerializer.Deserialize<T>(File.ReadAllText(candidate));if(value is not null)return value;}
  }catch(JsonException){}catch(IOException){}catch(UnauthorizedAccessException){}
  return default;
 }
 internal static string DirectoryForRun() {
  var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GlassDock");
  if(!Environment.GetCommandLineArgs().Any(x=>x=="--self-test"||x.StartsWith("--test-",StringComparison.Ordinal)))return root;
  var test=Path.Combine(root,"Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(test);
  foreach(var name in new[]{"dock.json","dock.json.bak","preferences.json","preferences.json.bak"})if(File.Exists(Path.Combine(root,name)))File.Copy(Path.Combine(root,name),Path.Combine(test,name));
  return test;
 }
}
