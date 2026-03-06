using System.Diagnostics;
using Aco228.Common.LocalStorage;

namespace Aco228.ContextualImage.Infrastructure;

public static class RembgService
{
    /// <summary>
    /// pip install rembg[gpu]
    /// </summary>

    public static FileInfo Run(FileInfo inputFile)
    {
        var storageManager = StorageManager.Instance;
        var tempFolder = storageManager.GetTempFolder();

        string inputPath = inputFile.FullName;
        string outputPath = tempFolder.GetPathForFile(Guid.NewGuid() + ".png");

        // Use python directly, call rembg.cli
        string pythonExe = "python"; // Assumes python is in PATH

        using var process = new Process();
        process.StartInfo.FileName = pythonExe;

        // Call rembg via module import, no rembg.exe needed
        process.StartInfo.Arguments =
            $"-m rembg.cli i \"{inputPath}\" \"{outputPath}\" --verbose";

        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();

        string stdOut = process.StandardOutput.ReadToEnd();
        string stdErr = process.StandardError.ReadToEnd();

        process.WaitForExit();

        Console.WriteLine("STDOUT:");
        Console.WriteLine(stdOut);

        Console.WriteLine("STDERR:");
        Console.WriteLine(stdErr);

        if (process.ExitCode != 0)
            throw new Exception($"rembg failed with exit code {process.ExitCode}:\n{stdErr}");

        if (!File.Exists(outputPath))
            throw new Exception($"rembg did not produce output file.\nSTDERR:\n{stdErr}");

        return new FileInfo(outputPath);
    }

    private static string GetPythonPath()
    {
        using var process = new Process();

        process.StartInfo.FileName = "where";
        process.StartInfo.Arguments = "python";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        string result = process.StandardOutput.ReadLine();
        process.WaitForExit();

        if (string.IsNullOrWhiteSpace(result))
            throw new Exception("Python not found in PATH.");

        return result.Trim();
    }

    private static string GetRembgPath(string pythonPath)
    {
        using var process = new Process();

        process.StartInfo.FileName = pythonPath;
        process.StartInfo.Arguments =
            "-c \"import os,sys; print(os.path.join(os.path.dirname(sys.executable),'Scripts','rembg.exe'))\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.CreateNoWindow = true;

        process.Start();
        string path = process.StandardOutput.ReadLine();
        process.WaitForExit();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new Exception("rembg.exe not found in Python Scripts directory.");

        return path.Trim();
    }
}