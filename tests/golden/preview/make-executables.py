#!/usr/bin/env python3
"""Writes the PE fixtures in tests/golden/preview/executables.

Hand-built native and .NET images covering what ExecutableMetadataProvider
shows (machine, bitness, subsystem, DLL flag, timestamp, assembly name,
version, target framework, references) plus malformed variants. None
carry a version resource, so the golden is the same on every OS. Rerun
only to change them, then delete executables.golden.txt and regenerate it
with the C# test.
"""

import os
import struct

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "executables")

FILE_ALIGN = 0x200
TEXT_RVA = 0x2000


def dos_header(e_lfanew=0x80):
    header = bytearray(0x80)
    header[0:2] = b"MZ"
    struct.pack_into("<I", header, 0x3C, e_lfanew)
    return bytes(header)


def coff(machine, sections, timestamp, opt_size, characteristics):
    return struct.pack("<HHIIIHH", machine, sections, timestamp & 0xFFFFFFFF, 0, 0, opt_size, characteristics)


def optional_header(pe32plus, subsystem, directories, size_of_image=0x4000):
    dirs = list(directories) + [(0, 0)] * (16 - len(directories))
    if pe32plus:
        fields = struct.pack("<HBBIIIII", 0x20B, 14, 0, 0x200, 0, 0, 0, TEXT_RVA)
        fields += struct.pack("<Q", 0x180000000)
    else:
        fields = struct.pack("<HBBIIIIII", 0x10B, 14, 0, 0x200, 0, 0, 0, TEXT_RVA, TEXT_RVA)
        fields += struct.pack("<I", 0x400000)
    fields += struct.pack("<IIHHHHHHIIIIHH", 0x2000, FILE_ALIGN, 6, 0, 0, 0, 6, 0, 0, size_of_image, 0x200, 0,
                          subsystem, 0x8160)
    if pe32plus:
        fields += struct.pack("<QQQQ", 0x100000, 0x1000, 0x100000, 0x1000)
    else:
        fields += struct.pack("<IIII", 0x100000, 0x1000, 0x100000, 0x1000)
    fields += struct.pack("<II", 0, 16)
    fields += b"".join(struct.pack("<II", rva, size) for rva, size in dirs)
    return fields


def section(name, virtual_size, rva, raw_size, raw_ptr):
    return name.ljust(8, b"\0") + struct.pack("<IIIIIIHHI", virtual_size, rva, raw_size, raw_ptr, 0, 0, 0, 0,
                                              0x60000020)


