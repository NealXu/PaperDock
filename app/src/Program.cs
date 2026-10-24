using System;
using System.Linq;
using System.Windows;

namespace PaperDock
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            PdfEngine.InitializeFonts();
            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            var startupFile = args.FirstOrDefault(value =>
                !value.StartsWith("-", StringComparison.Ordinal) && System.IO.File.Exists(value));
            application.Run(new MainWindow(startupFile));
        }
    }
}
