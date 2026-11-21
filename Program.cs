using System;
using System.Collections.Generic;
using System.Windows;
using System.Text;
using System.IO;
using System.Linq;

namespace WeChatCleaner {
 public static class Program {
  [STAThread] public static void Main(string[] args) {
   try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch {}
   if (args.Length > 0 && args[0] == "--self-test") { Environment.ExitCode = SelfTest.Run(); return; }
   if (args.Length > 1 && args[0] == "--export-test") { ExportTest.Run(args[1]); return; }
   if (args.Length > 0 && args[0] == "--detect-names") { DetectNames.Run(); return; }
   if (args.Length > 0 && args[0] == "--client-check") {
    Console.WriteLine("Old:  "+Engine.ClientRunningInfo(SourceKind.Old));
    Console.WriteLine("New:  "+Engine.ClientRunningInfo(SourceKind.New));
    Console.WriteLine("Work: "+Engine.ClientRunningInfo(SourceKind.Work));
    return;
   }
   ConfigureDpi();
   var app = new Application();
   try { app.Resources.MergedDictionaries.Add((ResourceDictionary)System.Windows.Markup.XamlReader.Parse(Theme.Xaml)); } catch (Exception ex) { MessageBox.Show("主题加载失败：" + ex.Message); }
   app.DispatcherUnhandledException += delegate(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) {
    LogCrash(e.Exception);
    MessageBox.Show("程序遇到问题：\n" + e.Exception.Message + "\n\n详细信息已写入 error.log", "WeChat Cleaner", MessageBoxButton.OK, MessageBoxImage.Error);
    e.Handled = true;
   };
   try {
    app.Run(new MainWindow());
   } catch (Exception ex) {
    LogCrash(ex);
    MessageBox.Show("启动失败：\n" + ex.Message, "WeChat Cleaner", MessageBoxButton.OK, MessageBoxImage.Error);
   }
  }
  internal static void ConfigureDpi() {
   // WPF per-monitor scaling is available starting with Windows 10 1607.
   if (Environment.OSVersion.Version >= new Version(10,0,14393))
    AppContext.SetSwitch("Switch.System.Windows.DoNotScaleForDpiChanges",false);
  }
  static void LogCrash(Exception ex) {
   try {
    string dir = SourceStore.Folder;
    System.IO.Directory.CreateDirectory(dir);
    System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "error.log"), ex.ToString(), Encoding.UTF8);
   } catch {}
  }
 }

 public static class ExportTest {
  /// Writes a sample csv and xlsx so the export can be verified without the user interface.
  public static void Run(string dir) {
   try {
    Directory.CreateDirectory(dir);
    var header = new List<string>{ "序号", "文件名", "类型", "大小", "大小(字节)", "创建时间", "修改时间", "完整路径" };
    var rows = new List<object[]>();
    for (int i = 1; i <= 25; i++) {
     rows.Add(new object[]{ i, "示例文件 " + i + ".pdf", ".pdf", "1.23 MB", 1290342L,
      "2026-11-20 10:0" + (i%10) + ":00", "2026-11-19 09:1" + (i%10) + ":00",
      "D:\\Documents\\WeChat Files\\wxid_test\\FileStorage\\File\\2026-11\\示例文件 " + i + ".pdf" });
    }
    string csv = Path.Combine(dir, "export_sample.csv");
    string xlsx = Path.Combine(dir, "export_sample.xlsx");
    TableExport.WriteCsv(csv, header, rows);
    TableExport.WriteXlsx(xlsx, header, rows);
    Console.WriteLine("EXPORT csv=" + csv + " size=" + new FileInfo(csv).Length);
    Console.WriteLine("EXPORT xlsx=" + xlsx + " size=" + new FileInfo(xlsx).Length);
    return;
   } catch (Exception ex) {
    Console.Error.WriteLine(ex.ToString());
   }
  }
 }

 public static class DetectNames {
  /// Prints the resolved account identity for each known data source (diagnostic).
  public static void Run() {
   foreach (var a in SourceDiscovery.Find(SourceStore.Load(),DiscoveryLocations.Windows(),System.Threading.CancellationToken.None)) {
     var id = NameResolver.Resolve(a);
     string line = "ACCOUNT " + a.Kind + " " + a.Id +
      " -> nickname=" + (id.Nickname ?? "<未识别>") +
      " corp=" + (id.CorpName ?? "<无>") +
      " source=" + (id.Source ?? "<无>");
     Console.WriteLine(line);
     try {
      string outFile = Environment.GetEnvironmentVariable("WCC_NAME_OUT");
      if (!String.IsNullOrEmpty(outFile)) File.AppendAllText(outFile, line + Environment.NewLine, Encoding.UTF8);
     } catch {}
   }
  }
 }

}