def image(machine=0x8664, pe32plus=True, subsystem=3, timestamp=1614834367, dll=False, text=b"", directories=(),
          sections=None):
    """A PE image with one .text section holding `text` at TEXT_RVA."""
    opt = optional_header(pe32plus, subsystem, directories)
    raw_size = max(FILE_ALIGN, (len(text) + FILE_ALIGN - 1) // FILE_ALIGN * FILE_ALIGN)
    section_count = 1 if sections is None else sections
    characteristics = 0x0022 | (0x2000 if dll else 0)
    headers = dos_header() + b"PE\0\0" + coff(machine, section_count, timestamp, len(opt), characteristics) + opt
    headers += section(b".text", max(len(text), 1), TEXT_RVA, raw_size, FILE_ALIGN)
    headers = headers.ljust(FILE_ALIGN, b"\0")
    return headers + text.ljust(raw_size, b"\0")


# ── ECMA-335 metadata ─────────────────────────────────────────────────────


class Heap:
    def __init__(self, initial):
        self.data = bytearray(initial)

    def add(self, data):
        offset = len(self.data)
        self.data += data
        return offset


def compressed(n):
    if n < 0x80:
        return bytes([n])
    if n < 0x4000:
        return struct.pack(">H", n | 0x8000)
    return struct.pack(">I", n | 0xC0000000)


def ser_string(text):
    if text is None:
        return b"\xff"
    data = text.encode()
    return compressed(len(data)) + data


class Metadata:
    def __init__(self, wide_heaps=False):
        self.strings = Heap(b"\0")
        self.blobs = Heap(b"\0")
        self.guids = Heap(b"")
        self.wide = wide_heaps
        self.tables = {}

    def s(self, text):
        return self.strings.add(text.encode() + b"\0")

    def b(self, data):
        return self.blobs.add(compressed(len(data)) + data)

    def g(self):
        self.guids.add(bytes(range(16)))
        return len(self.guids.data) // 16

    def row(self, table, *cells):
        self.tables.setdefault(table, []).append(cells)

    def build(self, signature=0x424A5342):
        idx = "I" if self.wide else "H"
        heap_sizes = 0x07 if self.wide else 0
        valid = 0
        for table in self.tables:
            valid |= 1 << table
        tables = struct.pack("<IBBBBQQ", 0, 2, 0, heap_sizes, 1, valid, 0)
        for table in sorted(self.tables):
            tables += struct.pack("<I", len(self.tables[table]))
        for table in sorted(self.tables):
            for cells in self.tables[table]:
                for fmt, value in cells:
                    tables += struct.pack("<" + (idx if fmt in "sgb" else fmt), value)
        streams = [(b"#~", tables), (b"#Strings", bytes(self.strings.data)), (b"#GUID", bytes(self.guids.data)),
                   (b"#Blob", bytes(self.blobs.data))]
        streams = [(name, data.ljust((len(data) + 3) // 4 * 4, b"\0")) for name, data in streams]
        version = b"v4.0.30319".ljust(12, b"\0")
        header = struct.pack("<IHHII", signature, 1, 1, 0, len(version)) + version + struct.pack("<HH", 0, len(streams))
        names = [name + b"\0" for name, _ in streams]
        names = [name.ljust((len(name) + 3) // 4 * 4, b"\0") for name in names]
        header_size = len(header) + sum(8 + len(name) for name in names)
        offset = header_size
        for (name, data), padded in zip(streams, names):
            header += struct.pack("<II", offset, len(data)) + padded
            offset += len(data)
        return header + b"".join(data for _, data in streams)


MODULE, TYPEREF, MEMBERREF, CUSTOMATTRIBUTE, ASSEMBLY, ASSEMBLYREF = 0x00, 0x01, 0x0A, 0x0C, 0x20, 0x23


def dotnet_metadata(name, version, refs, tfm=False, tfm_value=".NETCoreApp,Version=v8.0", wide=False,
                    assembly=True):
    md = Metadata(wide)
    md.row(MODULE, ("H", 0), ("s", md.s(name + ".dll")), ("g", md.g()), ("g", 0), ("g", 0))
    for ref_name, ref_version in refs:
        md.row(ASSEMBLYREF, *[("H", v) for v in ref_version], ("I", 0), ("b", 0), ("s", md.s(ref_name)),
               ("s", 0), ("b", 0))
    if tfm:
        # ResolutionScope -> AssemblyRef 1 (tag 2, 2 bits)
        md.row(TYPEREF, ("H", (1 << 2) | 2), ("s", md.s("TargetFrameworkAttribute")),
               ("s", md.s("System.Runtime.Versioning")))
        # MemberRefParent -> TypeRef 1 (tag 1, 3 bits)
        md.row(MEMBERREF, ("H", (1 << 3) | 1), ("s", md.s(".ctor")), ("b", md.b(b"\x20\x01\x01\x0e")))
        value = b"\x01\x00" + ser_string(tfm_value) + b"\x00\x00"
        # Parent -> Assembly 1 (tag 14, 5 bits); Type -> MemberRef 1 (tag 3, 3 bits)
        md.row(CUSTOMATTRIBUTE, ("H", (1 << 5) | 14), ("H", (1 << 3) | 3), ("b", md.b(value)))
    if assembly:
        md.row(ASSEMBLY, ("I", 0x8004), *[("H", v) for v in version], ("I", 0), ("b", 0), ("s", md.s(name)),
               ("s", 0))
    return md


def dotnet_image(md, signature=0x424A5342, cor_size=72, **kwargs):
    metadata = md.build(signature)
    md_rva = TEXT_RVA + 0x48
    cor = struct.pack("<IHHIIII", 72, 2, 5, md_rva, len(metadata), 1, 0) + bytes(48)
    text = cor + metadata
    return image(text=text, directories=[(0, 0)] * 14 + [(TEXT_RVA, cor_size)], **kwargs)


def write(name, data):
    with open(os.path.join(OUT, name), "wb") as f:
        f.write(data)


def main():
    os.makedirs(OUT, exist_ok=True)

    # Native images: machine, bitness, subsystem, DLL flag, timestamp
    write("native-x86-gui.exe", image(machine=0x14C, pe32plus=False, subsystem=2, timestamp=1614834367))
    write("native-x64-console.dll", image(machine=0x8664, subsystem=3, timestamp=0, dll=True))
    write("native-arm64-efi.exe", image(machine=0xAA64, subsystem=10, timestamp=631152000))
    write("native-arm-bootapp.exe", image(machine=0x1C0, pe32plus=False, subsystem=16, timestamp=4133894399))
    write("native-ia64-native.exe", image(machine=0x200, subsystem=1, timestamp=946684800))
    write("native-riscv64-cegui.exe", image(machine=0x5064, subsystem=9, timestamp=4102444800))
    write("native-unknown-machine.exe", image(machine=0x1234, subsystem=99, timestamp=1700000000))
    write("native-efi-drivers.dll", image(machine=0x8664, subsystem=11, timestamp=-1, dll=True))
    write("native-efi-runtime.dll", image(machine=0x14C, pe32plus=False, subsystem=12, timestamp=0x80000000))
    write("native-zero-sections.exe", image(sections=0))

    # COFF object without a DOS header (PEReader treats it as COFF-only)
    write("coff-only.dll", coff(0x8664, 0, 1614834367, 0, 0x2000))

    # .NET assemblies
    refs = [("System.Runtime", (8, 0, 0, 0)), ("System.Console", (8, 0, 0, 0))]
    write("dotnet-lib.dll", dotnet_image(dotnet_metadata("Contoso.Lib", (1, 2, 3, 4), refs, tfm=True), dll=True))
    write("dotnet-app.exe", dotnet_image(dotnet_metadata("app", (0, 0, 0, 0), [])))
    write("dotnet-wide-heaps.dll",
          dotnet_image(dotnet_metadata("Wide", (2, 0, 1, 0), refs[:1], tfm=True, wide=True), dll=True))
    write("dotnet-null-tfm.dll",
          dotnet_image(dotnet_metadata("NullTfm", (1, 0, 0, 0), refs[:1], tfm=True, tfm_value=None), dll=True))
    write("dotnet-module.dll", dotnet_image(dotnet_metadata("mod", (1, 0, 0, 0), [], assembly=False), dll=True))
    write("dotnet-bad-signature.dll",
          dotnet_image(dotnet_metadata("Bad", (1, 0, 0, 0), []), signature=0x12345678, dll=True))
    write("dotnet-cor-too-small.dll", dotnet_image(dotnet_metadata("Small", (1, 0, 0, 0), []), cor_size=8, dll=True))
    write("dotnet-cor-unmapped.dll", image(directories=[(0, 0)] * 14 + [(0x9000, 72)], dll=True))

    # Malformed
    write("not-pe.exe", b"This is not a PE file")
    write("empty.exe", b"")
    write("mz-only.exe", b"MZ" + bytes(0x3E))
    write("lfanew-past-end.exe", dos_header(e_lfanew=0x1000))
    write("bad-pe-signature.exe", dos_header() + b"PX\0\0" + bytes(0x200))
    write("bad-magic.exe", dos_header() + b"PE\0\0" + coff(0x8664, 0, 0, 240, 0) + struct.pack("<H", 0x107)
          + bytes(0x200))
    write("truncated-optional.dll", image()[:0x80 + 4 + 20 + 100])
    write("sections-past-end.exe", image(sections=5)[:0x200])
    write("anonymous-object.exe", b"\0\0\xff\xff" + bytes(0x100))
    write("readme.txt", b"not an executable")


if __name__ == "__main__":
    main()
