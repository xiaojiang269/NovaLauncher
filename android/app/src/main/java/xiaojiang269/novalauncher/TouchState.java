package com.xiaojiang269.novalauncher;

/**
 * 触屏输入快照：纯 Java，不依赖 Android。
 * 输入层写，运行时（GameRuntime）读，两边解耦。
 */
public final class TouchState {
    public static volatile boolean w, a, s, d;
    public static volatile boolean space, shift, ctrl;
    public static volatile boolean lmb, rmb;
    public static volatile boolean esc, f3, chat, inventory, drop, enter, backspace;
    public static volatile boolean typing;
    public static volatile boolean cursor;
    public static volatile int hotbar = 0;
    public static volatile float lookDx, lookDy;
}
