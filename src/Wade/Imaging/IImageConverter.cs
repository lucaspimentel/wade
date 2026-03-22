namespace Wade.Imaging;

internal interface IImageConverter
{
    public bool CanConvert(string path);
    public string? Convert(string path, CancellationToken ct); // Returns temp PNG path or null
}
