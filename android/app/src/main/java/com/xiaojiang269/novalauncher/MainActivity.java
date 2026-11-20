package com.xiaojiang269.novalauncher;

import android.app.Activity;
import android.graphics.Color;
import android.os.Build;
import android.os.Bundle;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.webkit.JavascriptInterface;
import java.io.BufferedReader;
import java.io.File;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

public class MainActivity extends Activity {

    private WebView web;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN,
                WindowManager.LayoutParams.FLAG_FULLSCREEN);
        if (Build.VERSION.SDK_INT >= 21) {
            getWindow().setStatusBarColor(Color.BLACK);
            getWindow().setNavigationBarColor(Color.BLACK);
        }

        web = new WebView(this);
        web.setBackgroundColor(Color.BLACK);
        web.setLayoutParams(new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));

        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setDatabaseEnabled(true);
        s.setAllowFileAccess(true);
        s.setAllowContentAccess(true);
        s.setAllowFileAccessFromFileURLs(true);
        s.setAllowUniversalAccessFromFileURLs(true);
        s.setLoadWithOverviewMode(true);
        s.setUseWideViewPort(true);
        s.setMediaPlaybackRequiresUserGesture(false);
        s.setCacheMode(WebSettings.LOAD_DEFAULT);
        if (Build.VERSION.SDK_INT >= 21) {
            s.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
        }
        s.setUserAgentString(s.getUserAgentString() + " NovaLauncherApp/3.4.9");

        web.addJavascriptInterface(new NetBridge(), "NovaBridge");
        web.setWebViewClient(new WebViewClient());
        web.setWebChromeClient(new WebChromeClient());
        web.setOverScrollMode(View.OVER_SCROLL_NEVER);

        applyOrientation();
        setContentView(web);
        web.loadUrl("file:///android_asset/index.html");
    }

    /** 屏幕方向偏好：landscape / portrait / auto，默认横屏（键位布局按 PC 设计）。 */
    private void applyOrientation() {
        String mode = getSharedPreferences("nova_prefs", MODE_PRIVATE).getString("orient", "auto");
        int o;
        if ("portrait".equals(mode)) o = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_SENSOR_PORTRAIT;
        else if ("sensor".equals(mode)) o = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_FULL_SENSOR;
        else o = android.content.pm.ActivityInfo.SCREEN_ORIENTATION_SENSOR_LANDSCAPE;   // auto = 自动横屏（左右随重力翻转）
        setRequestedOrientation(o);
    }

    public class NetBridge {
        /** 启动游戏容器：进入触屏输入层自检场景。 */
        @JavascriptInterface
        public void launchGame() {
            runOnUiThread(new Runnable() {
                public void run() {
                    try {
                        startActivity(new android.content.Intent(MainActivity.this, GameActivity.class));
                    } catch (Throwable ignored) {
                    }
                }
            });
        }

        /** 切换屏幕方向：landscape / portrait / auto。 */
        @JavascriptInterface
        public void setOrientation(final String mode) {
            getSharedPreferences("nova_prefs", MODE_PRIVATE).edit().putString("orient", mode).apply();
            runOnUiThread(new Runnable() {
                public void run() { applyOrientation(); }
            });
        }

        /** 当前屏幕方向偏好。 */
        @JavascriptInterface
        public String orientation() {
            return getSharedPreferences("nova_prefs", MODE_PRIVATE).getString("orient", "auto");
        }

        /** 全量 MC 版本目录（含每个版本的 Java 需求），供页面渲染版本列表。 */
        @JavascriptInterface
        public String versions() {
            java.util.ArrayList<VersionCatalog.Ver> list = VersionCatalog.fetch();
            if (list.isEmpty()) return "[]";
            try {
                java.io.FileOutputStream fo = new java.io.FileOutputStream(
                        new java.io.File(getFilesDir(), "versions.json"));
                fo.write(VersionCatalog.toJson(list).getBytes("UTF-8"));
                fo.close();
            } catch (Exception ignored) {
            }
            return VersionCatalog.toJson(list);
        }

        /** 查某个版本要的 Java 主版本（不联网，公式换算）。 */
        @JavascriptInterface
        public int javaMajorFor(String releaseTime) {
            return VersionCatalog.javaFor(releaseTime);
        }

        /** 确保某版本的 Java 已安装；返回 OK / DOWNLOADING / UNSUPPORTED / FAIL。 */
        @JavascriptInterface
        public String ensureJava(final int major) {
            if (major == 21) return "UNSUPPORTED";
            final File java = new File(RuntimeBoot.dirFor(MainActivity.this, major), "bin/java");
            if (java.exists() && java.length() > 0) return "OK";
            new Thread(new Runnable() {
                public void run() {
                    RuntimeBoot.ensureFor(MainActivity.this, major, new RuntimeBoot.Log() {
                        public void line(String s) {
                        }
                    });
                }
            }, "java-install-" + major).start();
            return "STARTED";
        }

        /** 当前各 Java 运行时的安装状态。 */
        @JavascriptInterface
        public String javaStatus() {
            StringBuilder sb = new StringBuilder("{");
            int[] majors = new int[]{8, 17, 21};
            for (int i = 0; i < majors.length; i++) {
                if (i > 0) sb.append(',');
                File f = new File(RuntimeBoot.dirFor(MainActivity.this, majors[i]), "bin/java");
                sb.append('"').append(majors[i]).append('"').append(':')
                        .append(f.exists() ? "true" : "false");
            }
            return sb.append('}').toString();
        }

        /** 通用 GET（返回 JSON 文本），给 Modrinth 等开放 API 用。 */
        /** 用系统浏览器打开链接（下载 APK / 打开发布页）。 */
        @JavascriptInterface
        public void openUrl(String url) {
            try {
                android.content.Intent i = new android.content.Intent(android.content.Intent.ACTION_VIEW,
                        android.net.Uri.parse(url));
                i.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK);
                MainActivity.this.startActivity(i);
            } catch (Exception ignored) {
            }
        }

        @JavascriptInterface
        public String apiGet(String url) {
            HttpURLConnection c = null;
            try {
                c = (HttpURLConnection) new URL(url).openConnection();
                c.setConnectTimeout(15000);
                c.setReadTimeout(20000);
                c.setRequestProperty("User-Agent", "NovaLauncherApp/3.4.6 (android)");
                c.setRequestProperty("Accept", "application/json");
                int code = c.getResponseCode();
                if (code < 200 || code >= 300) return "ERROR:HTTP " + code;
                BufferedReader br = new BufferedReader(new InputStreamReader(c.getInputStream(), "UTF-8"));
                StringBuilder sb = new StringBuilder();
                String line;
                while ((line = br.readLine()) != null) sb.append(line);
                br.close();
                return sb.toString();
            } catch (Exception e) {
                return "ERROR:" + e.getMessage();
            } finally {
                if (c != null) c.disconnect();
            }
        }

        /** 下载模组/光影/资源包到 App 的 mods 目录，返回本地路径。 */
        @JavascriptInterface
        public String modSave(String url, String fileName) {
            HttpURLConnection c = null;
            try {
                File dir = MainActivity.this.getExternalFilesDir("mods");
                if (dir == null) dir = new File(getFilesDir(), "mods");
                dir.mkdirs();
                String safe = fileName.replaceAll("[\\/:*?\"<>|]", "_");
                File out = new File(dir, safe);
                c = (HttpURLConnection) new URL(url).openConnection();
                c.setInstanceFollowRedirects(true);
                c.setConnectTimeout(20000);
                c.setReadTimeout(60000);
                c.setRequestProperty("User-Agent", "NovaLauncherApp/3.4.6 (android)");
                int code = c.getResponseCode();
                if (code < 200 || code >= 300) return "ERROR:HTTP " + code;
                java.io.InputStream in = new java.io.BufferedInputStream(c.getInputStream(), 65536);
                java.io.FileOutputStream fo = new java.io.FileOutputStream(out);
                byte[] buf = new byte[65536];
                int r;
                while ((r = in.read(buf)) > 0) fo.write(buf, 0, r);
                fo.close();
                in.close();
                return "OK:" + out.getAbsolutePath() + ":" + out.length();
            } catch (Exception e) {
                return "ERROR:" + e.getMessage();
            } finally {
                if (c != null) c.disconnect();
            }
        }

        /** 已下载到本地的模组文件清单。 */
        @JavascriptInterface
        public String modList() {
            File dir = MainActivity.this.getExternalFilesDir("mods");
            if (dir == null) dir = new File(getFilesDir(), "mods");
            StringBuilder sb = new StringBuilder("[");
            File[] files = dir.listFiles();
            if (files != null) {
                boolean first = true;
                for (File f : files) {
                    if (!f.isFile()) continue;
                    if (!first) sb.append(',');
                    first = false;
                    sb.append("{\"name\":\"").append(f.getName().replace("\"", "'"))
                            .append("\",\"size\":").append(f.length()).append('}');
                }
            }
            return sb.append(']').toString();
        }

        @JavascriptInterface
        public boolean modDelete(String name) {
            File dir = MainActivity.this.getExternalFilesDir("mods");
            if (dir == null) dir = new File(getFilesDir(), "mods");
            File f = new File(dir, name);
            return f.isFile() && f.delete();
        }

        @JavascriptInterface
        public String httpGet(String url) {
            HttpURLConnection c = null;
            try {
                c = (HttpURLConnection) new URL(url).openConnection();
                c.setRequestMethod("GET");
                c.setConnectTimeout(15000);
                c.setReadTimeout(15000);
                c.setRequestProperty("Accept", "application/vnd.github+json");
                c.setRequestProperty("User-Agent", "NovaLauncherApp/3.4.9");
                int code = c.getResponseCode();
                if (code < 200 || code >= 300) {
                    return "ERROR:HTTP " + code;
                }
                BufferedReader br = new BufferedReader(new InputStreamReader(c.getInputStream(), "UTF-8"));
                StringBuilder sb = new StringBuilder();
                String line;
                while ((line = br.readLine()) != null) sb.append(line);
                br.close();
                return sb.toString();
            } catch (Exception e) {
                return "ERROR:" + e.getMessage();
            } finally {
                if (c != null) c.disconnect();
            }
        }

        // ===== Java 版启动闭环（实验）=====
        private final StringBuilder jlog = new StringBuilder();
        private Thread jthread;

        private void jline(String s) {
            synchronized (jlog) {
                jlog.append(s).append('\n');
                if (jlog.length() > 8000) jlog.delete(0, jlog.length() - 6000);
            }
        }

        @JavascriptInterface
        public String javaLaunch(final String ver, final String user) {
            if (GameLauncher.isRunning()) return "RUNNING";
            synchronized (jlog) { jlog.setLength(0); }
            jline("准备启动 Java 版 " + ver + "，账号 " + user);
            jthread = new Thread(new Runnable() {
                public void run() {
                    GameLauncher.launch(MainActivity.this, ver, user, new GameLauncher.Log() {
                        public void line(String s) { jline(s); }
                    });
                }
            }, "java-launch");
            jthread.setDaemon(true);
            jthread.start();
            return "STARTED";
        }

        @JavascriptInterface
        public String javaLog() {
            synchronized (jlog) { return jlog.toString(); }
        }

        @JavascriptInterface
        public boolean javaRunning() { return GameLauncher.isRunning(); }

        @JavascriptInterface
        public void javaStop() {
            GameLauncher.stop();
            jline("已请求停止游戏进程");
        }
    }

    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_BACK && web != null && web.canGoBack()) {
            web.goBack();
            return true;
        }
        return super.onKeyDown(keyCode, event);
    }

    @Override
    protected void onDestroy() {
        if (web != null) {
            web.destroy();
            web = null;
        }
        super.onDestroy();
    }
}
