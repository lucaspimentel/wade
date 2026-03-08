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
                if (Directory.Exists(path))
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

    public static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.GetFiles(source))
        {
            string destFile = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, destFile);
        }

        foreach (string dir in Directory.GetDirectories(source))
        {
            string destDir = Path.Combine(destination, Path.GetFileName(dir));
            CopyDirectory(dir, destDir);
        }
    }
}
