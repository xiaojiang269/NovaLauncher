package com.xiaojiang269.novalauncher;

import android.content.Context;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedInputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;
import java.util.List;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;

/**
 * 版本安装器：拉 version_manifest → version json → client.jar + libraries(+natives) + assets 索引。
 * 只负责把「跑起来所需的文件」落盘，不参与启动参数拼装。
 */
public final class VersionInstaller {

    public interface Log { void line(String s); }

    public static final String MANIFEST_A = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";
    public static final String MANIFEST_B = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    /** 安装结果：给启动器拼 classpath 用。 */
    public static final class Installed {
        public File gameDir;      // .../games/<ver>
        public File clientJar;
        public File nativesDir;
        public File assetsDir;
        public String assetIndexId;
        public List<File> classpath = new ArrayList<File>();
        public String mainClass;
        public String versionId;
    }

    public static File baseDir(Context c, String ver) {
        File d = new File(c.getExternalFilesDir(null), "games/" + ver);
        d.mkdirs();
        return d;
    }

    /** 完整安装；已存在且非空的文件跳过，支持断点续装。 */
    public static Installed install(Context c, String ver, Log log) throws Exception {
        Installed r = new Installed();
        r.gameDir = baseDir(c, ver);
        r.nativesDir = new File(r.gameDir, "natives");
        r.nativesDir.mkdirs();
        r.assetsDir = new File(r.gameDir, "assets");
        r.assetsDir.mkdirs();

        log.line("解析版本清单 …");
        JSONObject manifest = null;
        for (String url : new String[]{MANIFEST_A, MANIFEST_B}) {
            try { manifest = new JSONObject(get(url)); break; } catch (Throwable t) { log.line("清单源失败：" + t); }
        }
        if (manifest == null) throw new Exception("版本清单拉取失败，检查网络或代理");

        JSONObject verJson = null;
        JSONArray arr = manifest.getJSONArray("versions");
        for (int i = 0; i < arr.length(); i++) {
            JSONObject o = arr.getJSONObject(i);
            if (ver.equals(o.optString("id"))) {
                String u = o.optString("url");
                log.line("找到 " + ver + "，下载版本 JSON（" + o.optString("type") + "）");
                verJson = new JSONObject(get(u));
                writeIfChanged(new File(r.gameDir, ver + ".json"), get(u));
                break;
            }
        }
        if (verJson == null) throw new Exception("清单里没有版本 " + ver);

        r.versionId = ver;
        r.mainClass = verJson.optString("mainClass", "net.minecraft.client.main.Main");

        // 1) client.jar
        JSONObject dl = verJson.getJSONObject("downloads").getJSONObject("client");
        File jar = new File(r.gameDir, ver + ".jar");
        r.clientJar = jar;
        fetchTo(dl.getString("url"), jar, dl.optLong("size", 0), "client.jar", log);

        // 2) libraries（含 natives 解包）
        JSONArray libs = verJson.getJSONArray("libraries");
        int jars = 0, nats = 0, skip = 0;
        for (int i = 0; i < libs.length(); i++) {
            JSONObject lib = libs.getJSONObject(i);
            if (!allowed(lib)) { skip++; continue; }
            JSONObject d = lib.optJSONObject("downloads");
            if (d == null) { skip++; continue; }

            String name = lib.optString("name", "");
            JSONObject art = d.optJSONObject("artifact");
            boolean isNative = name.contains("natives-linux") || name.contains("natives-android")
                    || (lib.optJSONObject("natives") != null);
            if (isNative) {
                if (art != null && art.optString("url").length() > 0) {
                    File tmp = new File(r.nativesDir, "tmp-native.jar");
                    fetchTo(art.getString("url"), tmp, art.optLong("size", 0), name, log);
                    unzipSo(tmp, r.nativesDir, log);
                    tmp.delete();
                    nats++;
                }
                continue;
            }
            if (art != null && art.optString("url").length() > 0) {
                String path = art.optString("path", name.replace(':', '/') + ".jar");
                File out = new File(new File(r.gameDir, "libraries"), path);
                fetchTo(art.getString("url"), out, art.optLong("size", 0), path, log);
                r.classpath.add(out);
                jars++;
            }
        }
        log.line("库文件 " + jars + " 个，原生库 " + nats + " 个，跳过 " + skip);

        // 3) assets 索引（物体不预取，进游戏时按需下载）
        JSONObject as = verJson.optJSONObject("assetIndex");
        if (as != null) {
            r.assetIndexId = as.optString("id", ver);
            File idx = new File(new File(r.assetsDir, "indexes"), r.assetIndexId + ".json");
            fetchTo(as.getString("url"), idx, as.optLong("size", 0), "assets 索引", log);
        } else {
            r.assetIndexId = "legacy";
        }

        // 3.5) assets objects（贴图/音效按清单全量下载，已存在则跳过）
        try {
            File idxFile = new File(new File(r.assetsDir, "indexes"), r.assetIndexId + ".json");
            if (idxFile.exists()) downloadObjects(idxFile, r.assetsDir, log);
        } catch (Throwable t) {
            log.line("资源下载中断（可重试续传）：" + t);
        }

        // 4) 运行参数文件（1.13+ 的 logging 配置）
        JSONObject logging = verJson.optJSONObject("logging");
        if (logging != null && logging.optJSONObject("client") != null) {
            JSONObject cl = logging.getJSONObject("client");
            JSONObject f = cl.optJSONObject("file");
            if (f != null && f.optString("url").length() > 0) {
                fetchTo(f.getString("url"), new File(r.gameDir, "log4j2.xml"), f.optLong("size", 0), "日志配置", log);
            }
        }
        log.line("安装完成：" + r.gameDir.getAbsolutePath());
        return r;
    }

