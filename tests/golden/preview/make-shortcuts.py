#!/usr/bin/env python3
"""Writes the .lnk fixtures in tests/golden/preview/shortcuts.

Hand-built MS-SHLLINK files covering the fields ShortcutMetadataProvider
shows (target, launch URI, string data, hotkey, window, link info) and
malformed variants. Rerun only to change them, then delete
shortcuts.golden.txt and regenerate it with the C# test.
"""

import os
import struct

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "shortcuts")
LINK_CLSID = bytes.fromhex("0114020000000000c000000000000046")

HAS_ID_LIST = 0x01
HAS_LINK_INFO = 0x02
HAS_NAME = 0x04
HAS_RELATIVE_PATH = 0x08
HAS_WORKING_DIR = 0x10
HAS_ARGUMENTS = 0x20
HAS_ICON_LOCATION = 0x40
IS_UNICODE = 0x80


def header(flags, show=1, hotkey=0, size=0x4C, clsid=LINK_CLSID):
    return (struct.pack("<I", size) + clsid + struct.pack("<II", flags, 0x20)
            + struct.pack("<qqq", 133000000000000000, 0, 0)
            + struct.pack("<IiIHHII", 1234, 0, show, hotkey, 0, 0, 0))


def id_list(items):
    body = b"".join(struct.pack("<H", len(item) + 2) + item for item in items) + b"\0\0"
    return struct.pack("<H", len(body)) + body


def root_folder(guid_hex):
    return b"\x1f\x50" + bytes.fromhex(guid_hex)


def volume(letter):
    return b"\x2f" + f"{letter}:\\".encode() + bytes(19)


def file_entry(name, is_dir=False):
    return (b"\x31" + b"\0" + struct.pack("<IHHH", 0, 0x5821, 0x6000, 0x10 if is_dir else 0x20)
            + name.encode() + b"\0")


def volume_id(label, unicode_label=False):
    if unicode_label:
        data = label.encode("utf-16-le") + b"\0\0"
        return struct.pack("<IIII", 0x14 + len(data), 3, 0x1234ABCD, 0x14) + data
    data = label.encode() + b"\0"
    return struct.pack("<IIII", 0x10 + len(data), 3, 0x1234ABCD, 0x10) + data


def link_info(base=None, suffix="", volume=None, unicode_base=None, unicode_suffix=None, flags=1):
    unicode = unicode_base is not None or unicode_suffix is not None
    header_size = 0x24 if unicode else 0x1C
    parts = []
    offset = header_size

    def place(data):
        nonlocal offset
        if data is None:
            return 0
        start = offset
        parts.append(data)
        offset += len(data)
        return start

    volume_offset = place(volume)
    base_offset = place(None if base is None else base.encode() + b"\0")
    suffix_offset = place(suffix.encode() + b"\0")
    ubase_offset = place(None if unicode_base is None else unicode_base.encode("utf-16-le") + b"\0\0")
    usuffix_offset = place(None if unicode_suffix is None else unicode_suffix.encode("utf-16-le") + b"\0\0")

    fields = struct.pack("<IIIIIII", offset, header_size, flags, volume_offset, base_offset, 0, suffix_offset)
    if unicode:
        fields += struct.pack("<II", ubase_offset, usuffix_offset)
    return fields + b"".join(parts)


def string_data(values, unicode=True):
    out = b""
    for value in values:
        if unicode:
            out += struct.pack("<H", len(value)) + value.encode("utf-16-le")
        else:
            out += struct.pack("<H", len(value.encode())) + value.encode()
    return out


def extra_terminal():
    return b"\0\0\0\0"


def write(name, data):
    with open(os.path.join(OUT, name), "wb") as f:
        f.write(data)


