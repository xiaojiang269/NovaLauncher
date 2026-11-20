package com.xiaojiang269.novalauncher;

import android.content.Context;
import android.content.res.AssetManager;

import java.io.BufferedInputStream;
import java.io.BufferedReader;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.util.zip.GZIPInputStream;

/**
 * 运行时引导：把打包在 assets 里的 Android JRE 17（aarch64）解到应用私有目录，
 * 校验 bin/java，并跑一次 `java -version` 证明运行时真的能启动。
 *
 * 产物目录：/data/data/<pkg>/files/jre
 */
public final class RuntimeBoot {

    public static final String ASSET = "runtime/jre17.tar.gz";

    public static final String URL_JRE8 =
            "https://github.com/xiaojiang269/NovaLauncher/releases/download/runtime-jre8-arm64/jre8-arm64.tar.gz";
    public static final String URL_JRE17_ARM64 =
            "https://github.com/xiaojiang269/NovaLauncher/releases/download/v3.2.5/nova-jre17-arm64-android.tar.gz";
    public static final String URL_JRE17_X86_64 =
            "https://github.com/xiaojiang269/NovaLauncher/releases/download/v3.2.5/nova-jre17-x86_64-android.tar.gz";
    public static final String URL_JRE17 =
            "https://github.com/xiaojiang269/NovaLauncher/releases/download/runtime-jre8-arm64/jre17-arm64.tar.gz";

    /** 版本所需的 Java 主版本 → 运行时目录名。 */
    public static File dirFor(Context c, int major) {
        return new File(c.getFilesDir(), "jre" + major);
    }

    /**
     * 按 Java 主版本准备运行时：
     *  - 17：优先用安装包内已带的 JRE17（无需联网）
     *  - 8：从我们自己的 Release 下载
     *  - 21：当前没有可用的 arm64 构建，明确拒绝而不是凑数
     */
    public static File ensureFor(Context c, int major, Log log) {
        File root = dirFor(c, major);
        File java = new File(root, "bin/java");
        if (java.exists() && java.length() > 0) {
            log.line("Java " + major + " 已就绪");
            return java;
        }
        if (major == 21) {
            log.line("Java 21：暂无 arm64 构建，请先用 17 跑 ≤1.20.4 的版本");
            return null;
        }
        if (major == 17) {
            return ensure(c, log);
        }
        if (major == 8) {
            log.line("未安装 Java 8，开始自动下载（约 39 MB）");
            return downloadAndExtract(c, URL_JRE8, root, log);
        }
        log.line("未知 Java 版本需求：" + major);
        return null;
    }

    private static File downloadAndExtract(Context c, String url, File root, Log log) {
        File tmp = new File(c.getCacheDir(), "runtime-dl.tar.gz");
        try {
            HttpURLConnection conn = (HttpURLConnection) new java.net.URL(url).openConnection();
            conn.setInstanceFollowRedirects(true);
            conn.setConnectTimeout(20000);
            conn.setReadTimeout(60000);
            conn.setRequestProperty("User-Agent", "NovaLauncherApp/3.7.0");
            int code = conn.getResponseCode();
            if (code != 200) {
                log.line("下载失败：HTTP " + code);
                return null;
            }
            long total = conn.getContentLength();
            java.io.InputStream in = new java.io.BufferedInputStream(conn.getInputStream(), 65536);
            FileOutputStream out = new FileOutputStream(tmp);
            byte[] buf = new byte[65536];
            long got = 0;
            int mark = 0;
            int r;
            while ((r = in.read(buf)) > 0) {
                out.write(buf, 0, r);
                got += r;
                int pct = total > 0 ? (int) (got * 100 / total) : 0;
                if (pct >= mark + 10) {
                    mark = pct - pct % 10;
                    log.line("下载中 " + mark + "%（" + (got / 1048576) + " MB）");
                }
            }
            out.close();
            in.close();
            log.line("下载完成，开始解压");
            if (root.exists()) deleteTree(root);
            root.mkdirs();
            InputStream din = new BufferedInputStream(new GZIPInputStream(new FileInputStream(tmp), 65536), 65536);
            int n = untar(din, root, log);
            din.close();
            tmp.delete();
            File java = new File(root, "bin/java");
            if (!java.exists()) {
                log.line("解压后缺少 bin/java");
                return null;
            }
            java.setExecutable(true, false);
            File[] bins = new File(root, "bin").listFiles();
            if (bins != null) for (File f : bins) f.setExecutable(true, false);
            log.line("Java 运行时就绪（共 " + n + " 个文件）");
            return java;
        } catch (Throwable t) {
            log.line("运行时下载/解压失败：" + t);
            return null;
        }
    }


