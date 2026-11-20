package com.xiaojiang269.novalauncher;

import android.content.Context;

import java.io.BufferedReader;
import java.io.File;
import java.io.InputStreamReader;
import java.nio.charset.Charset;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * 最小启动闭环：装好文件 → 拼 classpath 与参数 → 起 java 进程 → 把 stdout/stderr 回传。
 * 现阶段的验证目标是「能跑到 MC 主类并打印日志」，图形栈由 GL4ES + LWJGL3 接管后再接入。
 */
public final class GameLauncher {

    public interface Log { void line(String s); }

    private static Process proc;
    private static final AtomicBoolean running = new AtomicBoolean(false);
    private static Thread reader;

    public static boolean isRunning() { return running.get(); }

    public static void stop() {
        if (proc != null) {
            try { proc.destroy(); } catch (Throwable ignored) {}
            proc = null;
        }
        running.set(false);
    }

    /** 启动指定版本（离线账号），返回是否成功拉起进程。 */
    public static boolean launch(final Context c, final String ver, final String user, final Log log) {
        if (running.get()) { log.line("已有游戏进程在运行"); return false; }
        try {
            File java = RuntimeBoot.ensure(c, new RuntimeBoot.Log() {
                public void line(String s) { log.line("[java] " + s); }
            });
            if (java == null) { log.line("Java 运行时不可用，无法启动"); return false; }

            VersionInstaller.Installed ins = VersionInstaller.install(c, ver, new VersionInstaller.Log() {
                public void line(String s) { log.line(s); }
            });

            // classpath：client.jar + 全部库
            StringBuilder cp = new StringBuilder(ins.clientJar.getAbsolutePath());
            for (File f : ins.classpath) cp.append(':').append(f.getAbsolutePath());

            String offlineUuid = UUID.nameUUIDFromBytes(("OfflinePlayer:" + user).getBytes("UTF-8")).toString();
            File root = java.getParentFile().getParentFile();
            String libPath = c.getApplicationInfo().nativeLibraryDir + ":"
                    + new File(root, "lib").getAbsolutePath() + ":" + ins.nativesDir.getAbsolutePath();

            List<String> cmd = new ArrayList<String>();
            cmd.add(java.getAbsolutePath());
            cmd.add("-Xmx2G");
            cmd.add("-XX:+UseG1GC");
            cmd.add("-Djava.library.path=" + ins.nativesDir.getAbsolutePath());
            cmd.add("-Dorg.lwjgl.librarypath=" + ins.nativesDir.getAbsolutePath());
            cmd.add("-Dfml.ignoreInvalidMinecraftCertificates=true");
            cmd.add("-Dlog4j2.configureFile=" + new File(ins.gameDir, "log4j2.xml").getAbsolutePath());

            // 1.13+ 用 log4j2 xml 时需要显式指定（早期版本忽略即可）
            cmd.add("-Dlog4j.configurationFile=" + new File(ins.gameDir, "log4j2.xml").getAbsolutePath());
            cmd.add("-cp");
            cmd.add(cp.toString());
            cmd.add(ins.mainClass);
            cmd.add("--username"); cmd.add(user);
            cmd.add("--version"); cmd.add(ins.versionId);
            cmd.add("--gameDir"); cmd.add(ins.gameDir.getAbsolutePath());
            cmd.add("--assetsDir"); cmd.add(ins.assetsDir.getAbsolutePath());
            cmd.add("--assetIndex"); cmd.add(ins.assetIndexId);
            cmd.add("--uuid"); cmd.add(offlineUuid);
            cmd.add("--accessToken"); cmd.add("0");
            cmd.add("--userType"); cmd.add("legacy");
            cmd.add("--versionType"); cmd.add("release");

            log.line("启动命令：" + ins.mainClass + " 版本 " + ver + " 用户 " + user);

            ProcessBuilder pb = new ProcessBuilder(cmd);
            pb.directory(ins.gameDir);
            pb.redirectErrorStream(true);
            pb.environment().put("LD_LIBRARY_PATH", libPath);
            pb.environment().put("LIBGL_ES", "3");
            pb.environment().put("LIBGL_GL", "21");
            pb.environment().put("MESA_GL_VERSION_OVERRIDE", "2.1");
            pb.environment().put("HOME", ins.gameDir.getAbsolutePath());
            proc = pb.start();
            running.set(true);

            reader = new Thread(new Runnable() {
                public void run() {
                    try {
                        BufferedReader br = new BufferedReader(
                                new InputStreamReader(proc.getInputStream(), Charset.forName("UTF-8")));
                        String line;
                        while ((line = br.readLine()) != null) log.line("[mc] " + line);
                    } catch (Throwable t) {
                        log.line("[mc] 读取日志中断：" + t);
                    } finally {
                        running.set(false);
                        log.line("[mc] 进程结束");
                    }
                }
            }, "mc-log");
            reader.setDaemon(true);
            reader.start();
            return true;
        } catch (Throwable t) {
            running.set(false);
            log.line("启动失败：" + t);
            return false;
        }
    }
}