def main():
    os.makedirs(OUT, exist_ok=True)
    my_computer = "e04fd020ea3a6910a2d808002b30309d"

    # A typical local file shortcut with every string-data field
    flags = (HAS_ID_LIST | HAS_LINK_INFO | HAS_NAME | HAS_RELATIVE_PATH | HAS_WORKING_DIR | HAS_ARGUMENTS
             | HAS_ICON_LOCATION | IS_UNICODE)
    write("notepad.lnk", header(flags, show=3, hotkey=0x064E)
          + id_list([root_folder(my_computer), volume("C"), file_entry("Windows", True), file_entry("notepad.exe")])
          + link_info(base="C:\\Windows\\notepad.exe", volume=volume_id("OS"))
          + string_data(["Edit text files", "..\\..\\Windows\\notepad.exe", "C:\\Users\\me",
                         "/A \"notes.txt\"", "%SystemRoot%\\system32\\notepad.exe"])
          + extra_terminal())

    # Unicode LinkInfo paths and a Unicode volume label
    write("unicode-linkinfo.lnk", header(HAS_LINK_INFO | IS_UNICODE)
          + link_info(base="C:\\Users\\", suffix="", volume=volume_id("Données", unicode_label=True),
                      unicode_base="C:\\Users\\", unicode_suffix="José\\Café.txt")
          + extra_terminal())

    # Network share: no local base path, only the common path suffix
    write("network.lnk", header(HAS_LINK_INFO | HAS_WORKING_DIR | IS_UNICODE, show=7)
          + link_info(base=None, suffix="share\\docs\\report.docx", flags=2)
          + string_data(["\\\\server\\share\\docs"])
          + extra_terminal())

    # Store/Xbox app: a launch URI inside the ID list, no link info
    uri = "msgamelaunch://shortcutLaunch/?ProductId=9NBLGGH4R315".encode("utf-16-le") + b"\0\0"
    write("store-app.lnk", header(HAS_ID_LIST | HAS_NAME | IS_UNICODE)
          + id_list([root_folder("0000000000000000c000000000000046"), b"\x00\x00" + bytes(6) + uri])
          + string_data(["Game"])
          + extra_terminal())

    # ANSI string data with only a relative path; odd hotkey and window
    write("relative-ansi.lnk", header(HAS_RELATIVE_PATH | HAS_ARGUMENTS, show=2, hotkey=0x00FF)
          + string_data([".\\tool.exe", "--verbose"], unicode=False)
          + extra_terminal())

    # Hotkey with function key and all modifiers; empty string data fields
    write("hotkey-f12.lnk", header(HAS_NAME | HAS_WORKING_DIR | IS_UNICODE, hotkey=0x077B)
          + string_data(["", ""])
          + extra_terminal())

    # Extra data blocks after string data (ignored by the provider)
    env = struct.pack("<II", 0x314, 0xA0000001) + bytes(0x314 - 8)
    write("extra-data.lnk", header(HAS_RELATIVE_PATH | IS_UNICODE)
          + string_data(["a.txt"]) + env + struct.pack("<II", 0x20, 0xDEADBEEF) + bytes(0x18)
          + struct.pack("<I", 0x9999) + extra_terminal())

    # Header only: no fields set at all
    write("header-only.lnk", header(0))

    # Malformed
    write("bad-header-size.lnk", header(HAS_NAME, size=0x50))
    write("bad-clsid.lnk", header(HAS_NAME, clsid=bytes(16)))
    write("truncated-header.lnk", header(HAS_NAME)[:40])
    write("empty.lnk", b"")
    write("truncated-strings.lnk", header(HAS_NAME | HAS_WORKING_DIR | IS_UNICODE) + string_data(["abc"])[:4])
    write("bad-item-size.lnk", header(HAS_ID_LIST) + struct.pack("<HH", 6, 1) + bytes(4))
    write("linkinfo-past-end.lnk", header(HAS_LINK_INFO)
          + struct.pack("<IIIIIII", 0x1C, 0x1C, 1, 0, 0x400, 0, 0))
    write("not-a-shortcut.txt", b"plain text")


if __name__ == "__main__":
    main()
