package atelier.art;
import java.io.*;
import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;

/** Sample the six original Java 1.8 biped transforms without starting a game. */
public class SampleClassic {
    public static void main(String[] args) throws ReflectiveOperationException, IOException {
        Locale.setDefault(Locale.ROOT);
        Class<?> biped = Class.forName("ccl");
        Constructor<?> create = biped.getConstructor(float.class,float.class,int.class,int.class);
        Method pose = biped.getMethod("a",float.class,float.class,float.class,float.class,float.class,float.class,Class.forName("wv",false,biped.getClassLoader()));
        try (PrintWriter out = new PrintWriter(Files.newBufferedWriter(Paths.get(args[1])))) {
            for (String line : Files.readAllLines(Paths.get(args[0]))) {
                String[] f = line.split(" ");
                Object model = create.newInstance(0f,0f,64,64);
                biped.getField("m").setInt(model,Integer.parseInt(f[6])); // heldItemRight
                biped.getField("n").setBoolean(model,Boolean.parseBoolean(f[7])); // isSneak
                biped.getField("p").setFloat(model,Float.parseFloat(f[8])); // swingProgress
                biped.getField("o").setBoolean(model,Boolean.parseBoolean(f[9])); // aimedBow
                pose.invoke(model,Float.parseFloat(f[1]), Float.parseFloat(f[2]), Float.parseFloat(f[3]),
                        Float.parseFloat(f[4]), Float.parseFloat(f[5]), 1f/16f, null);
                out.print(f[0]);
                for (String limb : new String[]{"e","g","h","i","j","k"}) {
                    Object part = biped.getField(limb).get(model);
                    for (String field : new String[]{"c","d","e","f","g","h"})
                        out.printf(" %.7f",part.getClass().getField(field).getFloat(part));
                }
                out.println();
            }
        }
    }
}
