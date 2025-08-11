using System;
using System.Globalization;
using System.IO;

public class SineWaveGenerator
{
    public static void Main(string[] args)
    {
        int totalSamples = args.Length > 0 ? int.Parse(args[0], CultureInfo.InvariantCulture) : 240;
        int amplitude = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 100;
        int samplesPerCycle = args.Length > 2 ? int.Parse(args[2], CultureInfo.InvariantCulture) : 24;
        int offset = args.Length > 3 ? int.Parse(args[3], CultureInfo.InvariantCulture) : 1000;
        double noisePercent = args.Length > 4 ? double.Parse(args[4], CultureInfo.InvariantCulture) : 0.15;
        long seedValue;
        long? seed = (args.Length > 5 && long.TryParse(args[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out seedValue))
            ? seedValue
            : (long?)null;
        string outputPath = args.Length > 6 ? args[6] : "sine_values_cs.txt";

        // Random in C# accepts an int seed. Mix the long into an int if provided.
        Random rng = seed.HasValue
            ? new Random(unchecked((int)(seed.Value ^ (seed.Value >> 32))))
            : new Random();

        double noiseAmp = noisePercent * amplitude;

        using (var writer = new StreamWriter(outputPath))
        {
            writer.WriteLine("[");
            for (int t = 0; t < totalSamples; t++)
            {
                double value = offset
                               + amplitude * Math.Sin(2.0 * Math.PI * t / samplesPerCycle)
                               + (rng.NextDouble() * 2.0 - 1.0) * noiseAmp; // ±noise
                int rounded = (int)Math.Round(value);
                if (t < totalSamples - 1)
                {
                    writer.WriteLine("  " + rounded + ",");
                }
                else
                {
                    writer.WriteLine("  " + rounded);
                }
            }
            writer.WriteLine("]");
        }

        Console.WriteLine($"Wrote {totalSamples} values to {Path.GetFullPath(outputPath)}");
    }
}