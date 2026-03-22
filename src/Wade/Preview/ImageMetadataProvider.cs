using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using Wade.Imaging;

namespace Wade.Preview;

internal sealed class ImageMetadataProvider : IMetadataProvider
{
    public string Label => "Image";

    public bool CanProvideMetadata(string path, PreviewContext context) =>
        ImagePreview.IsImageFile(path);

    public MetadataResult? GetMetadata(string path, PreviewContext context, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return null;
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            ImageInfo info = Image.Identify(stream);

            ct.ThrowIfCancellationRequested();

            var sections = new List<MetadataSection>();

            // Image section
            var imageEntries = new List<MetadataEntry>
            {
                new("Resolution", $"{info.Width} \u00d7 {info.Height}"),
            };

            string? formatName = info.Metadata.DecodedImageFormat?.Name;

            if (formatName is not null)
            {
                imageEntries.Add(new MetadataEntry("Format", formatName.ToUpperInvariant()));
            }

            imageEntries.Add(new MetadataEntry("Color depth", $"{info.PixelType.BitsPerPixel} bpp"));

            if (info.FrameMetadataCollection.Count > 1)
            {
                imageEntries.Add(new MetadataEntry("Frames", info.FrameMetadataCollection.Count.ToString()));
            }

            sections.Add(new MetadataSection("Image", imageEntries.ToArray()));

            // EXIF section
            ExifProfile? exif = info.Metadata.ExifProfile;

            if (exif is not null && exif.Values.Count > 0)
            {
                var exifEntries = new List<MetadataEntry>();

                string? make = GetExifString(exif, ExifTag.Make);
                string? model = GetExifString(exif, ExifTag.Model);
                string? camera = FormatCamera(make, model);

                if (camera is not null)
                {
                    exifEntries.Add(new MetadataEntry("Camera", camera));
                }

                string? dateTaken = GetExifString(exif, ExifTag.DateTimeOriginal);

                if (dateTaken is not null)
                {
                    exifEntries.Add(new MetadataEntry("Date taken", dateTaken));
                }

                string? exposure = FormatExposure(exif);

                if (exposure is not null)
                {
                    exifEntries.Add(new MetadataEntry("Exposure", exposure));
                }

                if (exif.TryGetValue(ExifTag.FocalLength, out IExifValue<Rational>? focalLength))
                {
                    exifEntries.Add(new MetadataEntry("Focal length", $"{focalLength.Value.ToDouble():0.#} mm"));
                }

                string? gps = FormatGps(exif);

                if (gps is not null)
                {
                    exifEntries.Add(new MetadataEntry("GPS", gps));
                }

                string? software = GetExifString(exif, ExifTag.Software);

                if (software is not null)
                {
                    exifEntries.Add(new MetadataEntry("Software", software));
                }

                if (exifEntries.Count > 0)
                {
                    sections.Add(new MetadataSection("EXIF", exifEntries.ToArray()));
                }
            }

            string ext = Path.GetExtension(path).TrimStart('.').ToUpperInvariant();
            string fileTypeLabel = $"{ext} ({info.Width} \u00d7 {info.Height})";

            return new MetadataResult
            {
                Sections = sections.ToArray(),
                FileTypeLabel = fileTypeLabel,
            };
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnknownImageFormatException)
        {
            return null;
        }
        catch (InvalidImageContentException)
        {
            return null;
        }
    }

    private static string? GetExifString(ExifProfile exif, ExifTag<string> tag)
    {
        if (!exif.TryGetValue(tag, out IExifValue<string>? value))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(value.Value) ? null : value.Value.Trim();
    }

    internal static string? FormatCamera(string? make, string? model)
    {
        if (make is null && model is null)
        {
            return null;
        }

        if (make is null)
        {
            return model;
        }

        if (model is null)
        {
            return make;
        }

        // Avoid duplication when model already contains the make (e.g. "Canon" + "Canon EOS R5")
        return model.StartsWith(make, StringComparison.OrdinalIgnoreCase)
            ? model
            : $"{make} {model}";
    }

    internal static string? FormatExposure(ExifProfile exif)
    {
        var parts = new List<string>();

        if (exif.TryGetValue(ExifTag.ExposureTime, out IExifValue<Rational>? shutterSpeed))
        {
            Rational r = shutterSpeed.Value;

            if (r.Numerator > 0 && r.Denominator > 0)
            {
                double seconds = r.ToDouble();
                parts.Add(seconds >= 1 ? $"{seconds:0.#}s" : $"1/{(int)Math.Round(1.0 / seconds)}s");
            }
        }

        if (exif.TryGetValue(ExifTag.FNumber, out IExifValue<Rational>? aperture))
        {
            parts.Add($"f/{aperture.Value.ToDouble():0.#}");
        }

        if (exif.TryGetValue(ExifTag.ISOSpeedRatings, out IExifValue<ushort[]>? isoValues)
            && isoValues.Value is { Length: > 0 })
        {
            parts.Add($"ISO {isoValues.Value[0]}");
        }

        return parts.Count > 0 ? string.Join(", ", parts) : null;
    }

    internal static string? FormatGps(ExifProfile exif)
    {
        if (!exif.TryGetValue(ExifTag.GPSLatitude, out IExifValue<Rational[]>? latValue)
            || !exif.TryGetValue(ExifTag.GPSLongitude, out IExifValue<Rational[]>? lonValue))
        {
            return null;
        }

        if (latValue.Value is not { Length: 3 } lat || lonValue.Value is not { Length: 3 } lon)
        {
            return null;
        }

        double latitude = lat[0].ToDouble() + lat[1].ToDouble() / 60 + lat[2].ToDouble() / 3600;
        double longitude = lon[0].ToDouble() + lon[1].ToDouble() / 60 + lon[2].ToDouble() / 3600;

        exif.TryGetValue(ExifTag.GPSLatitudeRef, out IExifValue<string>? latRef);
        exif.TryGetValue(ExifTag.GPSLongitudeRef, out IExifValue<string>? lonRef);

        if (latRef?.Value == "S")
        {
            latitude = -latitude;
        }

        if (lonRef?.Value == "W")
        {
            longitude = -longitude;
        }

        return $"{latitude:F6}, {longitude:F6}";
    }
}
