using System;
using System.Windows.Forms;

namespace FfxTool.Gui
{
    // M3 Expressive Bold entry — Win7 net48, PC font for text, direct SVG for symbols
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
