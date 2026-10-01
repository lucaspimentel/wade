#!/usr/bin/env python3
"""Writes the archive fixtures in tests/golden/preview/archives.

The fixtures are committed; rerun only to change them (then delete
archives.golden.txt and regenerate it with the C# test). Timestamps are
fixed so the output is byte-stable.
"""

import gzip
import io
import os
import struct
import tarfile
import zipfile

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "archives")
DATE = (2024, 1, 2, 3, 4, 6)
MTIME = 1704164646

TEXT = "".join(f"line {i}: the quick brown fox jumps over the lazy dog\n" for i in range(40))


def path(name):
    return os.path.join(OUT, name)


def zinfo(name, compress=zipfile.ZIP_DEFLATED):
    info = zipfile.ZipInfo(name, DATE)
    info.compress_type = compress
    info.external_attr = 0o644 << 16
    return info


def write_zip(name, members, comment=b""):
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as zf:
        for member in members:
            entry, data = member[0], member[1]
            compress = member[2] if len(member) > 2 else zipfile.ZIP_DEFLATED
            zf.writestr(zinfo(entry, compress), data)
        zf.comment = comment
    data = buf.getvalue()
    with open(path(name), "wb") as f:
        f.write(data)
    return data


def make_zip64(name, members):
    """A regular zip whose EOCD defers to a Zip64 EOCD record + locator."""
    data = write_zip(name, members)
    eocd = data.rindex(b"PK\x05\x06")
    (_, disk, cd_disk, count_disk, count, cd_size, cd_offset, comment_len) = struct.unpack(
        "<IHHHHIIH", data[eocd:eocd + 22])
    zip64_eocd = struct.pack("<IQHHIIQQQQ", 0x06064B50, 44, 45, 45, 0, 0, count_disk, count, cd_size, cd_offset)
    locator = struct.pack("<IIQI", 0x07064B50, 0, eocd, 1)
    new_eocd = struct.pack("<IHHHHIIH", 0x06054B50, 0, 0, 0xFFFF, 0xFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0)
    with open(path(name), "wb") as f:
        f.write(data[:eocd] + zip64_eocd + locator + new_eocd)


def tinfo(name, size=0, kind=tarfile.REGTYPE, linkname=""):
    info = tarfile.TarInfo(name)
    info.size = size
    info.type = kind
    info.mtime = MTIME
    info.mode = 0o755 if kind == tarfile.DIRTYPE else 0o644
    info.uid = info.gid = 1000
    info.uname = info.gname = "user"
    info.linkname = linkname
    return info


def tar_bytes(members, fmt=tarfile.USTAR_FORMAT, pax_headers=None):
    buf = io.BytesIO()
    with tarfile.open(fileobj=buf, mode="w", format=fmt, pax_headers=pax_headers or {}) as tf:
        for member in members:
            if member[1] == "dir":
                tf.addfile(tinfo(member[0], kind=tarfile.DIRTYPE))
            elif member[1] == "link":
                tf.addfile(tinfo(member[0], kind=tarfile.SYMTYPE, linkname=member[2]))
            else:
                data = member[1].encode() if isinstance(member[1], str) else member[1]
                tf.addfile(tinfo(member[0], len(data)), io.BytesIO(data))
    return buf.getvalue()


def gz(data, name=None):
    buf = io.BytesIO()
    with gzip.GzipFile(filename=name or "", mode="wb", fileobj=buf, mtime=MTIME) as f:
        f.write(data)
    return buf.getvalue()


def write(name, data):
    with open(path(name), "wb") as f:
        f.write(data)


def v7_header(name, size, typeflag):
    header = bytearray(512)
    header[0:len(name)] = name.encode()
    header[100:108] = b"0000644\0"
    header[108:116] = b"0001750\0"
    header[116:124] = b"0001750\0"
    header[124:136] = b"%011o\0" % size
    header[136:148] = b"%011o\0" % MTIME
    header[156:157] = typeflag
    header[148:156] = b" " * 8
    checksum = sum(header)
    header[148:156] = b"%06o\0 " % checksum
    return bytes(header)


