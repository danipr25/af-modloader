using System;
using System.Windows.Forms;

namespace AFModLoader.Installer
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            // "--game <folder>" pre-fills the folder field (for testing with a copy of the game folder).
            string game = null;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] == "--game")
                {
                    game = args[i + 1];
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(game));
        }
    }
}
