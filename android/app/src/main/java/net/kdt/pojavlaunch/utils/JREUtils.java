package net.kdt.pojavlaunch.utils;

import android.content.Context;
/**
 * 与 Pojav 的 libpojavexec.so 对接的最小 JNI 封装。
 *
 * 这些 native 方法的实现都在 libpojavexec.so 里，JNI 按「类名+方法名」绑定，
 * 所以只要我们声明同名同类，不需要 Pojav 的 dex 也能调用。
 *
 * 关键作用：把 Android 的 Surface 交给 JVM 侧的 GLFW/EGL 后端，
 * 让 Minecraft 真正画到 SurfaceView 上（而不是另起进程拿不到窗口）。
 */
public final class JREUtils {
    private JREUtils() {}

    static {
        // libexithook 的 JNI_OnLoad 依赖 Pojav 自身的 Java 类，加载即返回 JNI_ERR；
        // 我们只需要 pojavexec 里的 JVM 启动 / 窗口绑定能力，所以只加载它。
        System.loadLibrary("pojavexec");
    }

    /** 切换到工作目录（MC 的资源、日志、存档都相对这里）。 */
    public static native int chdir(String path);

    /** 加载一个 so（用于把 JRE 的 libjvm / libjava 等预加载进进程）。 */
    public static native boolean dlopen(String libPath);

    /** 设置 LD_LIBRARY_PATH（JVM 内部找 libjli / libnet 等靠它）。 */
    public static native void setLdLibraryPath(String ldLibraryPath);

    /** 绑定渲染窗口：GLFW 的 Android 后端从这里拿 ANativeWindow（传 Surface 对象）。 */
    public static native void setupBridgeWindow(Object surface);

    /** 解绑窗口（退出游戏时调用）。 */
    public static native void releaseBridgeWindow();

    public static native void setupExitMethod(Context context);

}
