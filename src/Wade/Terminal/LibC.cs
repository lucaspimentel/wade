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
    private static extern int open(string pathname, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern nint read(int fd, byte[] buf, nint count);

    [DllImport("libc", SetLastError = true)]
    private static extern int poll(ref PollFd fds, int nfds, int timeout);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcgetattr(int fd, byte[] termios);

    [DllImport("libc", SetLastError = true)]
    private static extern int tcsetattr(int fd, int optionalActions, byte[] termios);

    [DllImport("libc")]
    private static extern void cfmakeraw(byte[] termios);

    public static int Open(string pathname, int flags) => open(pathname, flags);

    public static int Close(int fd) => close(fd);

    public static nint Read(int fd, byte[] buf, nint count) => read(fd, buf, count);

    /// <summary>
    /// Read into buf starting at the given offset.
    /// </summary>
    public static nint Read(int fd, byte[] buf, int offset, int count)
    {
        // Create a temporary buffer for the offset read, then copy back
        byte[] tmp = new byte[count];
        nint n = read(fd, tmp, count);
        if (n > 0)
        {
            Array.Copy(tmp, 0, buf, offset, (int)n);
        }

        return n;
    }

    public static int Poll(ref PollFd fds, int nfds, int timeout) => poll(ref fds, nfds, timeout);

    public static int Tcgetattr(int fd, byte[] termios) => tcgetattr(fd, termios);

    public static int Tcsetattr(int fd, int optionalActions, byte[] termios) => tcsetattr(fd, optionalActions, termios);

    public static void Cfmakeraw(byte[] termios) => cfmakeraw(termios);

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int fd;
        public short events;
        public short revents;
    }
}
