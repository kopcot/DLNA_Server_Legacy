namespace DLNAServer.Helpers.Files
{
    internal static class DirectoryHelper
    {
        public static void CreateDirectoryIfNoExists(DirectoryInfo? directory)
        {
            ArgumentNullException.ThrowIfNull(directory);

            if (!directory.Exists)
            {
                directory.Create();
                if (OperatingSystem.IsLinux())
                {
                    directory.UnixFileMode =
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
                }
            }
        }
        public static int GetDirectoryDepth(string? actualFolder)
        {
            if (actualFolder == null)
            {
                return 0;
            }

            DirectoryInfo directoryInfoDepthCount = new(actualFolder);
            int depth = 0;
            while (directoryInfoDepthCount.Parent != null)
            {
                depth++;
                directoryInfoDepthCount = directoryInfoDepthCount.Parent;
            }
            return depth;
        }
    }
}