def ustar_header(name, size=0, typeflag=b"0", prefix=b"", size_field=None, checksum=True, magic=b"ustar\x0000"):
    header = bytearray(512)
    raw = name if isinstance(name, bytes) else name.encode()
    header[0:len(raw)] = raw
    header[100:108] = b"0000644\0"
    header[108:116] = b"0001750\0"
    header[116:124] = b"0001750\0"
    header[124:136] = size_field if size_field is not None else b"%011o\0" % size
    header[136:148] = b"%011o\0" % MTIME
    header[156:157] = typeflag
    header[257:265] = magic
    header[345:345 + len(prefix)] = prefix
    header[148:156] = b" " * 8
    if checksum:
        header[148:156] = b"%06o\0 " % sum(header)
    return bytes(header)


def block(data):
    return data + bytes(-len(data) % 512)


def pax_record(key, value):
    body = f" {key}={value}\n".encode()
    length = len(body)
    while len(str(length).encode()) + len(body) != length:
        length = len(str(length).encode()) + len(body)
    return str(length).encode() + body


def edge_tars():
    end = bytes(1024)
    one = ustar_header("one.txt", 3) + block(b"one")
    write("edge-no-end.tar", one)
    write("edge-no-end.tgz", gz(one))
    write("edge-zero-bytes.tar", b"")
    write("edge-short.tar", b"x" * 100)
    write("edge-name-padding.tar", ustar_header(b"spaces  ", 1) + block(b"s")
          + ustar_header(b"nul\0garbage", 1) + block(b"n") + end)
    base256 = b"\x80" + (5).to_bytes(11, "big")
    write("edge-base256.tar", ustar_header("b256.txt", size_field=base256) + block(b"12345") + end)
    pax = pax_record("path", "renamed/by-pax.txt") + pax_record("size", "4")
    write("edge-pax-override.tar", ustar_header("PaxHeaders/x", len(pax), b"x") + block(pax)
          + ustar_header("original.txt", 0) + block(b"four") + ustar_header("next.txt", 1) + block(b"n") + end)
    longname = b"gnu/" + b"n" * 120 + b"\0"
    write("edge-gnu-L.tar", ustar_header("././@LongLink", len(longname), b"L", magic=b"ustar  \0")
          + block(longname) + ustar_header("truncated-name", 2, magic=b"ustar  \0") + block(b"ok") + end)
    write("edge-prefix.tar", ustar_header("leaf.txt", 1, prefix=b"some/prefix") + block(b"l") + end)
    write("edge-gnu-prefix.tar", ustar_header("leaf.txt", 1, prefix=b"not/a/prefix", magic=b"ustar  \0")
          + block(b"l") + end)
    write("edge-space-checksum.tar", ustar_header("a.txt", 1, checksum=False) + block(b"a") + end)
    write("edge-bad-size.tar", ustar_header("a.txt", size_field=b"12x45678901\0") + block(b"a") + end)
    write("edge-types.tar", ustar_header("hard", typeflag=b"1") + ustar_header("chr", typeflag=b"3")
          + ustar_header("blk", typeflag=b"4") + ustar_header("fifo", typeflag=b"6")
          + ustar_header("contig", 1, typeflag=b"7") + block(b"c") + ustar_header("dir", typeflag=b"5") + end)
    write("edge-one-zero-block.tar", one + bytes(512))
    write("edge-unpadded.tar", ustar_header("one.txt", 3) + b"one")
    write("edge-data-short.tar", ustar_header("big.txt", 1000) + b"0123456789")
    write("edge-data-after-end.tar", one + end + ustar_header("after.txt", 1) + block(b"z"))


