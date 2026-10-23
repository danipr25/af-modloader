using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AFModLoader.Installer
{
    /// <summary>
    /// The modern Windows "choose a folder" dialog (the one Explorer uses). If Windows cannot create it, the old folder
    /// dialog is used instead.
    /// </summary>
    internal static class FolderPicker
    {
        private const uint FOS_PICKFOLDERS = 0x20;
        private const uint FOS_FORCEFILESYSTEM = 0x40;
        private const uint FOS_PATHMUSTEXIST = 0x800;
        private const uint SIGDN_FILESYSPATH = 0x80058000;
        private const int ERROR_CANCELLED = unchecked((int)0x800704C7);

        /// <summary>
        /// Returns the chosen folder, or null if the player cancelled. If the dialog fails, log says why (for Details) and
        /// the old folder dialog is used instead.
        /// </summary>
        public static string Pick(IWin32Window owner, string title, string startFolder, Action<string> log)
        {
            IFileOpenDialog dialog;
            try
            {
                dialog = (IFileOpenDialog)new FileOpenDialog();
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidCastException)
            {
                log("The Windows folder picker could not be created (" + ex.Message + "). Using the older dialog.");
                return PickWithOldDialog(owner, title, startFolder);
            }

            IShellItem start = null;
            IShellItem result = null;
            try
            {
                uint options;
                dialog.GetOptions(out options);
                dialog.SetOptions(options | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                dialog.SetTitle(title);
                if (!string.IsNullOrEmpty(startFolder))
                {
                    // A start folder that does not exist is not a problem: the dialog opens where it likes.
                    Guid shellItem = typeof(IShellItem).GUID;
                    if (SHCreateItemFromParsingName(startFolder, IntPtr.Zero, ref shellItem, out start) == 0 && start != null)
                    {
                        try
                        {
                            dialog.SetFolder(start);
                        }
                        catch (COMException)
                        {
                            // Keep going: the dialog opens in its default folder.
                        }
                    }
                }

                int hr = dialog.Show(owner.Handle);
                if (hr == ERROR_CANCELLED)
                {
                    return null;
                }

                if (hr != 0)
                {
                    log("The Windows folder picker failed (HRESULT 0x" + hr.ToString("X8") + "). Using the older dialog.");
                    return PickWithOldDialog(owner, title, startFolder);
                }

                dialog.GetResult(out result);
                IntPtr text;
                result.GetDisplayName(SIGDN_FILESYSPATH, out text);
                try
                {
                    return Marshal.PtrToStringUni(text);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(text);
                }
            }
            catch (COMException ex)
            {
                log("The Windows folder picker failed (HRESULT 0x" + ex.ErrorCode.ToString("X8") + "). Using the older dialog.");
                return PickWithOldDialog(owner, title, startFolder);
            }
            finally
            {
                Release(result);
                Release(start);
                Marshal.ReleaseComObject(dialog);
            }
        }

        private static string PickWithOldDialog(IWin32Window owner, string title, string startFolder)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = title;
                if (!string.IsNullOrEmpty(startFolder) && System.IO.Directory.Exists(startFolder))
                {
                    dialog.SelectedPath = startFolder;
                }

                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
            }
        }

        private static void Release(object comObject)
        {
            if (comObject != null)
            {
                Marshal.ReleaseComObject(comObject);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindContext,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem item);

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr bindContext, ref Guid handler, ref Guid riid, out IntPtr result);

            void GetParent(out IShellItem parent);

            void GetDisplayName(uint name, out IntPtr text);

            void GetAttributes(uint mask, out uint attributes);

            void Compare(IShellItem other, uint hint, out int order);
        }

        // The whole vtable, in order: IModalWindow, then IFileDialog, then IFileOpenDialog. Methods this code does not
        // call are declared with loose types, only to keep the positions right.
        [ComImport]
        [Guid("D57C7288-D4AD-4768-BE02-9D969532D960")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileOpenDialog
        {
            [PreserveSig]
            int Show(IntPtr owner);

            void SetFileTypes(uint count, IntPtr filters);

            void SetFileTypeIndex(uint index);

            void GetFileTypeIndex(out uint index);

            void Advise(IntPtr events, out uint cookie);

            void Unadvise(uint cookie);

            void SetOptions(uint options);

            void GetOptions(out uint options);

            void SetDefaultFolder(IShellItem folder);

            void SetFolder(IShellItem folder);

            void GetFolder(out IShellItem folder);

            void GetCurrentSelection(out IShellItem item);

            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);

            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);

            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);

            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string text);

            void GetResult(out IShellItem item);

            void AddPlace(IShellItem item, uint placement);

            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);

            void Close(int result);

            void SetClientGuid(ref Guid guid);

            void ClearClientData();

            void SetFilter(IntPtr filter);

            void GetResults(out IntPtr items);

            void GetSelectedItems(out IntPtr items);
        }
    }
}
