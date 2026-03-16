using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Wade.Preview;

internal sealed class ExecutableMetadataProvider : IMetadataProvider
{
    public string Label => "Executable metadata";

    public bool CanProvideMetadata(string path, PreviewContext context)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream);
            PEHeaders headers = peReader.PEHeaders;

            var sections = new List<MetadataSection>();
            string ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

            // ── PE Headers ──
            var peEntries = new List<MetadataEntry>();

            string machine = FormatMachine(headers.CoffHeader.Machine);
            string bitness = headers.PEHeader is not null && headers.PEHeader.Magic == PEMagic.PE32Plus ? "PE32+" : "PE32";
            peEntries.Add(new MetadataEntry("Architecture", $"{machine} ({bitness})"));

            if (headers.PEHeader is not null)
            {
                peEntries.Add(new MetadataEntry("Subsystem", FormatSubsystem(headers.PEHeader.Subsystem)));
            }

            bool isDll = (headers.CoffHeader.Characteristics & Characteristics.Dll) != 0;
            peEntries.Add(new MetadataEntry("Type", isDll ? "DLL" : "Executable"));

            DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(headers.CoffHeader.TimeDateStamp);
            if (timestamp.Year is >= 2000 and <= 2100)
            {
                peEntries.Add(new MetadataEntry("Timestamp", $"{timestamp:yyyy-MM-dd HH:mm:ss} UTC"));
            }

            sections.Add(new MetadataSection("Executable Info", peEntries.ToArray()));

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            // ── FileVersionInfo (Windows only) ──
            if (OperatingSystem.IsWindows())
            {
                MetadataSection? versionSection = GetFileVersionInfoSection(path);
                if (versionSection is not null)
                {
                    sections.Add(versionSection);
                }
            }

            // ── .NET Assembly Metadata ──
            if (peReader.HasMetadata)
            {
                GetAssemblyMetadataSections(sections, peReader, ct);
            }

            return new MetadataResult
            {
                Sections = sections.ToArray(),
                FileTypeLabel = ext,
            };
        }
        catch (BadImageFormatException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static MetadataSection? GetFileVersionInfoSection(string path)
    {
        FileVersionInfo fvi;

        try
        {
            fvi = FileVersionInfo.GetVersionInfo(path);
        }
        catch
        {
            return null;
        }

        bool hasData = !string.IsNullOrWhiteSpace(fvi.ProductName)
            || !string.IsNullOrWhiteSpace(fvi.FileDescription)
            || !string.IsNullOrWhiteSpace(fvi.CompanyName)
            || !string.IsNullOrWhiteSpace(fvi.FileVersion);

        if (!hasData)
        {
            return null;
        }

        var entries = new List<MetadataEntry>();

        AddEntryIfPresent(entries, "Product", fvi.ProductName);
        AddEntryIfPresent(entries, "Description", fvi.FileDescription);
        AddEntryIfPresent(entries, "Company", fvi.CompanyName);
        AddEntryIfPresent(entries, "Copyright", fvi.LegalCopyright);
        AddEntryIfPresent(entries, "File version", fvi.FileVersion);

        if (!string.IsNullOrWhiteSpace(fvi.ProductVersion)
            && fvi.ProductVersion != fvi.FileVersion)
        {
            AddEntryIfPresent(entries, "Product ver.", fvi.ProductVersion);
        }

        AddEntryIfPresent(entries, "Original name", fvi.OriginalFilename);

        return new MetadataSection("Version Info", entries.ToArray());
    }

    private static void GetAssemblyMetadataSections(List<MetadataSection> sections, PEReader peReader, CancellationToken ct)
    {
        MetadataReader mdReader;

        try
        {
            mdReader = peReader.GetMetadataReader();
        }
        catch (BadImageFormatException)
        {
            return;
        }

        var entries = new List<MetadataEntry>();
        AssemblyDefinition asmDef = mdReader.GetAssemblyDefinition();
        string asmName = mdReader.GetString(asmDef.Name);

        if (!string.IsNullOrEmpty(asmName))
        {
            entries.Add(new MetadataEntry("Name", asmName));
        }

        if (asmDef.Version != default)
        {
            entries.Add(new MetadataEntry("Version", asmDef.Version.ToString()));
        }

        string? tfm = ReadTargetFramework(mdReader);

        if (tfm is not null)
        {
            entries.Add(new MetadataEntry("Framework", tfm));
        }

        sections.Add(new MetadataSection(".NET Assembly", entries.ToArray()));

        if (ct.IsCancellationRequested)
        {
            return;
        }

        // Referenced assemblies
        var refs = new List<(string Name, Version Version)>();

        foreach (AssemblyReferenceHandle refHandle in mdReader.AssemblyReferences)
        {
            AssemblyReference asmRef = mdReader.GetAssemblyReference(refHandle);
            refs.Add((mdReader.GetString(asmRef.Name), asmRef.Version));
        }

        if (refs.Count > 0)
        {
            var refEntries = new List<MetadataEntry>();
            foreach ((string name, Version version) in refs)
            {
                refEntries.Add(new MetadataEntry("", $"{name} {version}"));
            }

            sections.Add(new MetadataSection($"Referenced Assemblies ({refs.Count})", refEntries.ToArray()));
        }
    }

    private static string? ReadTargetFramework(MetadataReader mdReader)
    {
        foreach (CustomAttributeHandle attrHandle in mdReader.GetAssemblyDefinition().GetCustomAttributes())
        {
            CustomAttribute attr = mdReader.GetCustomAttribute(attrHandle);

            if (attr.Constructor.Kind == HandleKind.MemberReference)
            {
                MemberReference memberRef = mdReader.GetMemberReference((MemberReferenceHandle)attr.Constructor);

                if (memberRef.Parent.Kind == HandleKind.TypeReference)
                {
                    TypeReference typeRef = mdReader.GetTypeReference((TypeReferenceHandle)memberRef.Parent);
                    string typeName = mdReader.GetString(typeRef.Name);

                    if (typeName == "TargetFrameworkAttribute")
                    {
                        return DecodeTargetFrameworkValue(mdReader, attr);
                    }
                }
            }
        }

        return null;
    }

    private static string? DecodeTargetFrameworkValue(MetadataReader mdReader, CustomAttribute attr)
    {
        BlobReader blobReader = mdReader.GetBlobReader(attr.Value);

        if (blobReader.Length < 4)
        {
            return null;
        }

        ushort prolog = blobReader.ReadUInt16();

        if (prolog != 0x0001)
        {
            return null;
        }

        try
        {
            return blobReader.ReadSerializedString();
        }
        catch
        {
            return null;
        }
    }

    private static void AddEntryIfPresent(List<MetadataEntry> entries, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            entries.Add(new MetadataEntry(label, value));
        }
    }

    private static string FormatMachine(Machine machine) =>
        machine switch
        {
            Machine.I386 => "x86",
            Machine.Amd64 => "x64",
            Machine.Arm => "ARM",
            Machine.Arm64 => "ARM64",
            _ => machine.ToString(),
        };

    private static string FormatSubsystem(Subsystem subsystem) =>
        subsystem switch
        {
            Subsystem.WindowsCui => "Console",
            Subsystem.WindowsGui => "GUI",
            Subsystem.EfiApplication => "EFI Application",
            Subsystem.EfiBootServiceDriver => "EFI Boot Service Driver",
            Subsystem.EfiRuntimeDriver => "EFI Runtime Driver",
            _ => subsystem.ToString(),
        };
}
