import java.util.Random;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.nio.charset.StandardCharsets;

public class SineWaveGenerator {
    public static void main(String[] args) throws Exception {
        int totalSamples = args.length > 0 ? Integer.parseInt(args[0]) : 240;
        int amplitude = args.length > 1 ? Integer.parseInt(args[1]) : 100;
        int samplesPerCycle = args.length > 2 ? Integer.parseInt(args[2]) : 24;
        int offset = args.length > 3 ? Integer.parseInt(args[3]) : 1000;
        double noisePercent = args.length > 4 ? Double.parseDouble(args[4]) : 0.15;
        Long seed = null;
        if (args.length > 5 && !args[5].isEmpty()) {
            try {
                seed = Long.parseLong(args[5]);
            } catch (NumberFormatException ignored) {
                seed = null;
            }
        }
        String outputPath = args.length > 6 ? args[6] : "sine_values.txt";

        Random random = (seed == null) ? new Random() : new Random(seed);
        double noiseAmp = noisePercent * amplitude;

        StringBuilder sb = new StringBuilder();
        sb.append("[\n");
        for (int t = 0; t < totalSamples; t++) {
            double value = offset
                    + amplitude * Math.sin(2.0 * Math.PI * t / samplesPerCycle)
                    + (random.nextDouble() * 2.0 - 1.0) * noiseAmp; // ±noise
            int rounded = (int) Math.round(value);
            sb.append("  ").append(rounded);
            if (t < totalSamples - 1) sb.append(",\n");
        }
        sb.append("\n]\n");

        Path out = Paths.get(outputPath);
        Files.write(out, sb.toString().getBytes(StandardCharsets.UTF_8));
        System.out.println("Wrote " + totalSamples + " values to " + out.toAbsolutePath());
    }
}