    public interface Log {
        void line(String s);
    }

    public static File jreDir(Context c) {
        return new File(c.getFilesDir(), "jre");
    }

    /** 按设备 ABI 准备运行时：arm64 走内置包，其它架构联网取对应构建。 */
    public static File ensureAny(Context c, Log log) {
        File root = dirFor(c, 17);
        File java = new File(root, "bin/java");
        if (java.exists() && java.length() > 0) { log.line("运行时已存在"); return java; }
        if (!isArm64()) {
            log.line("非 arm64 设备（" + android.os.Build.SUPPORTED_ABIS[0] + "），改用对应架构运行时");
            File j = downloadAndExtract(c, URL_JRE17_X86_64, root, log);
            if (j != null) probeVersion(j, log);
            return j;
        }
        return ensure(c, log);
    }

    /** 解压（已存在则跳过），返回可执行的 java 文件；失败返回 null。 */
    public static File ensure(Context c, Log log) {
        File root = dirFor(c, 17);
        File java = new File(root, "bin/java");
        if (java.exists() && java.length() > 0) {
            log.line("运行时已存在，跳过解压");
            return java;
        }
        log.line("首次启动：开始解压 JRE 17 …");
        try {
            if (root.exists()) deleteTree(root);
            root.mkdirs();
            AssetManager am = c.getAssets();
            InputStream in = new BufferedInputStream(new GZIPInputStream(am.open(ASSET), 65536), 65536);
            int n = untar(in, root, log);
            log.line("解压完成，共 " + n + " 个条目");
            if (!java.exists()) {
                log.line("解压后缺少 bin/java，运行时不可用");
                return null;
            }
            copyAsset(c, "runtime/probe.jar", new File(root.getParentFile(), "probe.jar"), log);
            copyAsset(c, "lwjgl3/lwjgl-glfw-classes.jar", new File(root.getParentFile(), "lwjgl3.jar"), log);
            java.setExecutable(true, false);
            File[] bins = new File(root, "bin").listFiles();
            if (bins != null) for (File f : bins) f.setExecutable(true, false);
            probeVersion(java, log);
            return java;
        } catch (Throwable t) {
            log.line("内置运行时不可用（" + t + "），改为联网下载");
            String url = isArm64() ? URL_JRE17_ARM64 : URL_JRE17_X86_64;
            return downloadAndExtract(c, url, root, log);
        }
    }

    private static void copyAsset(Context c, String asset, File dst, Log log) {
        try {
            if (dst.exists() && dst.length() > 0) return;
            dst.getParentFile().mkdirs();
            InputStream in = c.getAssets().open(asset);
            FileOutputStream out = new FileOutputStream(dst);
            byte[] buf = new byte[16384];
            int r;
            while ((r = in.read(buf)) > 0) out.write(buf, 0, r);
            out.close();
            in.close();
            log.line("已释放 " + dst.getName() + "（" + (dst.length() / 1024) + " KB）");
        } catch (Throwable t) {
            log.line("释放 " + asset + " 失败：" + t);
        }
    }


