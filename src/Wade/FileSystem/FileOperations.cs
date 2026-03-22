using System.Runtime.InteropServices;

namespace Wade.FileSystem;

internal static class FileOperations
{
    /// <summary>
    /// Deletes the specified paths. On Windows with permanent=false, sends to Recycle Bin.
    /// Returns the number of errors encountered.
    /// </summary>
    public static int Delete(IReadOnlyList<string> paths, bool permanent = false)
    {
        if (paths.Count == 0)
        {
            return 0;
        }

        if (!permanent && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return Shell32.RecycleFiles(paths);
        }

        int errors = 0;

        foreach (string path in paths)
        {
            try
            {
                // Check symlink FIRST — Directory.Exists follows the link on Windows
                var fileInfo = new FileInfo(path);
                if (fileInfo.LinkTarget != null)
                {
                    if (Directory.Exists(path))
                    {
                        Directory.Delete(path, false);
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
                else
                {
                    errors++;
                }
            }
            catch
            {
                errors++;
            }
        }

        return errors;
    }

    public static void CopyDirectory(string source, string destination, bool preserveSymlinks = true)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.GetFiles(source))
        {
            string destFile = Path.Combine(destination, Path.GetFileName(file));
            if (preserveSymlinks)
            {
                var info = new FileInfo(file);
                if (info.LinkTarget != null)
                {
                    try
                    {
                        File.CreateSymbolicLink(destFile, info.LinkTarget);
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }

            File.Copy(file, destFile);
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            string destDir = Path.Combine(destination, Path.GetFileName(dir));
            if (preserveSymlinks)
            {
                var info = new DirectoryInfo(dir);
                if (info.LinkTarget != null)
                {
                    try
                    {
                        Directory.CreateSymbolicLink(destDir, info.LinkTarget);
                        continue;
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }

            CopyDirectory(dir, destDir, preserveSymlinks);
        }
    }
}
