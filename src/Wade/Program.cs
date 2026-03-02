using Wade;
using Wade.FileSystem;

var config = WadeConfig.Load(args);

if (config.ShowConfig)
{
    Console.WriteLine(config.ToJson());
    return;
}

FilePreview.Initialize();
new App(config).Run();
