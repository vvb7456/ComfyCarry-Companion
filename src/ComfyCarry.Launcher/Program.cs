using System.Diagnostics;

var appDir = Path.Combine(AppContext.BaseDirectory, "app");
var exePath = Path.Combine(appDir, "ComfyCarry.exe");

if (!File.Exists(exePath))
{
    Console.Error.WriteLine($"Application files not found at: {exePath}");
    return 1;
}

try
{
    var psi = new ProcessStartInfo
    {
        FileName = exePath,
        UseShellExecute = false,
        WorkingDirectory = appDir,
    };
    foreach (var arg in args)
        psi.ArgumentList.Add(arg);

    Process.Start(psi);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}
