using System.Runtime.InteropServices;

namespace Wade.Terminal;

internal static class LibC
{
    public const int O_RDONLY = 0;
    public const int O_RDWR = 2;
    public const int TCSAFLUSH = 2;
    public const int TermiosSize = 256;
    public const short POLLIN = 0x0001;

    [DllImport("libc", SetLastError = true)]
    public static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    public static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    public static extern nint read(int fd, byte[] buf, nint count);

    /// <summary>
    /// Read into buf starting at the given offset.
    /// </summary>
    public static nint read(int fd, byte[] buf, int offset, int count)
    {
        // Create a temporary buffer for the offset read, then copy back
        var tmp = new byte[count];
        nint n = read(fd, tmp, count);
        if (n > 0)
        {
            Array.Copy(tmp, 0, buf, offset, (int)n);
        }

        return n;
    }

    [DllImport("libc", SetLastError = true)]
    public static extern int poll(ref PollFd fds, int nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    public static extern int tcgetattr(int fd, byte[] termios);

    [DllImport("libc", SetLastError = true)]
    public static extern int tcsetattr(int fd, int optionalActions, byte[] termios);

    [DllImport("libc")]
    public static extern void cfmakeraw(byte[] termios);

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }
}