    /** 规则过滤：默认允许；有 rules 时按 os.name=linux 判定。 */
    private static boolean allowed(JSONObject lib) throws Exception {
        JSONArray rules = lib.optJSONArray("rules");
        if (rules == null) return true;
        boolean allow = false;
        for (int i = 0; i < rules.length(); i++) {
            JSONObject r = rules.getJSONObject(i);
            String action = r.optString("action");
            JSONObject os = r.optJSONObject("os");
            boolean applies = true;
            if (os != null) {
                String name = os.optString("name", "");
                if (name.length() > 0 && !"linux".equals(name) && !"android".equals(name)) applies = false;
                String arch = os.optString("arch", "");
                if (applies && arch.length() > 0 && !arch.contains("64")) applies = false;
            }
            if (applies) allow = "allow".equals(action);
        }
        return allow;
    }


    /** 下载 assets 索引里列出的全部 objects；同 hash 已存在且大小一致则跳过。 */
    private static void downloadObjects(File idxFile, File assetsRoot, Log log) throws Exception {
        JSONObject idx = new JSONObject(new String(java.nio.file.Files.readAllBytes(idxFile.toPath()), "UTF-8"));
        JSONObject objs = idx.optJSONObject("objects");
        if (objs == null) return;
        JSONArray names = objs.names();
        if (names == null) return;
        int total = names.length(), done = 0, skipped = 0, failed = 0;
        long bytes = 0;
        log.line("资源 objects 共 " + total + " 个");
        for (int i = 0; i < total; i++) {
            String name = names.getString(i);
            JSONObject o = objs.getJSONObject(name);
            String hash = o.optString("hash");
            long size = o.optLong("size", 0);
            if (hash.length() < 4) continue;
            File dst = new File(new File(new File(assetsRoot, "objects"), hash.substring(0, 2)), hash);
            if (dst.exists() && dst.length() == size) { skipped++; done++; continue; }
            try {
                String url = "https://resources.download.minecraft.net/" + hash.substring(0, 2) + "/" + hash;
                fetchToQuiet(url, dst, size, "res");
                bytes += dst.length();
            } catch (Throwable t) {
                failed++;
            }
            done++;
            if (done % 100 == 0 || done == total) {
                log.line("资源 " + done + "/" + total + "（跳过 " + skipped + "，失败 " + failed
                        + "，已下载 " + (bytes >> 20) + "MB）");
            }
        }
        log.line("资源完成：成功 " + (total - skipped - failed) + " 个，复用 " + skipped + " 个，失败 " + failed + " 个");
        if (failed > 0) throw new Exception("有 " + failed + " 个资源下载失败，再点一次可续传");
    }

