package net.kdt.pojavlaunch;

/** libpojavexec 的日志回调目标，占位实现把日志转到 logcat。 */
public final class Logger {
    public interface eventLogListener { void onLogReceived(String str); }

    public static void appendToLog(String str) {
        android.util.Log.i("jrelog", str == null ? "" : str);
    }

    public static void begin(String str) { }
    public static void setLogListener(eventLogListener l) { }
}
