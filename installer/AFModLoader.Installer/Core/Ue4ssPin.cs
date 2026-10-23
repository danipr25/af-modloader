using System.Globalization;

namespace AFModLoader.Installer.Core
{
    /// <summary>The one UE4SS build this installer installs. Changing it is its own reviewed change.</summary>
    public sealed class Ue4ssPin
    {
        public static readonly Ue4ssPin Current = new Ue4ssPin(
            "v3.0.1-1181",
            "https://github.com/UE4SS-RE/RE-UE4SS/releases/download/experimental/UE4SS_v3.0.1-1181-g83330635.zip",
            8748315,
            "b9a166a756605b21058a64cde93bcaab1a855dba3553e5c7e6ce0fa8fdfad7b9");

        public Ue4ssPin(string build, string url, long size, string sha256)
        {
            Build = build;
            Url = url;
            Size = size;
            Sha256 = sha256;
        }

        /// <summary>Short name of the build, for example v3.0.1-1181.</summary>
        public string Build { get; }

        public string Url { get; }

        public long Size { get; }

        /// <summary>Lowercase hex.</summary>
        public string Sha256 { get; }

        /// <summary>The size for the window, in decimal megabytes: 8.7 MB.</summary>
        public string SizeText
        {
            get { return FormatMegabytes(Size) + " MB"; }
        }

        /// <summary>Bytes as decimal megabytes with one decimal, always with a point (3.2).</summary>
        public static string FormatMegabytes(long bytes)
        {
            return (bytes / 1000000.0).ToString("0.0", CultureInfo.InvariantCulture);
        }

        /// <summary>First and last four characters, for the window.</summary>
        public string ShortHash
        {
            get { return Sha256.Substring(0, 4) + "..." + Sha256.Substring(Sha256.Length - 4); }
        }
    }
}
