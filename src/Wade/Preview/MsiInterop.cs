using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Wade.Preview;

/// <summary>
/// Record holding a file entry from the MSI File table.
/// </summary>
internal record MsiFileEntry(string FileName, int FileSize);

/// <summary>
/// Record holding summary information from the MSI summary info stream.
/// </summary>
internal record MsiSummaryInfo(
    string? Subject,
    string? Author,
    string? Template,
    string? Comments,
    int? WordCount);

/// <summary>
/// Pure string helpers for MSI file name parsing (platform-independent).
/// </summary>
internal static class MsiFileName
{
    /// <summary>
    /// Parses MSI FileName column format: "shortname|longname" -> "longname".
    /// If no pipe is present, returns the original string.
    /// Only splits on the first pipe character.
    /// </summary>
    public static string ParseLongName(string rawName)
    {
        if (string.IsNullOrEmpty(rawName))
        {
            return rawName;
        }

        int pipeIndex = rawName.IndexOf('|');
        return pipeIndex >= 0 ? rawName[(pipeIndex + 1)..] : rawName;
    }
}

/// <summary>
/// Low-level P/Invoke wrappers for msi.dll (Windows Installer database API).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MsiInterop
{
    private const string MsiLib = "msi.dll";

    // MsiOpenDatabase persist modes
    private const int MSIDBOPEN_READONLY = 0;

    // Summary info property IDs
    private const int PID_SUBJECT = 3;
    private const int PID_AUTHOR = 4;
    private const int PID_TEMPLATE = 7;
    private const int PID_COMMENTS = 6;
    private const int PID_WORDCOUNT = 15;

    // Summary info property types
    private const int VT_LPSTR = 30;
    private const int VT_I4 = 3;

    [DllImport(MsiLib, CharSet = CharSet.Unicode)]
    private static extern uint MsiOpenDatabase(string szDatabasePath, nint szPersist, out nint phDatabase);

    [DllImport(MsiLib, CharSet = CharSet.Unicode)]
    private static extern uint MsiDatabaseOpenView(nint hDatabase, string szQuery, out nint phView);

    [DllImport(MsiLib, CharSet = CharSet.Unicode)]
    private static extern uint MsiViewExecute(nint hView, nint hRecord);

    [DllImport(MsiLib, CharSet = CharSet.Unicode)]
    private static extern uint MsiViewFetch(nint hView, out nint phRecord);

    [DllImport(MsiLib, CharSet = CharSet.Unicode)]
    private static extern uint MsiRecordGetString(nint hRecord, uint iField, char[] szValueBuf, ref uint pcchValueBuf);

    [DllImport(MsiLib, CharSet = CharSet.Unicode)]
    private static extern int MsiRecordGetInteger(nint hRecord, uint iField);

    [DllImport(MsiLib, CharSet = CharSet.Unicode)]
    private static extern uint MsiCloseHandle(nint hAny);

    [DllImport(MsiLib, CharSet = CharSet.Unicode)]
    private static extern uint MsiGetSummaryInformation(nint hDatabase, string? szDatabasePath, uint uiUpdateCount, out nint phSummaryInfo);

    [DllImport(MsiLib, CharSet = CharSet.Unicode)]
    private static extern uint MsiSummaryInfoGetProperty(
        nint hSummaryInfo,
        uint uiProperty,
        out uint puiDataType,
        out int piValue,
        out long pftValue,
        char[] szValueBuf,
        ref uint pcchValueBuf);

    /// <summary>
    /// Opens an MSI database in read-only mode. Returns the handle, or 0 on failure.
    /// </summary>
    public static nint OpenDatabaseReadOnly(string path)
    {
        uint result = MsiOpenDatabase(path, MSIDBOPEN_READONLY, out nint handle);
        return result == 0 ? handle : 0;
    }

    /// <summary>
    /// Queries the Property table and returns all property name/value pairs.
    /// </summary>
    public static Dictionary<string, string> QueryPropertyTable(nint db)
    {
        var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        uint result = MsiDatabaseOpenView(db, "SELECT Property, Value FROM Property", out nint view);
        if (result != 0)
        {
            return properties;
        }

        try
        {
            result = MsiViewExecute(view, 0);
            if (result != 0)
            {
                return properties;
            }

            while (MsiViewFetch(view, out nint record) == 0)
            {
                try
                {
                    string? name = GetRecordString(record, 1);
                    string? value = GetRecordString(record, 2);

                    if (name is not null && value is not null)
                    {
                        properties[name] = value;
                    }
                }
                finally
                {
                    MsiCloseHandle(record);
                }
            }
        }
        finally
        {
            MsiCloseHandle(view);
        }

        return properties;
    }

    /// <summary>
    /// Queries the File table and returns file entries with name and size.
    /// </summary>
    public static List<MsiFileEntry> QueryFileTable(nint db)
    {
        var files = new List<MsiFileEntry>();

        uint result = MsiDatabaseOpenView(db, "SELECT FileName, FileSize FROM File", out nint view);
        if (result != 0)
        {
            return files;
        }

        try
        {
            result = MsiViewExecute(view, 0);
            if (result != 0)
            {
                return files;
            }

            while (MsiViewFetch(view, out nint record) == 0)
            {
                try
                {
                    string? rawName = GetRecordString(record, 1);
                    int size = MsiRecordGetInteger(record, 2);

                    if (rawName is not null)
                    {
                        string fileName = MsiFileName.ParseLongName(rawName);
                        files.Add(new MsiFileEntry(fileName, size));
                    }
                }
                finally
                {
                    MsiCloseHandle(record);
                }
            }
        }
        finally
        {
            MsiCloseHandle(view);
        }

        return files;
    }

    /// <summary>
    /// Reads summary information properties from the MSI database.
    /// </summary>
    public static MsiSummaryInfo? GetSummaryInfo(nint db)
    {
        uint result = MsiGetSummaryInformation(db, null, 0, out nint summaryInfo);
        if (result != 0)
        {
            return null;
        }

        try
        {
            string? subject = GetSummaryStringProperty(summaryInfo, PID_SUBJECT);
            string? author = GetSummaryStringProperty(summaryInfo, PID_AUTHOR);
            string? template = GetSummaryStringProperty(summaryInfo, PID_TEMPLATE);
            string? comments = GetSummaryStringProperty(summaryInfo, PID_COMMENTS);
            int? wordCount = GetSummaryIntProperty(summaryInfo, PID_WORDCOUNT);

            return new MsiSummaryInfo(subject, author, template, comments, wordCount);
        }
        finally
        {
            MsiCloseHandle(summaryInfo);
        }
    }

    /// <summary>
    /// Closes an MSI database handle.
    /// </summary>
    public static void CloseDatabase(nint db)
    {
        if (db != 0)
        {
            MsiCloseHandle(db);
        }
    }

    private static string? GetRecordString(nint record, uint field)
    {
        uint size = 256;
        var buffer = new char[size];
        uint result = MsiRecordGetString(record, field, buffer, ref size);

        if (result == 234) // ERROR_MORE_DATA
        {
            size++; // size now holds required length excluding null
            buffer = new char[size];
            result = MsiRecordGetString(record, field, buffer, ref size);
        }

        return result == 0 ? new string(buffer, 0, (int)size) : null;
    }

    private static string? GetSummaryStringProperty(nint summaryInfo, int propertyId)
    {
        uint size = 256;
        var buffer = new char[size];
        uint result = MsiSummaryInfoGetProperty(
            summaryInfo, (uint)propertyId,
            out uint dataType, out _, out _, buffer, ref size);

        if (result == 234) // ERROR_MORE_DATA
        {
            size++;
            buffer = new char[size];
            result = MsiSummaryInfoGetProperty(
                summaryInfo, (uint)propertyId,
                out dataType, out _, out _, buffer, ref size);
        }

        if (result != 0 || dataType != VT_LPSTR)
        {
            return null;
        }

        string value = new(buffer, 0, (int)size);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static int? GetSummaryIntProperty(nint summaryInfo, int propertyId)
    {
        uint size = 0;
        uint result = MsiSummaryInfoGetProperty(
            summaryInfo, (uint)propertyId,
            out uint dataType, out int intValue, out _, [], ref size);

        if (result != 0 || dataType != VT_I4)
        {
            return null;
        }

        return intValue;
    }
}
