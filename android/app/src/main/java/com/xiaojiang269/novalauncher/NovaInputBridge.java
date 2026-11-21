package com.xiaojiang269.novalauncher;

import org.lwjgl.system.JNI;
import org.lwjgl.system.linux.DynamicLinkLoader;

/**
 * JVM 侧的输入读取线程：tail files/nova-input.txt，逐行翻译成 GLFW 输入栈事件。
 * 这个类被打进 nova-boot.jar，和 CallbackBridge 在同一个类加载器里，
 * 所以能直接调到 libpojavexec 注册的 native。
 */
public final class NovaInputBridge {

    public static void start() {
        final String path = System.getProperty("nova.input.file");
        if (path == null) return;
        Thread t = new Thread(new Runnable() {
            @Override public void run() {
                long pos = 0;
                StringBuilder pending = new StringBuilder();
                while (true) {
                    try {
                        java.io.RandomAccessFile f = new java.io.RandomAccessFile(path, "r");
                        long len = f.length();
                        if (len > pos) {
                            f.seek(pos);
                            byte[] buf = new byte[(int) (len - pos)];
                            f.readFully(buf);
                            pos = len;
                            pending.append(new String(buf, "UTF-8"));
                        }
                        f.close();
                        int nl;
                        while ((nl = pending.indexOf("\n")) >= 0) {
                            handle(pending.substring(0, nl).trim());
                            pending.delete(0, nl + 1);
                        }
                    } catch (Throwable e) {
                        // 文件还没建 / 被截断，等下一轮
                    }
                    try { Thread.sleep(12); } catch (InterruptedException ignored) { }
                }
            }
        }, "nova-input-bridge");
        t.setDaemon(true);
        t.start();
        System.out.println("[Nova] input bridge tailing " + path);
    }

    private static long addrKey, addrMouse, addrCursor;

    /** 从已加载的 libpojavexec.so 里取 critical_* 裸函数地址。 */
    public static boolean init() {
        try {
            String libPath = System.getProperty("org.lwjgl.librarypath");
            String so = (libPath == null ? "libpojavexec.so" : libPath + "/libpojavexec.so");
            long h = DynamicLinkLoader.dlopen(so, DynamicLinkLoader.RTLD_LAZY);
            if (h == 0) h = DynamicLinkLoader.dlopen("libpojavexec.so", DynamicLinkLoader.RTLD_LAZY);
            if (h == 0) { System.out.println("[Nova] dlopen pojavexec failed: " + DynamicLinkLoader.dlerror()); return false; }
            addrKey = DynamicLinkLoader.dlsym(h, "critical_send_key");
            addrMouse = DynamicLinkLoader.dlsym(h, "critical_send_mouse_button");
            addrCursor = DynamicLinkLoader.dlsym(h, "critical_send_cursor_pos");
            long addrStack = DynamicLinkLoader.dlsym(h, "critical_set_stackqueue");
            if (addrStack != 0) JNI.callV(1, addrStack);
            System.out.println("[Nova] fn addr key=" + addrKey + " mouse=" + addrMouse + " cursor=" + addrCursor + " stack=" + addrStack);
            return addrKey != 0 && addrMouse != 0 && addrCursor != 0;
        } catch (Throwable t) {
            System.out.println("[Nova] fn init failed: " + t);
            return false;
        }
    }

    private static float lastX = -1, lastY = -1;

    private static void handle(String line) {
        if (line.isEmpty()) return;
        String[] p = line.split(" ");
        if (p.length < 3) return;
        String cmd = p[0];
        try {
            if ("K".equals(cmd)) {
                int key = Integer.parseInt(p[1]);
                int action = Integer.parseInt(p[2]);
                // libpojavexec 里的 critical_send_key 是 4 个 int 的裸函数（无 env/jclass），
                // CallbackBridge 的 nativeSendKey 在 JVM 类加载器里没被 RegisterNatives 绑上，
                // 所以直接用 JNI.callV 调符号，绕开 Java native 绑定。
                if (addrKey == 0) throw new IllegalStateException("critical_send_key unresolved");
                JNI.callV(key, 0, action, 0, addrKey);
            } else if ("M".equals(cmd)) {
                int button = Integer.parseInt(p[1]);
                int action = Integer.parseInt(p[2]);
                if (addrMouse == 0) throw new IllegalStateException("critical_send_mouse_button unresolved");
                JNI.callV(button, action, 0, addrMouse);
            } else if ("C".equals(cmd)) {
                float dx = Float.parseFloat(p[1]);
                float dy = Float.parseFloat(p[2]);
                if (addrCursor == 0) throw new IllegalStateException("critical_send_cursor_pos unresolved");
                JNI.callV(dx * 4f, dy * 4f, addrCursor);
            }
        } catch (Throwable e) {
            if (errCount++ < 20) System.out.println("[Nova] input err: " + e);
        }
    }

    private static int errCount = 0;

    private static float clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
}
