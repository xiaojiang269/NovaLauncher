package com.xiaojiang269.novalauncher;

import android.content.Context;
import android.util.Log;
import android.view.Surface;

import net.kdt.pojavlaunch.utils.JREUtils;

import com.oracle.dalvik.VMLauncher;

import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;

/**
 * 同进程启动 Minecraft：这是能真正出画面的唯一路径。
 *
 * 为什么必须同进程：Android 的 Surface 是 binder 对象，跨不了进程边界。
 * 子进程 java 永远拿不到 ANativeWindow，glfwInit 必然失败。
 * 这里改为 dlopen libjvm + JNI_CreateJavaVM，在 App 进程里起 JVM，
 * 再把 SurfaceView 的 Surface 交给 libpojavexec 的 GLFW/EGL 后端。
 */
public final class GameLaunchInline {

    public interface Log { void line(String s); }

    private static volatile boolean running;
    private static volatile int lastExit = -1;

    public static boolean isRunning() { return running; }
    public static int lastExitCode() { return lastExit; }

    /** 准备好文件、加载 JRE、绑定 Surface，然后进 JVM 主循环（阻塞直到游戏退出）。 */
    public static void launch(final Context c, final Surface surface, final String ver,
                              final String user, final int memMb, final Log log) {
        if (running) { log.line("已有 JVM 在运行"); return; }
        running = true;
        try {
            File jre = RuntimeBoot.ensureAny(c, new RuntimeBoot.Log() {
                public void line(String s) { log.line(s); }
            });
            if (jre == null) { log.line("JRE 准备失败，无法启动"); return; }

            VersionInstaller.Installed ins = VersionInstaller.install(c, ver, new VersionInstaller.Log() {
                public void line(String s) { log.line(s); }
            });
            log.line("文件就绪：" + ins.gameDir.getAbsolutePath());

            String cp = buildClasspath(c, ins, log);
            String nativeDir = c.getApplicationInfo().nativeLibraryDir;
            File jreRoot = jre.getParentFile().getParentFile();
            File jreLib = new File(jreRoot, "lib");

            // gl4es / LWJGL 需要的第一批环境变量（必须在起 JVM 前设好）
            putEnv("LIBGL_ES", "3");
            putEnv("LIBGL_GL", "21");
            putEnv("LIBGL_MIPMAP", "3");
            putEnv("LIBGL_NOBANNER", "1");
            putEnv("LIBGL_NOERROR", "1");
            putEnv("LIBGL_NOINTOVLHACK", "1");
            putEnv("LIBGL_NORMALIZE", "1");
            putEnv("POJAV_NATIVEDIR", nativeDir);
            putEnv("HOME", ins.gameDir.getAbsolutePath());
            putEnv("PATH", jreRoot.getAbsolutePath() + "/bin:" + System.getenv("PATH"));
            android.util.Log.i("NovaRT", "env ready, nativeDir=" + nativeDir);

            // 预加载 JRE 的关键 so（libjli → libjvm → libjava），否则 JNI_CreateJavaVM 找不到符号
            JREUtils.dlopen(new File(jreLib, "libjli.so").getAbsolutePath());
            JREUtils.dlopen(new File(jreLib, "libjvm.so").getAbsolutePath());
            JREUtils.dlopen(new File(jreLib, "libjava.so").getAbsolutePath());
            JREUtils.dlopen(new File(jreLib, "libnet.so").getAbsolutePath());
            JREUtils.dlopen(new File(jreLib, "libnio.so").getAbsolutePath());
            JREUtils.setLdLibraryPath(jreLib.getAbsolutePath() + ":" + nativeDir);
            android.util.Log.i("NovaRT", "jre dlopen ok");

            JREUtils.setupExitMethod(c.getApplicationContext());
            JREUtils.chdir(ins.gameDir.getAbsolutePath());

            // 窗口绑定：从这一刻起，JVM 里的 GLFW 就有 ANativeWindow 了
            JREUtils.setupBridgeWindow(surface);
            android.util.Log.i("NovaRT", "bridge window bound " + surface);

            List<String> args = buildArgs(ins, cp, user, memMb, nativeDir, jreRoot);
            log.line("启动 JVM：" + ins.mainClass);
            JREUtils.dlopen(new File(nativeDir, "libgl4es_114.so").getAbsolutePath());

            lastExit = VMLauncher.launchJVM(args.toArray(new String[0]));
            log.line("JVM 退出码 " + lastExit);
            android.util.Log.i("NovaRT", "JVM exit=" + lastExit);
            JREUtils.releaseBridgeWindow();
        } catch (Throwable t) {
            log.line("启动失败：" + t);
            android.util.Log.e("NovaRT", "inline launch failed", t);
        } finally {
            running = false;
        }
    }

