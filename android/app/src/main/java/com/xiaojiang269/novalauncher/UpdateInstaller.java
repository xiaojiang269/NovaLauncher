package com.xiaojiang269.novalauncher;

import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageInstaller;

import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;

/**
 * 启动器内直接更新：把新 APK 流式下载进 PackageInstaller 会话并提交安装。
 * 不跳浏览器、不落第三方应用，只经过系统自带的安装确认框。
 */
public final class UpdateInstaller {

    public interface Progress { void onProgress(String msg); }

    public static void start(final Context ctx, final String url, final Progress cb) {
        new Thread(new Runnable() {
            @Override
            public void run() {
                HttpURLConnection c = null;
                PackageInstaller.Session session = null;
                try {
                    PackageInstaller pi = ctx.getPackageManager().getPackageInstaller();
                    PackageInstaller.SessionParams p =
                            new PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL);
                    if (android.os.Build.VERSION.SDK_INT >= 31) p.setRequireUserAction(
                            PackageInstaller.SessionParams.USER_ACTION_REQUIRED);
                    int id = pi.createSession(p);
                    session = pi.openSession(id);

                    c = (HttpURLConnection) new URL(url).openConnection();
                    c.setInstanceFollowRedirects(true);
                    c.setConnectTimeout(15000);
                    c.setReadTimeout(30000);
                    c.setRequestProperty("User-Agent", "NovaLauncherApp");
                    long len = c.getContentLength();
                    if (cb != null) cb.onProgress("开始下载更新包…");
                    OutputStream out = session.openWrite("nova-update.apk", 0, len > 0 ? len : -1);
                    InputStream in = c.getInputStream();
                    byte[] buf = new byte[8192];
                    long got = 0;
                    int n;
                    int mark = 0;
                    while ((n = in.read(buf)) > 0) {
                        out.write(buf, 0, n);
                        got += n;
                        if (len > 0) {
                            int pc = (int) (got * 100 / len);
                            if (pc >= mark + 10) {
                                mark = pc;
                                if (cb != null) cb.onProgress("已下载 " + pc + "%");
                            }
                        }
                    }
                    in.close();
                    out.flush();
                    out.close();
                    if (session != null) session.fsync(out);

                    if (cb != null) cb.onProgress("下载完成，正在安装…");
                    Intent done = new Intent(ctx, MainActivity.class);
                    done.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                    PendingIntent pi2 = PendingIntent.getActivity(ctx, 0, done,
                            PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
                    session.commit(pi2.getIntentSender());
                } catch (Throwable t) {
                    if (cb != null) cb.onProgress("更新失败：" + t);
                    if (session != null) {
                        try { session.abandon(); } catch (Throwable ignored) { }
                    }
                } finally {
                    if (session != null) {
                        try { session.close(); } catch (Throwable ignored) { }
                    }
                    if (c != null) c.disconnect();
                }
            }
        }, "nova-update").start();
    }
}
