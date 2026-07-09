using System.IO.Compression;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: bms-brotli-tool <input> <output>");
    return 2;
}

var inputPath = Path.GetFullPath(args[0]);
var outputPath = Path.GetFullPath(args[1]);
var outputDirectory = Path.GetDirectoryName(outputPath);

if (string.IsNullOrEmpty(outputDirectory))
{
    Console.Error.WriteLine($"Output path has no directory: {outputPath}");
    return 2;
}

Directory.CreateDirectory(outputDirectory);

using var source = File.OpenRead(inputPath);
using var destination = File.Create(outputPath);
using var brotli = new BrotliStream(destination, CompressionLevel.SmallestSize);
source.CopyTo(brotli);

Console.WriteLine($"Compressed BMS native backend: {inputPath} -> {outputPath}");
return 0;
