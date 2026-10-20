using System;

namespace AFModLoader.Installer.Core
{
    /// <summary>A problem the player can understand. The message is shown as it is.</summary>
    public sealed class InstallerException : Exception
    {
        public InstallerException(string message)
            : base(message)
        {
        }

        public InstallerException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
