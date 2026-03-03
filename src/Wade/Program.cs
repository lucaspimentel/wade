using Wade;

var config = WadeConfig.Load(args);

if (config.ShowConfig)
{
    Console.WriteLine(config.ToJson());
    return;
}

new App(config).Run();
