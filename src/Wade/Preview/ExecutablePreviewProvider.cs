using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Wade.Highlighting;

namespace Wade.Preview;

internal sealed class ExecutablePreviewProvider : IPreviewProvider
{
    public string Label => "Executable metadata";

    public bool CanPreview(string path, PreviewContext context)
    {
        string ext = Path.GetExtension(path);
        return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".dll", StringComparison.OrdinalIgnoreCase);
    }

    public PreviewResult? GetPreview(string path, PreviewContext context, CancellationToken ct)
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

            var lines = new List<StyledLine>();
            string ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();

            // ── PE Headers ──
            lines.Add(new StyledLine("  Executable Info", null));
            lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

            string machine = FormatMachine(headers.CoffHeader.Machine);
            string bitness = headers.PEHeader is not null && headers.PEHeader.Magic == PEMagic.PE32Plus ? "PE32+" : "PE32";
            lines.Add(new StyledLine($"  {"Architecture",-14} {machine} ({bitness})", null));

            if (headers.PEHeader is not null)
            {
                lines.Add(new StyledLine($"  {"Subsystem",-14} {FormatSubsystem(headers.PEHeader.Subsystem)}", null));
            }

            bool isDll = (headers.CoffHeader.Characteristics & Characteristics.Dll) != 0;
            lines.Add(new StyledLine($"  {"Type",-14} {(isDll ? "DLL" : "Executable")}", null));

            DateTimeOffset timestamp = DateTimeOffset.FromUnixTimeSeconds(headers.CoffHeader.TimeDateStamp);
            if (timestamp.Year is >= 2000 and <= 2100)
            {
                lines.Add(new StyledLine($"  {"Timestamp",-14} {timestamp:yyyy-MM-dd HH:mm:ss} UTC", null));
            }

            if (ct.IsCancellationRequested)
            {
                return null;
            }

            // ── FileVersionInfo (Windows only) ──
            if (OperatingSystem.IsWindows())
            {
                AddFileVersionInfo(lines, path);
            }

            // ── .NET Assembly Metadata ──
            if (peReader.HasMetadata)
            {
                AddAssemblyMetadata(lines, peReader, ct);
            }

            return new PreviewResult
            {
                TextLines = lines.ToArray(),
                IsRendered = true,
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

    private static void AddFileVersionInfo(List<StyledLine> lines, string path)
    {
        FileVersionInfo fvi;

        try
        {
            fvi = FileVersionInfo.GetVersionInfo(path);
        }
        catch
        {
            return;
        }

        // Only show section if there's meaningful data
        bool hasData = !string.IsNullOrWhiteSpace(fvi.ProductName)
            || !string.IsNullOrWhiteSpace(fvi.FileDescription)
            || !string.IsNullOrWhiteSpace(fvi.CompanyName)
            || !string.IsNullOrWhiteSpace(fvi.FileVersion);

        if (!hasData)
        {
            return;
        }

        lines.Add(new StyledLine("", null));
        lines.Add(new StyledLine("  Version Info", null));
        lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

        AddFieldIfPresent(lines, "Product", fvi.ProductName);
        AddFieldIfPresent(lines, "Description", fvi.FileDescription);
        AddFieldIfPresent(lines, "Company", fvi.CompanyName);
        AddFieldIfPresent(lines, "Copyright", fvi.LegalCopyright);
        AddFieldIfPresent(lines, "File version", fvi.FileVersion);

        // Only show product version if different from file version
        if (!string.IsNullOrWhiteSpace(fvi.ProductVersion)
            && fvi.ProductVersion != fvi.FileVersion)
        {
            AddFieldIfPresent(lines, "Product ver.", fvi.ProductVersion);
        }

        AddFieldIfPresent(lines, "Original name", fvi.OriginalFilename);
    }

    private static void AddAssemblyMetadata(List<StyledLine> lines, PEReader peReader, CancellationToken ct)
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

        lines.Add(new StyledLine("", null));
        lines.Add(new StyledLine("  .NET Assembly", null));
        lines.Add(new StyledLine("  \u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500", null));

        AssemblyDefinition asmDef = mdReader.GetAssemblyDefinition();
        string asmName = mdReader.GetString(asmDef.Name);

        if (!string.IsNullOrEmpty(asmName))
        {
            lines.Add(new StyledLine($"  {"Name",-14} {asmName}", null));
        }

        if (asmDef.Version != default)
        {
            lines.Add(new StyledLine($"  {"Version",-14} {asmDef.Version}", null));
        }

        string? tfm = ReadTargetFramework(mdReader);

        if (tfm is not null)
        {
            lines.Add(new StyledLine($"  {"Framework",-14} {tfm}", null));
        }

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
            lines.Add(new StyledLine("", null));
            lines.Add(new StyledLine($"  Referenced Assemblies ({refs.Count}):", null));

            foreach ((string name, Version version) in refs)
            {
                lines.Add(new StyledLine($"    {name} {version}", null));
            }
        }
    }

    private static string? ReadTargetFramework(MetadataReader mdReader)
    {
        // TargetFrameworkAttribute is stored as a custom attribute on the assembly.
        // We read it via the low-level metadata API (blob decoding), not System.Reflection.
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
        // Custom attribute value blob format:
        // [2 bytes prolog (0x0001)] [string length (compressed uint)] [UTF-8 string bytes] ...
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

    private static void AddFieldIfPresent(List<StyledLine> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add(new StyledLine($"  {label,-14} {value}", null));
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
