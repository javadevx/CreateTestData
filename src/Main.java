import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.Random;

public class Main {

    public static int index = 1000;
    public static Random rnd = new Random();

    public static void main(String[] args)
    {
        try {

            String root = "D:\\Data\\";

            for (int i = 0; i < 5; i++) {


                Path path = Paths.get(root + "SubFolder " + (i+1) + "\\");
                if(!Files.exists(path)) {
                    Files.createDirectory(path);
                    log("creating " + path);
                }

                String sroot = String.valueOf(path);
                for (int j = 0; j < 5; j++) {
                    log("creating sub folder");

                    path = Paths.get(sroot + "\\SubFolder " + (i+1) + " Child " + (j+1));
                    if(!Files.exists(path)) {
                        Files.createDirectory(path);
                        log("creating " + path);
                        String filename = "file" + (index++) + ".txt";
                        String data = String.valueOf(rnd.nextInt(10));
                        createFile(path + "\\" + filename, data);
                    }
                }
            }
        }
        catch(Exception io){

        }
    }

    public static void createFile(String filename, String data) throws Exception{
        Path path = Paths.get(filename);
        Files.write(path, data.getBytes());
    }
    public static void log(String msg){
        System.out.println(msg);
    }
}
