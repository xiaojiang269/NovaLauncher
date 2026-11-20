package net.kdt.pojavlaunch;

/** libpojavexec 会在这个类上注册 critical native，占位即可。 */
public final class CriticalNativeTest {
    public static native void testCriticalNative();
    public static native int testError();
}