    /**
     * Android 10+ 禁止从应用数据目录直接 execve（SELinux: execute_no_trans denied），
     * 改由系统 linker 作为入口 mmap 目标二进制，绕开该限制。
     */
    public static java.util.List<String> cmd(File java) {
        java.util.List<String> c = new java.util.ArrayList<String>();
        File linker = new File("/system/bin/linker64");
        boolean useLinker = android.os.Build.VERSION.SDK_INT >= 29 && linker.exists() && linker.canExecute();
        if (useLinker) c.add(linker.getAbsolutePath());
        c.add(java.getAbsolutePath());
        return c;
    }

    /** 跑一次 java -version，证明运行时真的能启动（输出前 2 行）。 */
    public static boolean probeVersion(File java, Log log) {
        if (java == null) return false;
        Process p = null;
        try {
            java.util.List<String> vc = cmd(java); vc.add("-version");
            ProcessBuilder pb = new ProcessBuilder(vc);
            pb.redirectErrorStream(true);
            File rootDir = java.getParentFile().getParentFile();
            pb.environment().put("LD_LIBRARY_PATH", new File(rootDir, "lib").getAbsolutePath()
                    + ":" + java.getParentFile().getAbsolutePath());
            p = pb.start();
            BufferedReader r = new BufferedReader(new InputStreamReader(p.getInputStream()));
            String s; int n = 0;
            while ((s = r.readLine()) != null && n < 2) { log.line("  " + s); android.util.Log.i("NovaRT", "java: " + s); n++; }
            int code = p.waitFor();
            log.line(code == 0 ? "Java 运行时自检通过" : "Java 运行时自检失败（退出码 " + code + "）");
            android.util.Log.i("NovaRT", "probeVersion exit=" + code);
            return code == 0;
        } catch (Throwable t) {
            log.line("Java 运行时无法执行：" + t);
            return false;
        } finally {
            if (p != null) try { p.destroy(); } catch (Throwable ignored) {}
        }
    }

    /** 用解好的 JRE 跑原生栈自检：加载 gl4es / lwjgl / openal，并读 LWJGL 版本。 */
    public static boolean probeNative(Context c, File java, Log log) {
        if (java == null) return false;
        File root = java.getParentFile().getParentFile();
        File work = root.getParentFile();
        Process p = null;
        try {
            String libPath = c.getApplicationInfo().nativeLibraryDir + ":" + new File(root, "lib").getAbsolutePath();
            String cp = new File(work, "probe.jar").getAbsolutePath() + ":"
                    + new File(work, "lwjgl3.jar").getAbsolutePath();
            java.util.List<String> pc = cmd(java); pc.add("-cp"); pc.add(cp); pc.add("nova.probe.NativeProbe");
            ProcessBuilder pb = new ProcessBuilder(pc);
            pb.directory(work);
            pb.redirectErrorStream(true);
            pb.environment().put("LD_LIBRARY_PATH", libPath);
            pb.environment().put("LIBGL_ES", "3");
            pb.environment().put("LIBGL_GL", "21");
            p = pb.start();
            BufferedReader r = new BufferedReader(new InputStreamReader(p.getInputStream()));
            String s;
            boolean ok = false;
            while ((s = r.readLine()) != null) {
                log.line("  " + s);
                if (s.contains("PROBE so gl4es_114 OK") && s.contains("PROBE so lwjgl OK")) ok = true;
            }
            p.waitFor();
            return ok;
        } catch (Throwable t) {
            log.line("原生栈自检失败：" + t);
            return false;
        } finally {
            if (p != null) p.destroy();
        }
    }

