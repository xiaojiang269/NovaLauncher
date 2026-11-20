package com.xiaojiang269.novalauncher;

/**
 * 运行时事件管线：把触屏输入层的按键状态，翻译成 Minecraft 运行时要吃的键盘/鼠标事件。
 *
 * 这一层不依赖 Android，也不依赖图形后端，可以单独跑（下方 main 就是自检）。
 * 之后接入 JDK-for-Android + LWJGL 时，运行时每帧调用 poll() 取走事件即可，
 * 键码用的是 GLFW 的编号，Minecraft 的 Keyboard/Mouse 处理器认这套。
 *
 * 键码取自 GLFW：ESC 256、SPACE 32、LSHIFT 340、字母为大写 ASCII、F3 292。
 */
public final class GameRuntime {

    // ---- GLFW 键码 ----
    public static final int K_ESC = 256;
    public static final int K_SPACE = 32;
    public static final int K_SHIFT = 340;
    public static final int K_F3 = 292;
    public static final int K_ENTER = 257;
    public static final int K_BACKSPACE = 259;
    public static final int K_E = 69;
    public static final int K_Q = 81;
    public static final int K_T = 84;
    public static final int K_W = 87;
    public static final int K_A = 65;
    public static final int K_S = 83;
    public static final int K_D = 68;

    public static final int MOUSE_LEFT = 0;
    public static final int MOUSE_RIGHT = 1;

    /** 一条待投递的事件。 */
    public static final class Event {
        public static final int KEY = 0;
        public static final int MOUSE_BUTTON = 1;
        public static final int MOUSE_MOVE = 2;
        public static final int HOTBAR = 3;
        public static final int CHAR = 4;

        public final int type;
        public final int code;
        public final boolean pressed;
        public final float dx, dy;

        Event(int type, int code, boolean pressed, float dx, float dy) {
            this.type = type;
            this.code = code;
            this.pressed = pressed;
            this.dx = dx;
            this.dy = dy;
        }

        @Override
        public String toString() {
            switch (type) {
                case KEY:
                    return "KEY " + code + (pressed ? " down" : " up");
                case MOUSE_BUTTON:
                    return "MOUSE " + (code == MOUSE_LEFT ? "LMB" : "RMB") + (pressed ? " down" : " up");
                case MOUSE_MOVE:
                    return "MOVE dx=" + dx + " dy=" + dy;
                case HOTBAR:
                    return "HOTBAR " + code;
                default:
                    return "CHAR " + (char) code;
            }
        }
    }

    private final java.util.ArrayDeque<Event> queue = new java.util.ArrayDeque<Event>();

    // 上一帧的快照，用来算「状态变化」
    private boolean[] prevKey = new boolean[512];
    private boolean prevLmb, prevRmb;
    private int prevHotbar = -1;
    private float lookAccX, lookAccY;

    /** 每帧调用一次：读输入快照，产出增量事件。 */
    public void poll() {
        edge(GameRuntimeKeys.ESC, TouchState.esc);
        edge(GameRuntimeKeys.F3, TouchState.f3);
        edge(GameRuntimeKeys.T, TouchState.chat);
        edge(GameRuntimeKeys.ENTER, TouchState.enter);
        edge(GameRuntimeKeys.BACKSPACE, TouchState.backspace);
        edge(GameRuntimeKeys.E, TouchState.inventory);
        edge(GameRuntimeKeys.Q, TouchState.drop);
        edge(GameRuntimeKeys.W, TouchState.w);
        edge(GameRuntimeKeys.A, TouchState.a);
        edge(GameRuntimeKeys.S, TouchState.s);
        edge(GameRuntimeKeys.D, TouchState.d);
        edge(GameRuntimeKeys.SPACE, TouchState.space);
        edge(GameRuntimeKeys.SHIFT, TouchState.shift);

        if (TouchState.lmb != prevLmb) {
            prevLmb = TouchState.lmb;
            queue.add(new Event(Event.MOUSE_BUTTON, MOUSE_LEFT, prevLmb, 0, 0));
        }
        if (TouchState.rmb != prevRmb) {
            prevRmb = TouchState.rmb;
            queue.add(new Event(Event.MOUSE_BUTTON, MOUSE_RIGHT, prevRmb, 0, 0));
        }
        if (TouchState.hotbar != prevHotbar) {
            prevHotbar = TouchState.hotbar;
            queue.add(new Event(Event.HOTBAR, prevHotbar, true, 0, 0));
        }

        // 视角增量：累加后按帧投递，避免一次拖动产生上百条事件
        lookAccX += TouchState.lookDx;
        lookAccY += TouchState.lookDy;
        TouchState.lookDx = 0;
        TouchState.lookDy = 0;
        if (Math.abs(lookAccX) > 0.5f || Math.abs(lookAccY) > 0.5f) {
            queue.add(new Event(Event.MOUSE_MOVE, 0, true, lookAccX, lookAccY));
            lookAccX = 0;
            lookAccY = 0;
        }
    }

    private void edge(int code, boolean now) {
        if (code < 0 || code >= prevKey.length) return;
        if (now != prevKey[code]) {
            prevKey[code] = now;
            queue.add(new Event(Event.KEY, code, now, 0, 0));
        }
    }

    public boolean hasNext() {
        return !queue.isEmpty();
    }

    public Event next() {
        return queue.poll();
    }

    public void clear() {
        queue.clear();
        java.util.Arrays.fill(prevKey, false);
        prevLmb = prevRmb = false;
        prevHotbar = -1;
        lookAccX = lookAccY = 0;
    }

    // 键码常量单独放一个类，避免与 Activity 里的字段命名冲突
    static final class GameRuntimeKeys {
        static final int ESC = K_ESC, F3 = K_F3, T = K_T, ENTER = K_ENTER;
        static final int BACKSPACE = K_BACKSPACE, E = K_E, Q = K_Q;
        static final int W = K_W, A = K_A, S = K_S, D = K_D;
        static final int SPACE = K_SPACE, SHIFT = K_SHIFT;
    }

    /** 自检：不接 Android 也能跑，验证状态变化是否按预期产出事件。 */
    public static void main(String[] args) {
        GameRuntime rt = new GameRuntime();
        StringBuilder log = new StringBuilder();
        log.append("== 帧 1：按住 W + 攻击 ==\n");
        TouchState.w = true;
        TouchState.lmb = true;
        TouchState.lookDx = 12;
        rt.poll();
        drain(rt, log);

        log.append("== 帧 2：状态不变，应无事件 ==\n");
        rt.poll();
        drain(rt, log);

        log.append("== 帧 3：松开 W，按 E 切到第 3 格 ==\n");
        TouchState.w = false;
        TouchState.lmb = false;
        TouchState.inventory = true;
        TouchState.hotbar = 2;
        rt.poll();
        drain(rt, log);

        log.append("== 帧 4：全部松开 ==\n");
        TouchState.inventory = false;
        rt.poll();
        drain(rt, log);
        System.out.println(log);
    }

    private static void drain(GameRuntime rt, StringBuilder sb) {
        if (!rt.hasNext()) {
            sb.append("（无事件）\n");
            return;
        }
        while (rt.hasNext()) sb.append("  ").append(rt.next()).append('\n');
    }
}
