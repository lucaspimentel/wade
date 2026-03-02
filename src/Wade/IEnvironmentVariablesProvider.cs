namespace Wade;

internal interface IEnvironmentVariablesProvider
{
    string? GetEnvironmentVariable(string name);
}

internal sealed class SystemEnvironmentVariablesProvider : IEnvironmentVariablesProvider
{
    public string? GetEnvironmentVariable(string name) =>
        Environment.GetEnvironmentVariable(name);
}
