package com.xiaojiang269.novalauncher;

import android.app.Activity;
import android.content.Intent;
import android.graphics.Color;
import android.os.Bundle;
import android.text.TextUtils;
import android.util.TypedValue;
import android.view.Gravity;
import android.view.WindowManager;
import android.widget.LinearLayout;
import android.widget.ProgressBar;
import android.widget.TextView;

import java.io.File;

/**
 * 启动闸门：进主界面之前先确认 Java 运行时。
 *
 * - 已装好 Java 17 → 直接进主界面（不做任何联网动作）
 * - 没装 / 缺文件 → 自动安装：优先用安装包内自带的 JRE17，失败再走下载
 * - 陌生设备 ABI 或失败 → 明确报错并停在闸门上，不硬闯
 */
public class SplashActivity extends Activity {

    private TextView status;
    private TextView detail;
    private ProgressBar bar;

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        setContentView(buildUi());
        checkRuntime();
    }

    private LinearLayout buildUi() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setBackgroundColor(Color.parseColor("#0B0F0C"));
        root.setGravity(Gravity.CENTER);
        root.setPadding(dp(28), dp(28), dp(28), dp(28));

        TextView logo = new TextView(this);
        logo.setText("NovaLauncher");
        logo.setTextColor(Color.WHITE);
        logo.setTextSize(TypedValue.COMPLEX_UNIT_SP, 30);
        logo.setTypeface(logo.getTypeface(), android.graphics.Typeface.BOLD);
        logo.setGravity(Gravity.CENTER);
        root.addView(logo);

        status = new TextView(this);
        status.setText("正在检查 Java 运行环境 …");
        status.setTextColor(Color.parseColor("#9BBF6A"));
        status.setTextSize(TypedValue.COMPLEX_UNIT_SP, 16);
        status.setGravity(Gravity.CENTER);
        status.setPadding(0, dp(18), 0, dp(10));
        root.addView(status);

        bar = new ProgressBar(this, null, android.R.attr.progressBarStyleHorizontal);
        bar.setIndeterminate(true);
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(dp(220), dp(4));
        root.addView(bar, lp);

        detail = new TextView(this);
        detail.setText("");
        detail.setTextColor(Color.parseColor("#B8C4B8"));
        detail.setTextSize(TypedValue.COMPLEX_UNIT_SP, 12);
        detail.setGravity(Gravity.CENTER);
        detail.setPadding(0, dp(14), 0, 0);
        detail.setMaxLines(8);
        root.addView(detail);
        return root;
    }

    private int dp(int v) {
        return (int) (v * getResources().getDisplayMetrics().density);
    }

    private void checkRuntime() {
        final File fast = new File(RuntimeBoot.dirFor(this, 17), "bin/java");
        if (fast.exists() && fast.length() > 0) {
            go();
            return;
        }
        new Thread(new Runnable() {
            @Override
            public void run() {
                if (!RuntimeBoot.isArm64()) {
                    show("当前设备不是 arm64，暂不支持", "SUPPORTED_ABIS=" + android.os.Build.SUPPORTED_ABIS[0], false);
                    return;
                }
                showStatus("首次启动：正在安装 Java 17 …");
                RuntimeBoot.Log log = new RuntimeBoot.Log() {
                    public void line(final String s) {
                        runOnUiThread(new Runnable() {
                            public void run() {
                                detail.setText(s);
                            }
                        });
                    }
                };
                File java = RuntimeBoot.ensure(SplashActivity.this, log);
                if (java == null) {
                    show("Java 安装失败", "可尝试重新安装 APK，或把上面的日志发给我们", false);
                    return;
                }
                runOnUiThread(new Runnable() {
                    public void run() {
                        bar.setIndeterminate(false);
                        bar.setProgress(100);
                    }
                });
                showStatus("Java 17 就绪，进入主界面");
                go();
            }
        }, "splash-java").start();
    }

    private void showStatus(final String s) {
        runOnUiThread(new Runnable() {
            public void run() {
                status.setText(s);
            }
        });
    }

    private void show(final String s, final String d, final boolean ok) {
        runOnUiThread(new Runnable() {
            public void run() {
                status.setText(s);
                status.setTextColor(Color.parseColor(ok ? "#9BBF6A" : "#FF8A80"));
                detail.setText(d);
                bar.setIndeterminate(false);
            }
        });
    }

    private void go() {
        runOnUiThread(new Runnable() {
            public void run() {
                startActivity(new Intent(SplashActivity.this, MainActivity.class));
                finish();
            }
        });
    }
}
