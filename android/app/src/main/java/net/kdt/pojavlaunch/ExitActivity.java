package net.kdt.pojavlaunch;

import android.app.Activity;

/** libpojavexec 的退出钩子要启动这个 Activity；我们只做占位。 */
public class ExitActivity extends Activity {
    public static native int getPid();
    public static native void killProcess(int pid);
}