    /** 跑 `java -version`，把输出回传。 */
    public static boolean probe(File java, Log log) {
        if (java == null) return false;
        Process p = null;
        try {
            File root = java.getParentFile().getParentFile();
            ProcessBuilder pb = new ProcessBuilder(cmd(java));
            pb.directory(root);
            pb.redirectErrorStream(true);
            pb.environment().put("LD_LIBRARY_PATH", new File(root, "lib").getAbsolutePath());
            p = pb.start();
            BufferedReader r = new BufferedReader(new InputStreamReader(p.getInputStream()));
            String s;
            while ((s = r.readLine()) != null) log.line("  " + s);
            int code = p.waitFor();
            log.line("java -version 退出码 " + code);
            return code == 0;
        } catch (Throwable t) {
            log.line("启动 java 失败：" + t);
            return false;
        } finally {
            if (p != null) p.destroy();
        }
    }

    // ---- 极简 tar 读取（只处理 JRE 包里实际出现的 '0' 文件 / '5' 目录 / GNU 长名） ----
    private static int untar(InputStream in, File root, Log log) throws Exception {
        byte[] hdr = new byte[512];
        int count = 0;
        String longName = null;
        while (true) {
            int got = readFully(in, hdr, 512);
            if (got < 512) break;
            String name = str(hdr, 0, 100);
            String prefix = str(hdr, 345, 155);
            long size = octal(hdr, 124, 12);
            char type = (char) hdr[156];
            if (prefix != null && !prefix.isEmpty()) name = prefix + "/" + name;
            if (type == 'L') { // GNU long name：内容就是下一条的名字
                byte[] buf = new byte[(int) size];
                readFully(in, buf, buf.length);
                longName = new String(buf).trim();
                continue;
            }
            if (longName != null) {
                name = longName;
                longName = null;
            }
            if (name == null || name.isEmpty()) {
                skip(in, size);
                continue;
            }
            File out = new File(root, name);
            if (type == '5') {
                out.mkdirs();
            } else if (type == '0' || type == '\0') {
                out.getParentFile().mkdirs();
                FileOutputStream fos = new FileOutputStream(out);
                copy(in, fos, size);
                fos.close();
                count++;
            } else {
                skip(in, size);
            }
            skip(in, pad(size));
        }
        return count;
    }

    private static long pad(long size) {
        long m = size % 512;
        return m == 0 ? 0 : 512 - m;
    }

    private static void copy(InputStream in, FileOutputStream out, long size) throws Exception {
        byte[] buf = new byte[16384];
        long left = size;
        while (left > 0) {
            int want = (int) Math.min(buf.length, left);
            int r = in.read(buf, 0, want);
            if (r < 0) break;
            out.write(buf, 0, r);
            left -= r;
        }
    }

    private static void skip(InputStream in, long size) throws Exception {
        byte[] buf = new byte[8192];
        long left = size;
        while (left > 0) {
            int r = in.read(buf, 0, (int) Math.min(buf.length, left));
            if (r < 0) break;
            left -= r;
        }
    }

    private static int readFully(InputStream in, byte[] b, int len) throws Exception {
        int off = 0;
        while (off < len) {
            int r = in.read(b, off, len - off);
            if (r < 0) return off;
            off += r;
        }
        return off;
    }

    private static String str(byte[] b, int off, int len) {
        StringBuilder sb = new StringBuilder();
        for (int i = off; i < off + len && i < b.length; i++) {
            if (b[i] == 0) break;
            sb.append((char) b[i]);
        }
        return sb.toString();
    }

    private static long octal(byte[] b, int off, int len) {
        String s = str(b, off, len).trim();
        if (s.isEmpty()) return 0;
        try {
            return Long.parseLong(s, 8);
        } catch (Exception e) {
            return 0;
        }
    }

    private static void deleteTree(File f) {
        File[] kids = f.listFiles();
        if (kids != null) for (File k : kids) deleteTree(k);
        f.delete();
    }

    /** 供过渡期使用：判断当前设备是否 arm64。 */
    public static boolean isArm64() {
        for (String abi : android.os.Build.SUPPORTED_ABIS) {
            if ("arm64-v8a".equals(abi)) return true;
        }
        return false;
    }
}
