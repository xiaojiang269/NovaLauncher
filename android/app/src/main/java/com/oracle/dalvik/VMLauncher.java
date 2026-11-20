package com.oracle.dalvik;

/** JNI 入口由 Pojav 的 libpojavexec.so 提供：在同进程内创建 JVM 并调用主类。 */
public final class VMLauncher {
    private VMLauncher() {}

    /** args[0] 必须是 "java"，其后是 JVM 参数、主类、程序参数。返回 JVM 退出码。 */
    public static native int launchJVM(String[] args);
}