    /** MC 的 lwjgl-glfw 必须换掉：用 Pojav 的 EGL 版本，否则会去找 GLFW 桌面后端。 */
    private static String buildClasspath(Context c, VersionInstaller.Installed ins, Log log) throws Exception {
        File lwjglDir = new File(c.getFilesDir(), "lwjgl");
        lwjglDir.mkdirs();
        File glfwJar = new File(lwjglDir, "lwjgl-glfw-classes.jar");
        if (!glfwJar.exists() || glfwJar.length() == 0) copyAsset(c, "components/lwjgl-glfw-classes.jar", glfwJar);

        StringBuilder sb = new StringBuilder(glfwJar.getAbsolutePath());
        sb.append(':').append(ins.clientJar.getAbsolutePath());
        int skipped = 0;
        for (File f : ins.classpath) {
            String n = f.getName().toLowerCase();
            if (n.contains("lwjgl-glfw")) { skipped++; continue; }   // 桌面 GLFW，必须剔除
            if (n.contains("natives-linux")) { skipped++; continue; }
            sb.append(':').append(f.getAbsolutePath());
        }
        log.line("classpath: " + (ins.classpath.size() - skipped) + " 个 jar（跳过 " + skipped + " 个桌面原生/GLFW 包）");
        return sb.toString();
    }

    private static List<String> buildArgs(VersionInstaller.Installed ins, String cp, String user,
                                          int memMb, String nativeDir, File jreRoot) throws Exception {
        String uuid = UUID.nameUUIDFromBytes(("OfflinePlayer:" + user).getBytes("UTF-8")).toString();
        List<String> a = new ArrayList<String>();
        a.add("java");
        a.add("-Xms" + Math.min(512, memMb) + "M");
        a.add("-Xmx" + memMb + "M");
        a.add("-XX:+UseG1GC");
        a.add("-Djava.class.path=" + cp);
        a.add("-Djava.library.path=" + nativeDir + ":" + ins.nativesDir.getAbsolutePath());
        a.add("-Dorg.lwjgl.librarypath=" + nativeDir);
        a.add("-Duser.home=" + ins.gameDir.getAbsolutePath());
        a.add("-Djava.io.tmpdir=" + new File(ins.gameDir, "tmp").getAbsolutePath());
        a.add("-Dlog4j2.configurationFile=" + new File(ins.gameDir, "log4j2.xml").getAbsolutePath());
        a.add("-Dfml.ignoreInvalidMinecraftCertificates=true");
        a.add("-Dmisode.debug=false");
        a.add("-XX:ActiveProcessorCount=" + Runtime.getRuntime().availableProcessors());
        a.add(ins.mainClass);
        a.add("--username"); a.add(user);
        a.add("--version"); a.add(ins.versionId);
        a.add("--gameDir"); a.add(ins.gameDir.getAbsolutePath());
        a.add("--assetsDir"); a.add(ins.assetsDir.getAbsolutePath());
        a.add("--assetIndex"); a.add(ins.assetIndexId);
        a.add("--uuid"); a.add(uuid);
        a.add("--accessToken"); a.add("0");
        a.add("--userType"); a.add("legacy");
        a.add("--versionType"); a.add("release");
        return a;
    }

    private static void copyAsset(Context c, String asset, File dst) throws Exception {
        InputStream in = c.getAssets().open(asset);
        OutputStream out = new FileOutputStream(dst);
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        in.close();
        out.close();
    }

    /**
     * 设置进程环境变量。Android 没有公开的 setenv，走 libcore 的 Posix 实现；
     * 拿不到就走 System.setProperty 的兼容分支（gl4es 读不到 env 时至少不崩）。
     */
    private static void putEnv(String k, String v) {
        try {
            Class<?> libcore = Class.forName("libcore.io.Libcore");
            java.lang.reflect.Field f = libcore.getDeclaredField("os");
            f.setAccessible(true);
            Object os = f.get(null);
            java.lang.reflect.Method m = os.getClass().getMethod("setenv", String.class, String.class, boolean.class);
            m.invoke(os, k, v, true);
        } catch (Throwable t) {
            android.util.Log.w("NovaRT", "putEnv " + k + " via libcore failed: " + t);
        }
    }
}
