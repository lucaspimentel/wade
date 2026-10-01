#!/usr/bin/env python3
"""Writes the fixtures in tests/golden/preview/documents and .../media.

documents/: Office (OPC docProps) and NuGet packages for
OfficeMetadataProvider and NuGetMetadataProvider, with encoding,
compression and structure variants. media/: ffprobe and mediainfo JSON
for MediaMetadataProvider's parsers (the tools themselves are not run).
Rerun only to change them, then delete documents.golden.txt and
media.golden.txt and regenerate them with the C# test.
"""

import os
import zipfile

ROOT = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.join(ROOT, "documents")
MEDIA = os.path.join(ROOT, "media")
FIXED_TIME = (2024, 1, 2, 3, 4, 6)

CORE_HEAD = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
             '<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" '
             'xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" '
             'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">')
APP_HEAD = ('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n'
            '<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" '
            'xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">')

LONG_DESCRIPTION = ("A quarterly report covering revenue, churn and the hiring plan for the platform team, "
                    "with appendices on infrastructure cost and an unusually long word: "
                    "supercalifragilisticexpialidocious-and-then-some-more-characters-to-force-a-hard-break.")


def core(body):
    return CORE_HEAD + body + "</cp:coreProperties>"


def app(body):
    return APP_HEAD + body + "</Properties>"


def parts(*names):
    items = "".join(f"<vt:lpstr>{name}</vt:lpstr>" for name in names)
    return f'<TitlesOfParts><vt:vector size="{len(names)}" baseType="lpstr">{items}</vt:vector></TitlesOfParts>'


def write_zip(directory, name, members, compression=zipfile.ZIP_DEFLATED):
    with zipfile.ZipFile(os.path.join(directory, name), "w", compression) as zf:
        for member in members:
            entry_name, data = member[0], member[1]
            info = zipfile.ZipInfo(entry_name, FIXED_TIME)
            info.compress_type = member[2] if len(member) > 2 else compression
            zf.writestr(info, data if isinstance(data, bytes) else data.encode())


def write(directory, name, data):
    with open(os.path.join(directory, name), "wb") as f:
        f.write(data if isinstance(data, bytes) else data.encode())


def office():
    full_core = core(
        "<dc:title>Q3 Report</dc:title><dc:creator>Ada Lovelace</dc:creator><dc:subject>Finance</dc:subject>"
        "<cp:keywords>revenue; churn</cp:keywords><cp:category>Reports</cp:category>"
        '<dcterms:created xsi:type="dcterms:W3CDTF">2024-01-15T10:30:00Z</dcterms:created>'
        '<dcterms:modified xsi:type="dcterms:W3CDTF">2024-02-20T08:05:09.123+05:30</dcterms:modified>'
        "<cp:lastModifiedBy>Grace Hopper</cp:lastModifiedBy><cp:revision>7</cp:revision>"
        f"<dc:description>  {LONG_DESCRIPTION}\n\n  second   paragraph  </dc:description>")
    full_app = app("<Pages>12</Pages><Words>12345</Words><Paragraphs>n/a</Paragraphs>"
                   "<Application>Microsoft Office Word</Application>" + parts("Intro", "Body"))
    write_zip(DOCS, "report.docx", [("[Content_Types].xml", "<Types/>"), ("docProps/core.xml", full_core),
                                     ("docProps/app.xml", full_app)])

    write_zip(DOCS, "budget.xlsx", [
        ("docProps/core.xml", core("<dc:title>Budget</dc:title><dcterms:created>not a date</dcterms:created>")),
        ("docProps/app.xml", app("<Application>Microsoft Excel</Application>" + parts("Summary", " ", "Q1", "Q2"))),
    ])

    write_zip(DOCS, "deck.pptx", [
        ("docProps/app.xml", app("<Slides>42</Slides><HiddenSlides>3</HiddenSlides><Words>999</Words>"
                                 + parts("Title slide"))),
    ])

    write_zip(DOCS, "template.dotx", [("docProps/core.xml", core("<dc:description>Only a description.</dc:description>"))])

    write_zip(DOCS, "whitespace.potx", [
        ("docProps/core.xml", core("<dc:title>   </dc:title><cp:revision>\n</cp:revision>")),
        ("docProps/app.xml", app("<Company>Contoso</Company>")),
    ])

    write_zip(DOCS, "no-docprops.xltx", [("[Content_Types].xml", "<Types/>")])

    utf16 = core("<dc:title>Données café</dc:title><dc:creator>José</dc:creator>").replace('encoding="UTF-8"',
                                                                                         'encoding="UTF-16"')
    write_zip(DOCS, "utf16-core.docx", [("docProps/core.xml", b"\xff\xfe" + utf16.encode("utf-16-le"))])

    write_zip(DOCS, "bom-core.docx", [("docProps/core.xml", b"\xef\xbb\xbf" + core(
        "<dc:title>BOM</dc:title><dcterms:modified>2023-12-31</dcterms:modified>"
        "<dcterms:created>2023-12-31T23:59</dcterms:created>").encode())])

    write_zip(DOCS, "stored.docx", [("docProps/core.xml", core("<dc:title>Stored</dc:title>"))],
              compression=zipfile.ZIP_STORED)

    write_zip(DOCS, "bzip2-entry.docx", [("docProps/core.xml", core("<dc:title>BZ</dc:title>"), zipfile.ZIP_BZIP2)])

    write_zip(DOCS, "namespaces.docx", [
        ("docProps/core.xml",
         '<?xml version="1.0"?><p:coreProperties '
         'xmlns:p="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" '
         'xmlns:d="http://purl.org/dc/elements/1.1/"><title>no namespace</title>'
         "<d:title>Prefixed <![CDATA[and CDATA]]> <b>nested</b></d:title><p:revision>+0012</p:revision>"
         "<d:creator>first</d:creator><d:creator>second</d:creator></p:coreProperties>"),
        ("docProps/app.xml", app("<Pages>1234567</Pages><Words> 42 </Words><Paragraphs>99999999999</Paragraphs>")),
    ])

    write(DOCS, "not-a-zip.docx", "plain text, not a package")