    /** 无逐文件日志的下载，用于 assets 小文件批量拉取。 */
    private static void fetchToQuiet(String url, File dst, long size, String label) throws Exception {
        if (dst.exists() && dst.length() > 0 && (size <= 0 || dst.length() == size)) return;
        dst.getParentFile().mkdirs();
        File tmp = new File(dst.getAbsolutePath() + ".part");
        HttpURLConnection conn = (HttpURLConnection) new URL(url).openConnection();
        conn.setInstanceFollowRedirects(true);
        conn.setConnectTimeout(15000);
        conn.setReadTimeout(30000);
        conn.setRequestProperty("User-Agent", "NovaLauncherApp/3.5.0");
        int code = conn.getResponseCode();
        if (code != 200) throw new Exception("HTTP " + code);
        InputStream in = new BufferedInputStream(conn.getInputStream(), 32768);
        FileOutputStream out = new FileOutputStream(tmp);
        byte[] buf = new byte[32768];
        int rr;
        while ((rr = in.read(buf)) > 0) out.write(buf, 0, rr);
        out.close(); in.close();
        if (tmp.length() == 0) throw new Exception("空文件");
        if (dst.exists()) dst.delete();
        tmp.renameTo(dst);
    }

    private static void unzipSo(File zip, File outDir, Log log) throws Exception {
        ZipInputStream zin = new ZipInputStream(new BufferedInputStream(new java.io.FileInputStream(zip)));
        ZipEntry e;
        int n = 0;
        while ((e = zin.getNextEntry()) != null) {
            if (e.isDirectory()) continue;
            String nm = e.getName();
            if (!nm.endsWith(".so") && !nm.endsWith(".dylib") && !nm.endsWith(".dll")) continue;
            File out = new File(outDir, new File(nm).getName());
            FileOutputStream fo = new FileOutputStream(out);
            byte[] buf = new byte[8192];
            int r;
            while ((r = zin.read(buf)) > 0) fo.write(buf, 0, r);
            fo.close();
            out.setExecutable(true, false);
            n++;
        }
        zin.close();
        if (n > 0) log.line("  原生库解出 " + n + " 个 .so");
    }

    private static void writeIfChanged(File f, String data) throws Exception {
        f.getParentFile().mkdirs();
        FileOutputStream out = new FileOutputStream(f);
        out.write(data.getBytes("UTF-8"));
        out.close();
    }

    /** 带进度的下载；目标已存在且大小一致则跳过。 */
    private static void fetchTo(String url, File dst, long size, String label, Log log) throws Exception {
        if (dst.exists() && dst.length() > 0 && (size <= 0 || dst.length() == size)) {
            return;
        }
        dst.getParentFile().mkdirs();
        File tmp = new File(dst.getAbsolutePath() + ".part");
        HttpURLConnection conn = (HttpURLConnection) new URL(url).openConnection();
        conn.setInstanceFollowRedirects(true);
        conn.setConnectTimeout(20000);
        conn.setReadTimeout(60000);
        conn.setRequestProperty("User-Agent", "NovaLauncherApp/3.4.9");
        int code = conn.getResponseCode();
        if (code != 200) throw new Exception("HTTP " + code + " <- " + url);
        long total = conn.getContentLength() > 0 ? conn.getContentLength() : size;
        InputStream in = new BufferedInputStream(conn.getInputStream(), 65536);
        FileOutputStream out = new FileOutputStream(tmp);
        byte[] buf = new byte[65536];
        long got = 0;
        int mark = -1;
        int r;
        while ((r = in.read(buf)) > 0) {
            out.write(buf, 0, r);
            got += r;
            if (total > 0) {
                int pct = (int) (got * 100 / total);
                if (pct >= mark + 20) { mark = pct - pct % 20; log.line(label + " " + mark + "%（" + (got >> 20) + "MB）"); }
            }
        }
        out.close(); in.close();
        if (tmp.length() == 0) throw new Exception("下载为空：" + url);
        if (dst.exists()) dst.delete();
        if (!tmp.renameTo(dst)) throw new Exception("重命名失败：" + dst);
        if (mark < 100 && total > 0) log.line(label + " 完成（" + (dst.length() >> 20) + "MB）");
    }

    public static String get(String url) throws Exception {
        HttpURLConnection conn = (HttpURLConnection) new URL(url).openConnection();
        conn.setInstanceFollowRedirects(true);
        conn.setConnectTimeout(20000);
        conn.setReadTimeout(30000);
        conn.setRequestProperty("User-Agent", "NovaLauncherApp/3.4.9");
        int code = conn.getResponseCode();
        if (code != 200) throw new Exception("HTTP " + code);
        InputStream in = conn.getInputStream();
        StringBuilder sb = new StringBuilder();
        byte[] buf = new byte[8192];
        int r;
        while ((r = in.read(buf)) > 0) sb.append(new String(buf, 0, r, "UTF-8"));
        in.close();
        return sb.toString();
    }
}
