namespace Wade.Imaging;

internal interface IImageConverter
{
    bool CanConvert(string path);
    string? Convert(string path, CancellationToken ct); // Returns temp PNG path or null
}