def nuspec(metadata, ns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"):
    xmlns = f' xmlns="{ns}"' if ns else ""
    return f'<?xml version="1.0" encoding="utf-8"?>\n<package{xmlns}><metadata>{metadata}</metadata></package>'


def nuget():
    metadata = (
        "<id>Contoso.Lib</id><version>1.2.3</version><authors>Contoso, Ada</authors>"
        '<license type="expression">MIT</license><licenseUrl>https://ignored.example</licenseUrl>'
        "<projectUrl>https://contoso.example/lib</projectUrl>"
        '<repository type="git" url="https://github.com/contoso/lib" commit="abc"/>'
        f"<tags>contoso lib utilities</tags><description>{LONG_DESCRIPTION}</description>"
        "<dependencies>"
        '<group targetFramework="net8.0"><dependency id="System.Text.Json" version="8.0.0"/>'
        '<dependency id="Contoso.Core"/><dependency version="1.0"/></group>'
        '<group targetFramework=".NETStandard2.0"></group>'
        '<group><dependency id="Any.Dep" version="[1.0,2.0)"/></group>'
        "</dependencies>")
    write_zip(DOCS, "Contoso.Lib.1.2.3.nupkg", [("_rels/.rels", "<Relationships/>"),
                                                 ("lib/net8.0/Contoso.Lib.dll", b"MZ"),
                                                 ("Contoso.Lib.nuspec", nuspec(metadata))])

    flat = ("<ID>Flat.Pkg</ID><VERSION>0.1.0-beta</VERSION><licenseUrl>https://licenses.example/mit</licenseUrl>"
            "<repository/><tags>   </tags>"
            '<dependencies><dependency id="A" version="1.0"/><dependency id="B"/></dependencies>')
    write_zip(DOCS, "flat-deps.nupkg", [("Flat.Pkg.NUSPEC", nuspec(flat, ns=None))])

    write_zip(DOCS, "nested-nuspec.nupkg", [("sub/Nested.nuspec", nuspec("<id>Nested</id>"))])

    write_zip(DOCS, "no-metadata.nupkg", [("NoMeta.nuspec", '<package xmlns="urn:x"><files/></package>')])

    write_zip(DOCS, "empty-license.snupkg", [("Sym.nuspec", nuspec(
        '<id>Sym</id><license type="file"></license><dependencies><group targetFramework="net6.0">'
        "</group></dependencies>"))])

    write_zip(DOCS, "first-nuspec-wins.nupkg", [("A.nuspec", nuspec("<id>First</id>")),
                                                ("B.nuspec", nuspec("<id>Second</id>"))])

    write(DOCS, "not-a-zip.nupkg", "nope")


def media():
    files = {
        "ffprobe-video.json": """{
  "streams": [
    {"index": 0, "codec_type": "video", "codec_long_name": "H.264 / AVC / MPEG-4 AVC / MPEG-4 part 10",
     "width": 1920, "height": 1080, "r_frame_rate": "30000/1001", "bit_rate": "4983720"},
    {"index": 1, "codec_type": "audio", "codec_long_name": "AAC (Advanced Audio Coding)", "channels": 2,
     "channel_layout": "stereo", "sample_rate": "48000", "bit_rate": "128000"},
    {"index": 2, "codec_type": "subtitle", "codec_long_name": "MOV text"}
  ],
  "format": {"format_long_name": "QuickTime / MOV", "duration": "3725.480000", "size": "2345678901",
             "bit_rate": "5112233"}
}""",
        "ffprobe-audio-only.json": """{
  "streams": [
    {"codec_type": "audio", "codec_long_name": "FLAC (Free Lossless Audio Codec)", "channels": 6,
     "channel_layout": "", "sample_rate": "96000"},
    {"codec_type": "audio", "codec_long_name": "Opus", "channels": "3", "sample_rate": 44100, "bit_rate": 96000}
  ],
  "format": {"format_long_name": "FLAC", "duration": "222.5", "size": "8912345"}
}""",
        "ffprobe-edge-values.json": """{
  "streams": [
    {"codec_type": "video", "codec_long_name": "   ", "width": 1.0e3, "height": "720", "r_frame_rate": "25",
     "bit_rate": "999"},
    {"codec_type": "video", "width": 640, "r_frame_rate": "0/0", "bit_rate": "-5"},
    {"codec_type": "video", "r_frame_rate": "24000/1001x"},
    {"codec_type": "audio", "channels": 8, "channel_layout": "7.1(wide)", "sample_rate": "0"},
    {"codec_type": "audio", "channels": 5}
  ],
  "format": {"duration": "N/A", "size": "-1", "bit_rate": "1,500,000", "format_long_name": null}
}""",
        "ffprobe-empty.json": '{"streams": [], "format": {}}',
        "ffprobe-format-not-object.json": '{"format": 5}',
        "ffprobe-stream-not-object.json": '{"streams": [1, 2]}',
        "ffprobe-invalid.json": '{"streams": [',
        "ffprobe-root-array.json": "[]",
        "mediainfo-video.json": """{
  "media": {"@ref": "movie.mkv", "track": [
    {"@type": "General", "Format": "Matroska", "Duration": "5025.042", "FileSize": "734003200",
     "OverallBitRate": "1168541"},
    {"@type": "Video", "Format": "HEVC", "Width": "3840", "Height": "2160", "FrameRate": "23.976",
     "BitRate": "1000000"},
    {"@type": "Audio", "Format": "E-AC-3", "Channels": "6", "SamplingRate": "48000", "BitRate": "640000"},
    {"@type": "Text", "Format": "UTF-8"}
  ]}
}""",
        "mediainfo-audio.json": """{
  "media": {"track": [
    {"@type": "General", "Format": "MPEG Audio", "Duration": "65", "FileSize": "1024"},
    {"@type": "Audio", "Format": "MPEG Audio", "Channels": "1", "SamplingRate": "44100", "BitRate": 320000}
  ]}
}""",
        "mediainfo-no-media.json": "{}",
        "mediainfo-track-not-array.json": '{"media": {"track": {"@type": "General"}}}',
        "mediainfo-media-not-object.json": '{"media": "x"}',
        "mediainfo-empty-tracks.json": '{"media": {"track": [{"@type": "Other"}]}}',
    }
    for name, text in files.items():
        write(MEDIA, name, text)


def main():
    os.makedirs(DOCS, exist_ok=True)
    os.makedirs(MEDIA, exist_ok=True)
    office()
    nuget()
    media()


if __name__ == "__main__":
    main()