def main():
    os.makedirs(OUT, exist_ok=True)

    # Zip
    write_zip("stored.zip", [
        ("docs/", b""),
        ("docs/readme.txt", b"hello\n", zipfile.ZIP_STORED),
        ("empty.txt", b"", zipfile.ZIP_STORED),
        ("data.bin", bytes(range(256)) * 4, zipfile.ZIP_STORED),
    ])
    write_zip("deflated.zip", [
        ("src/", b""),
        ("src/Main.cs", TEXT.encode()),
        ("src/b.txt", b"b" * 5000),
        ("README.md", TEXT[:300].encode()),
        ("café/naïve 日本.txt", "unicode name".encode()),
        ("Zeta.txt", b"z"),
        ("alpha.txt", b"a" * 3),
        ("random.bin", os.urandom(0) + bytes((i * 7919) % 251 for i in range(3000)), zipfile.ZIP_DEFLATED),
    ])
    write_zip("many.zip", [(f"{'File' if i % 2 else 'file'}{i:03}.txt", b"x" * i) for i in range(120)])
    write_zip("empty.zip", [])
    write_zip("dirs-only.zip", [("a/", b""), ("a/b/", b"")])
    write_zip("comment.zip", [("note.txt", b"with an archive comment")], comment=b"PK\x05\x06 decoy in the comment")
    make_zip64("zip64.zip", [("big-ish.txt", TEXT.encode()), ("small.txt", b"s")])
    write_zip("secondary.docx", [("[Content_Types].xml", b"<Types/>"), ("word/document.xml", b"<w:document/>")])
    write_zip("package.nupkg", [("package.nuspec", b"<package/>"), ("lib/net8.0/a.dll", b"MZ" + bytes(100))])
    write("corrupt.zip", b"PK\x03\x04 this is not really a zip archive at all")
    truncated = write_zip("truncated.zip", [("a.txt", TEXT.encode()), ("b.txt", b"b")])
    write("truncated.zip", truncated[:-30])
    # Central-directory filename bytes that are not UTF-8 (no UTF-8 flag)
    raw = bytearray(write_zip("cp437-name.zip", [("naïve.txt", b"x")]))
    for marker in (b"na\xc3\xafve.txt",):
        while marker in raw:
            i = raw.index(marker)
            raw[i:i + len(marker)] = b"na\x8bve.txt\x20"
    write("cp437-name.zip", bytes(raw))

    # Tar
    plain = tar_bytes([
        ("project", "dir"),
        ("project/src", "dir"),
        ("project/src/main.py", "print('hi')\n"),
        ("project/README", TEXT),
        ("project/link", "link", "README"),
        ("project/Big.bin", bytes(70000)),
        ("project/empty", ""),
    ])
    write("plain.tar", plain)
    write("plain.tar.gz", gz(plain, "plain.tar"))
    write("plain.tgz", gz(plain))
    write("tarball.gz", gz(plain))
    long_name = "deep/" + "/".join(f"segment{i:02}" for i in range(14)) + "/file.txt"
    write("gnu-longname.tar", tar_bytes([(long_name, "gnu"), ("short.txt", "s")], fmt=tarfile.GNU_FORMAT))
    write("ustar-prefix.tar", tar_bytes([(long_name, "ustar")], fmt=tarfile.USTAR_FORMAT))
    write("pax.tar", tar_bytes(
        [(long_name, "pax"), ("café 日本.txt", "unicode"), ("plain.txt", "p")],
        fmt=tarfile.PAX_FORMAT, pax_headers={"comment": "global header"}))
    write("many.tar", tar_bytes([(f"{'Entry' if i % 3 else 'entry'}{i:03}", "y" * i) for i in range(120)]))
    write("empty.tar", bytes(1024))
    write("truncated.tar", plain[:2000])
    bad = bytearray(tar_bytes([("a.txt", "a")]))
    bad[0] = ord("b")
    write("bad-checksum.tar", bytes(bad))
    write("v7.tar", v7_header("old/", 0, b"\0") + v7_header("old/file.txt", 5, b"\0") + b"hello".ljust(512, b"\0")
          + bytes(1024))
    write("truncated.tgz", gz(plain)[:-200])

    edge_tars()

    # Plain gzip
    write("script.py.gz", gz(b"import os\n\ndef main():\n    print(os.getcwd())  # cwd\n", "script.py"))
    write("notes.txt.gz", gz(TEXT.encode()))
    write("crlf.txt.gz", gz(b"one\r\ntwo\rthree\n"))
    write("binary.bin.gz", gz(b"\x00\x01\x02" * 100))
    write("empty.gz", gz(b""))
    write("multi.gz", gz(b"first member\n") + gz(b"second member\n"))
    write("truncated-text.txt.gz", gz(TEXT.encode())[:-40])
    badcrc = bytearray(gz(b"crc mismatch\n"))
    badcrc[-8] ^= 0xFF
    write("bad-crc.txt.gz", bytes(badcrc))
    write("trailing-garbage.txt.gz", gz(b"member\n") + b"garbage after the member")
    write("notgzip.gz", b"plain text, not gzip\n")
    write("invalid-utf8.txt.gz", gz(b"ok \xff\xfe bad\n\xe6\x97\xa5"))


if __name__ == "__main__":
    main()
