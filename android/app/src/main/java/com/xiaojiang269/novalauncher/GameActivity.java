package com.xiaojiang269.novalauncher;

import android.app.Activity;
import android.content.Context;
import android.content.SharedPreferences;
import android.graphics.Canvas;
import android.graphics.LinearGradient;
import android.graphics.Paint;
import android.graphics.Path;
import android.graphics.RectF;
import android.graphics.Shader;
import android.graphics.Typeface;
import android.os.Build;
import android.os.Bundle;
import android.opengl.GLES20;
import android.opengl.GLSurfaceView;
import android.util.Log;

import javax.microedition.khronos.egl.EGLConfig;
import javax.microedition.khronos.opengles.GL10;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowManager;

import java.io.File;
import java.util.ArrayList;

/**
 * 游戏容器 + PC 键位映射的触屏输入层。
 *
 * 屏幕上每个控件都是一个「电脑按键」，按下即按住对应键盘键 / 鼠标键。
 * 左侧 WASD 八向，顶部 Esc/F3/T/Input/Enter/BackSp，右侧跳跃/攻击/放置/潜行/E/Q，
 * 右上角光标模式。输入快照收口到 Input，Java 运行时接进来后直接读它生成事件。
 */
public class GameActivity extends Activity {

    private WorldView world;

    /** GPU 自检结果，供输入层状态栏显示。 */
    public static volatile String GL_INFO = "";

    /**
     * 真实 GPU 自检：用框架自带的 GLSurfaceView 建 EGL surface + 上下文，
     * 拿 GL_VERSION / RENDERER / VENDOR 并清屏一次。这一步能证明设备上的
     * EGL + GLES 通路是通的，为后续把 LWJGL 的窗口后端接到这个 surface 打底。
     */
    private void startGlProbe() {
        final GLSurfaceView gl = new GLSurfaceView(this);
        gl.setEGLContextClientVersion(2);
        gl.setRenderer(new GLSurfaceView.Renderer() {
            public void onSurfaceCreated(GL10 unused, EGLConfig cfg) {
                try {
                    String ver = GLES20.glGetString(GLES20.GL_VERSION);
                    String ren = GLES20.glGetString(GLES20.GL_RENDERER);
                    String ven = GLES20.glGetString(GLES20.GL_VENDOR);
                    String sl = GLES20.glGetString(GLES20.GL_SHADING_LANGUAGE_VERSION);
                    GL_INFO = "GL_VERSION=" + ver + " | RENDERER=" + ren + " | VENDOR=" + ven
                            + " | GLSL=" + sl;
                    Log.i("NovaGL", GL_INFO);
                } catch (Throwable t) {
                    GL_INFO = "GL 自检异常：" + t;
                    Log.e("NovaGL", "probe fail", t);
                }
            }
            public void onSurfaceChanged(GL10 unused, int w, int h) {
                GLES20.glViewport(0, 0, w, h);
            }
            public void onDrawFrame(GL10 unused) {
                GLES20.glClearColor(0.05f, 0.11f, 0.16f, 1f);
                GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT);
            }
        });
        setContentView(gl, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        gl.postDelayed(new Runnable() {
            public void run() {
                showWorld();
            }
        }, 2600);
    }

    private void showWorld() {
        world = new WorldView(this);
        setContentView(world, new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        world.startRuntime(getApplicationContext());
    }

    // ===== 真正出画面：SurfaceView + 同进程 JVM =====
    private android.view.SurfaceView gameSurface;
    private android.widget.FrameLayout stage;
    private volatile boolean inlineStarted = false;
    private volatile boolean inlineReady = false;

    private void startInline() {
        stage = new android.widget.FrameLayout(this);
        stage.setBackgroundColor(0xFF000000);

        gameSurface = new android.view.SurfaceView(this);
        gameSurface.setZOrderMediaOverlay(false);
        stage.addView(gameSurface, new android.widget.FrameLayout.LayoutParams(-1, -1));

        world = new WorldView(this);
        world.fakeWorld = false;            // 不再画占位世界，真画面来自 Surface
        stage.addView(world, new android.widget.FrameLayout.LayoutParams(-1, -1));
        setContentView(stage);

        final String ver = getIntent() == null ? "1.20.1"
                : getIntent().getStringExtra("version");
        final String user = getIntent() == null ? "Player"
                : getIntent().getStringExtra("user");
        final int mem = getIntent() == null ? 2048 : getIntent().getIntExtra("mem", 2048);
        final String fver = ver == null ? "1.20.1" : ver;
        final String fuser = user == null ? "Player" : user;

        gameSurface.getHolder().addCallback(new android.view.SurfaceHolder.Callback() {
            public void surfaceCreated(android.view.SurfaceHolder h) {
                if (inlineStarted) return;
                inlineStarted = true;
                final android.view.Surface surf = h.getSurface();
                try { org.lwjgl.glfw.CallbackBridge.setContext(getApplicationContext()); } catch (Throwable t) { }
                InputPump.start(getFilesDir());
                new Thread(new Runnable() {
                    public void run() {
                        world.rtStatus = "正在准备 Java / 游戏文件 …";
                        GameLaunchInline.launch(GameActivity.this.getApplicationContext(),
                                surf, fver, fuser, mem, new GameLaunchInline.Log() {
                                    public void line(String s) {
                                        world.rtLine(s);
                                        Log.i("NovaRT", s);
                                    }
                                });
                        world.rtStatus = "JVM 已退出";
                        inlineReady = false;
                    }
                }, "mc-jvm").start();
            }
            public void surfaceChanged(android.view.SurfaceHolder h, int f, int w, int ht) { }
            public void surfaceDestroyed(android.view.SurfaceHolder h) { }
        });
    }

    @Override
    protected void onCreate(Bundle b) {
        super.onCreate(b);
        getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN,
                WindowManager.LayoutParams.FLAG_FULLSCREEN);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        if (getIntent() != null && getIntent().getBooleanExtra("skipGlProbe", false)) {
            showWorld();
        } else {
            startInline();
        }
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (Build.VERSION.SDK_INT >= 19) {
            getWindow().getDecorView().setSystemUiVisibility(
                    View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                            | View.SYSTEM_UI_FLAG_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                            | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                            | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION);
        }
        if (world != null) world.resume();
    }

    @Override
    protected void onPause() {
        if (world != null) world.pause();
        super.onPause();
    }

    @Override
    public boolean onKeyDown(int keyCode, KeyEvent e) {
        if (keyCode == KeyEvent.KEYCODE_BACK) {
            finish();
            return true;
        }
        return super.onKeyDown(keyCode, e);
    }


    // ================= 视图 =================
    public static class WorldView extends View {

        private final Paint p = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint txt = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Paint fill = new Paint(Paint.ANTI_ALIAS_FLAG);
        private final Path path = new Path();

        private float yaw = 32f, pitch = 8f;
        private double camX = 0, camZ = 0;
        private long lastFrame = 0;
        private boolean running = true;
        private float bob = 0;

        /** false = 真画面由 Surface 提供，不再画占位世界。 */
        boolean fakeWorld = true;

        private int lookPtr = -1;
        private float lookLastX, lookLastY;

        private int lastW = -1, lastH = -1;
        private boolean laidOut = false;
        private float horizon = 0;

        /** 一个按键盘位控件。 */
        static final class Key {
            String label;
            String keyName;
            RectF r = new RectF();
            int shape;   // 0 圆角矩形 1 三角 2 圆
            int dir;     // 三角方向 0上 1下 2左 3右
            boolean down;
            int pointer = -1;
        }

        private final ArrayList<Key> keys = new ArrayList<Key>();

        private final RectF hotbar = new RectF();
        private final int[] items = new int[]{0x9BBF6A, 0x8C6239, 0x3B6E8F, 0xC9C3B4, 0x9BBF6A,
                0x8C6239, 0x3B6E8F, 0xB0563C, 0xD8D2C0};
        private int selected = 0;
        private boolean panel = false;
        private final float[] vals = new float[]{1.0f, 1.0f, 1.0f};
        private final RectF[] sliders = new RectF[3];
        private int sliderPtr = -1;

        // ---- 布局编辑器状态 ----
        private boolean editMode = false;
        private Key dragKey = null;
        private int dragPtr = -1;
        private float dragDx = 0, dragDy = 0;
        private Key sizeKey = null;
        private int sizePtr = -1;
        private float sizeStartDist = 1f, sizeStartW = 1f, sizeStartH = 1f;
        private final java.util.HashMap<String, float[]> over = new java.util.HashMap<String, float[]>();
        private final RectF editDone = new RectF();
        private final RectF editReset = new RectF();
        private final RectF editBtn = new RectF();
        private final Paint dash = new Paint(Paint.ANTI_ALIAS_FLAG);

        // ---- 运行时（JRE）状态 ----
        private final StringBuilder rtLog = new StringBuilder();
        private volatile String rtStatus = "运行时：未初始化";
        private void appendGlInfo() {
            if (GL_INFO != null && GL_INFO.length() > 0) rtLine("[GPU] " + GL_INFO);
        }
        private volatile boolean rtReady = false;
        private volatile boolean rtBusy = false;
        private boolean rtShow = true;

        public WorldView(Context c) {
            super(c);
            setFocusable(true);
            txt.setColor(0xFFFFFFFF);
            txt.setTypeface(Typeface.DEFAULT_BOLD);
            lastFrame = System.currentTimeMillis();
            loadOverrides();
        }

        // ================= 布局持久化与编辑器 =================
        private SharedPreferences prefs() {
            return getContext().getSharedPreferences("nova_touch_layout", Context.MODE_PRIVATE);
        }

        private void loadOverrides() {
            over.clear();
            String raw = prefs().getString("layout", "");
            if (raw == null || raw.isEmpty()) return;
            for (String line : raw.split("\n")) {
                String[] f = line.split(";");
                if (f.length < 5) continue;
                try {
                    over.put(f[0], new float[]{Float.parseFloat(f[1]), Float.parseFloat(f[2]),
                            Float.parseFloat(f[3]), Float.parseFloat(f[4])});
                } catch (Exception ignore) {
                }
            }
        }

        private void saveOverrides() {
            StringBuilder sb = new StringBuilder();
            for (java.util.Map.Entry<String, float[]> e : over.entrySet()) {
                float[] v = e.getValue();
                if (sb.length() > 0) sb.append('\n');
                sb.append(e.getKey()).append(';').append(v[0]).append(';').append(v[1])
                        .append(';').append(v[2]).append(';').append(v[3]);
            }
            prefs().edit().putString("layout", sb.toString()).apply();
        }

        private void applyOverrides(int w, int h, float u) {
            for (int i = 0; i < keys.size(); i++) {
                Key k = keys.get(i);
                float[] o = over.get(k.keyName + "#" + i);
                if (o == null) continue;
                float nw = o[2] * u, nh = o[3] * u;
                float cx = o[0] * w, cy = o[1] * h;
                k.r.set(cx - nw / 2f, cy - nh / 2f, cx + nw / 2f, cy + nh / 2f);
            }
        }

        private void storeKey(Key k) {
            int idx = keys.indexOf(k);
            if (idx < 0) return;
            float u = Math.min(getWidth(), getHeight());
            over.put(k.keyName + "#" + idx, new float[]{
                    k.r.centerX() / getWidth(), k.r.centerY() / getHeight(),
                    k.r.width() / u, k.r.height() / u});
        }

        private void resetLayout() {
            over.clear();
            prefs().edit().remove("layout").apply();
            laidOut = false;
            lastW = -1;
            lastH = -1;
            layoutHud();
            invalidate();
        }

        void resume() {
            running = true;
            lastFrame = System.currentTimeMillis();
            invalidate();
        }

        void pause() {
            running = false;
        }

        // ---------- 键位布局 ----------
        private Key add(String label, String keyName, float cx, float cy, float size) {
            Key k = new Key();
            k.label = label;
            k.keyName = keyName;
            k.shape = 0;
            k.r.set(cx - size / 2f, cy - size / 2f, cx + size / 2f, cy + size / 2f);
            keys.add(k);
            return k;
        }

        private void addWide(String label, String keyName, float cx, float cy, float w, float h) {
            Key k = new Key();
            k.label = label;
            k.keyName = keyName;
            k.shape = 0;
            k.r.set(cx - w / 2f, cy - h / 2f, cx + w / 2f, cy + h / 2f);
            keys.add(k);
        }

        private void addArrow(int dir, float cx, float cy, float size) {
            Key k = new Key();
            k.label = "";
            k.keyName = new String[]{"W", "S", "A", "D"}[dir];
            k.shape = 1;
            k.dir = dir;
            k.r.set(cx - size / 2f, cy - size / 2f, cx + size / 2f, cy + size / 2f);
            keys.add(k);
        }

        private void layoutHud() {
            int w = getWidth(), h = getHeight();
            if (w == 0 || h == 0) return;
            if (laidOut && Math.abs(w - lastW) < 2 && Math.abs(h - lastH) < 2) return;
            lastW = w;
            lastH = h;
            laidOut = true;
            keys.clear();

            float u = Math.min(w, h);
            float bs = u * 0.075f * vals[0];
            float top = h * 0.115f;

            // 顶部功能键
            add("Esc", "ESC", w * 0.030f + bs / 2f, top, bs);
            add("F3", "F3", w * 0.030f + bs * 2f, top, bs);
            add("T", "T", w * 0.030f + bs * 3f, top, bs);
            addWide("Input", "INPUT", w * 0.290f, top, bs * 1.9f, bs);
            addWide("Enter", "ENTER", w * 0.390f, top, bs * 1.6f, bs);
            addWide("BackSp", "BACKSPACE", w * 0.480f, top, bs * 1.9f, bs);

            // 右上角光标模式
            Key cur = add("", "CURSOR", w * 0.955f, top, bs * 0.9f);
            cur.shape = 2;

            // 左侧 WASD 八向
            float pcx = w * 0.115f, pcy = h * 0.66f;
            float rad = u * 0.115f;
            float as = u * 0.070f;
            addArrow(0, pcx, pcy - rad, as);
            addArrow(1, pcx, pcy + rad, as);
            addArrow(2, pcx - rad, pcy, as);
            addArrow(3, pcx + rad, pcy, as);
            float d = rad * 0.70f;
            float ds = as * 0.46f;
            addArrow(0, pcx + d, pcy - d, ds);
            addArrow(0, pcx - d, pcy - d, ds);
            addArrow(1, pcx + d, pcy + d, ds);
            addArrow(1, pcx - d, pcy + d, ds);

            // 右侧动作区
            addWide("跳跃\nSpace", "SPACE", w * 0.845f, h * 0.62f, bs * 1.75f, bs * 1.30f);
            add("攻击\nLMB", "LMB", w * 0.925f, h * 0.775f, bs * 1.15f);
            add("放置\nRMB", "RMB", w * 0.845f, h * 0.855f, bs * 1.10f);
            add("潜行\nShift", "SHIFT", w * 0.945f, h * 0.905f, bs * 1.05f);
            add("E", "E", w * 0.755f, h * 0.885f, bs * 1.0f);
            add("Q", "Q", w * 0.955f, h * 0.455f, bs * 0.95f);
            add("设置", "SETTINGS", w * 0.955f, h * 0.265f, bs * 0.95f);

            applyOverrides(w, h, u);

            // 快捷栏
            float hbH = u * 0.070f * vals[0];
            float hbW = Math.min(w * 0.42f, hbH * 9.6f);
            hotbar.set(w / 2f - hbW / 2f, h - hbH - h * 0.045f,
                    w / 2f + hbW / 2f, h - h * 0.045f);

            for (int i = 0; i < 3; i++) {
                float sy = h * 0.20f + i * (h * 0.085f);
                sliders[i] = new RectF(w * 0.32f, sy, w * 0.68f, sy + h * 0.040f);
            }
            horizon = h * 0.42f;

            // 设置面板里的「编辑键位布局」按钮 + 编辑态顶部按钮
            editBtn.set(w * 0.32f, h * 0.60f, w * 0.68f, h * 0.685f);
            editDone.set(w * 0.60f, h * 0.035f, w * 0.71f, h * 0.10f);
            editReset.set(w * 0.735f, h * 0.035f, w * 0.875f, h * 0.10f);
        }

        @Override
        protected void onSizeChanged(int w, int h, int ow, int oh) {
            super.onSizeChanged(w, h, ow, oh);
            lastW = -1;
            lastH = -1;
            laidOut = false;
            layoutHud();
        }

        @Override
        protected void onDraw(Canvas c) {
            layoutHud();
            long now = System.currentTimeMillis();
            float dt = Math.min(0.05f, (now - lastFrame) / 1000f);
            lastFrame = now;
            step(dt);
            // 占位世界模式下才画自己的快捷栏/血量；真机跑起来后由 MC 自带的 HUD 显示，
            // 再叠一层会重复（用户要求：删掉下方任务栏，用游戏自带的）
            if (fakeWorld) { drawSky(c); drawWorld(c); drawHotbarAndBars(c); }
            drawKeys(c);
            if (cursorMode()) drawCursor(c);
            drawStatus(c);
            drawBetaTag(c);
            if (editMode) drawEditOverlay(c);
            // 运行时进度不再盖在游戏画面上，改到「设置」面板里显示
            if (panel) drawRuntimeInfo(c);
            if (panel) drawPanel(c);
            if (running) postInvalidateDelayed(16);
        }

        private boolean cursorMode() {
            for (Key k : keys) if ("CURSOR".equals(k.keyName) && k.down) return true;
            return false;
        }

        private void step(float dt) {
            if (editMode) {
                for (Key k : keys) {
                    k.down = false;
                    k.pointer = -1;
                }
                TouchState.w = TouchState.a = TouchState.s = TouchState.d = false;
                TouchState.space = TouchState.shift = TouchState.lmb = TouchState.rmb = false;
                TouchState.esc = TouchState.f3 = TouchState.chat = TouchState.inventory = TouchState.drop = false;
                TouchState.cursor = false;
                return;
            }
            float mx = (down("D") ? 1 : 0) - (down("A") ? 1 : 0);
            float mz = (down("S") ? 1 : 0) - (down("W") ? 1 : 0);
            if (mx != 0 && mz != 0) {
                mx *= 0.707f;
                mz *= 0.707f;
            }
            float speed = 4.2f;
            float rad = (float) Math.toRadians(yaw);
            camX += (-Math.sin(rad) * (-mz) + Math.cos(rad) * mx) * speed * dt;
            camZ += (-Math.cos(rad) * (-mz) - Math.sin(rad) * mx) * speed * dt;
            bob += (Math.abs(mx) + Math.abs(mz)) * dt * 6f;

            TouchState.w = down("W");
            TouchState.a = down("A");
            TouchState.s = down("S");
            TouchState.d = down("D");
            TouchState.space = down("SPACE");
            TouchState.shift = down("SHIFT");
            TouchState.lmb = down("LMB");
            TouchState.rmb = down("RMB");
            TouchState.esc = down("ESC");
            TouchState.f3 = down("F3");
            TouchState.chat = down("T");
            TouchState.inventory = down("E");
            TouchState.drop = down("Q");
            TouchState.enter = down("ENTER");
            TouchState.backspace = down("BACKSPACE");
            TouchState.typing = down("INPUT");
            TouchState.cursor = cursorMode();
            TouchState.hotbar = selected;
        }

        private boolean down(String name) {
            for (Key k : keys) if (name.equals(k.keyName) && k.down) return true;
            return false;
        }

        // ---------- 场景占位渲染 ----------
        private void drawSky(Canvas c) {
            int h = getHeight();
            float top = horizon - h * 0.45f + (float) Math.toRadians(pitch) * h * 0.6f;
            p.setShader(new LinearGradient(0, top - h * 0.2f, 0, horizon + 20,
                    new int[]{0xFF7FB6E3, 0xFF9BC9E8, 0xFFCDE4F2}, null, Shader.TileMode.CLAMP));
            c.drawRect(0, 0, getWidth(), horizon + 40, p);
            p.setShader(null);
        }

        private static int hash(int x, int y) {
            int hh = x * 374761393 + y * 668265263;
            hh = (hh ^ (hh >> 13)) * 1274126177;
            return hh ^ (hh >> 16);
        }

        private void drawWorld(Canvas c) {
            int w = getWidth(), h = getHeight();
            int camBX = (int) Math.floor(camX), camBZ = (int) Math.floor(camZ);
            float yawOff = yaw / 62f;
            fill.setColor(0xFF6D8F4C);
            c.drawRect(0, horizon + h * 0.05f, w, h, fill);
            for (int d = 15; d >= 0; d--) {
                float scale = (16 - d);
                float bw = 8f + scale * 5.2f;
                float rowY = horizon + 10 + (16 - d) * (16 - d) * 2.1f
                        - (float) Math.toRadians(pitch) * 90f
                        + (float) Math.sin(bob) * 1.5f;
                int count = (int) Math.ceil(w / bw) + 4;
                float startBlock = camBZ - d - 2;
                float shift = -((camBX + startBlock) * bw * 0.55f) % bw - yawOff * bw * 0.9f;
                for (int i = 0; i < count; i++) {
                    int bx = (int) (camBX + i - count / 2 + startBlock);
                    int hs = hash(bx, (int) startBlock) & 7;
                    int base = hs == 0 ? 0xC9C3B4 : hs == 1 ? 0x3B6E8F : 0x9BBF6A;
                    float x = shift + i * bw;
                    float bh = bw * (0.55f + (hash(bx, (int) startBlock) & 3) * 0.12f);
                    fill.setColor(base);
                    c.drawRect(x, rowY - bh, x + bw * 0.96f, rowY, fill);
                    fill.setColor(light(base, 0.22f));
                    c.drawRect(x, rowY - bh, x + bw * 0.96f, rowY - bh + bh * 0.18f, fill);
                    fill.setColor(light(base, -0.30f));
                    c.drawRect(x, rowY - bh * 0.16f, x + bw * 0.96f, rowY, fill);
                }
            }
            p.setShader(new LinearGradient(0, horizon - 6, 0, horizon + h * 0.12f,
                    new int[]{0x00CDE4F2, 0x55CDE4F2, 0x00CDE4F2}, null, Shader.TileMode.CLAMP));
            c.drawRect(0, horizon - 6, w, horizon + h * 0.12f, p);
            p.setShader(null);
        }

        private static int light(int color, float f) {
            int r = Math.min(255, Math.max(0, (int) (((color >> 16) & 0xFF) * (1 + f))));
            int g = Math.min(255, Math.max(0, (int) (((color >> 8) & 0xFF) * (1 + f))));
            int b = Math.min(255, Math.max(0, (int) ((color & 0xFF) * (1 + f))));
            return 0xFF000000 | r << 16 | g << 8 | b;
        }

        // ---------- HUD ----------
        private void drawHotbarAndBars(Canvas c) {
            int w = getWidth(), h = getHeight();
            float u = Math.min(w, h);

            fill.setColor(0x99FFFFFF);
            c.drawRect(w / 2f - 1.4f, h / 2f - u * 0.022f, w / 2f + 1.4f, h / 2f + u * 0.022f, fill);
            c.drawRect(w / 2f - u * 0.022f, h / 2f - 1.4f, w / 2f + u * 0.022f, h / 2f + 1.4f, fill);

            float slot = hotbar.width() / 9f;
            fill.setColor(0x77000000);
            c.drawRect(hotbar, fill);
            for (int i = 0; i < 9; i++) {
                RectF r = new RectF(hotbar.left + i * slot + 3, hotbar.top + 3,
                        hotbar.left + (i + 1) * slot - 3, hotbar.bottom - 3);
                fill.setColor(0x44000000);
                c.drawRect(r, fill);
                fill.setColor(0xFF000000 | items[i]);
                float inset = r.width() * 0.17f;
                c.drawRect(r.left + inset, r.top + inset, r.right - inset, r.bottom - inset, fill);
                p.setStyle(Paint.Style.STROKE);
                p.setStrokeWidth(i == selected ? 3f : 1.1f);
                p.setColor(i == selected ? 0xFFFFFFFF : 0x55FFFFFF);
                c.drawRect(r, p);
                p.setStyle(Paint.Style.FILL);
            }

            float iconS = u * 0.028f;
            float hy = hotbar.top - iconS * 1.15f;
            for (int i = 0; i < 10; i++) {
                float x = hotbar.left + i * (iconS * 1.02f);
                fill.setColor(0xFF6B2B2B);
                c.drawRect(x, hy, x + iconS * 0.84f, hy + iconS * 0.78f, fill);
                fill.setColor(0xFFE04A46);
                c.drawRect(x, hy, x + iconS * 0.84f, hy + iconS * 0.32f, fill);
            }
            for (int i = 0; i < 10; i++) {
                float x = hotbar.right - (i + 1) * (iconS * 1.02f);
                fill.setColor(0xFF6A5124);
                c.drawRect(x, hy, x + iconS * 0.84f, hy + iconS * 0.78f, fill);
                fill.setColor(0xFFC79A45);
                c.drawRect(x, hy, x + iconS * 0.84f, hy + iconS * 0.32f, fill);
            }
        }

        private void drawKeys(Canvas c) {
            int w = getWidth(), h = getHeight();
            float u = Math.min(w, h);
            for (Key k : keys) {
                RectF r = k.r;
                boolean on = k.down;
                if (k.shape == 1) {
                    drawArrow(c, k, on);
                    continue;
                }
                if (k.shape == 2) {
                    fill.setColor(on ? 0xE6FFD9A0 : 0x33FFFFFF);
                    c.drawCircle(r.centerX(), r.centerY(), r.width() / 2f, fill);
                    p.setStyle(Paint.Style.STROKE);
                    p.setStrokeWidth(1.5f);
                    p.setColor(on ? 0xFFFFFFFF : 0x66FFFFFF);
                    c.drawCircle(r.centerX(), r.centerY(), r.width() / 2f, p);
                    p.setStyle(Paint.Style.FILL);
                    fill.setColor(on ? 0xFF1A1206 : 0xFFFFFFFF);
                    float s = r.width() * 0.26f;
                    c.drawRoundRect(new RectF(r.centerX() - s, r.centerY() - s * 1.3f,
                            r.centerX() + s, r.centerY() + s * 1.3f), s * 0.6f, s * 0.6f, fill);
                    continue;
                }
                fill.setColor(on ? 0xE6FFD9A0 : argb(0.46f * vals[2], 0x000000));
                c.drawRoundRect(r, 8, 8, fill);
                p.setStyle(Paint.Style.STROKE);
                p.setStrokeWidth(1.5f);
                p.setColor(on ? 0xFFFFFFFF : argb(0.50f * vals[2] + 0.15f, 0xFFFFFF));
                c.drawRoundRect(r, 8, 8, p);
                p.setStyle(Paint.Style.FILL);
                if (k.label == null || k.label.isEmpty()) continue;
                txt.setColor(on ? 0xFF1A1206 : 0xF2FFFFFF);
                txt.setTextAlign(Paint.Align.CENTER);
                String[] lines = k.label.split("\n");
                float fs = Math.max(u * 0.019f, Math.min(r.height() * 0.40f, r.width() * 0.30f));
                txt.setTextSize(fs);
                float total = lines.length * fs * 1.12f;
                float y0 = r.centerY() - total / 2f + fs * 0.84f;
                for (int i = 0; i < lines.length; i++) {
                    c.drawText(lines[i], r.centerX(), y0 + i * fs * 1.12f, txt);
                }
            }
        }

        private void drawArrow(Canvas c, Key k, boolean on) {
            RectF r = k.r;
            path.reset();
            float cx = r.centerX(), cy = r.centerY();
            float s = r.width() / 2f;
            switch (k.dir) {
                case 0:
                    path.moveTo(cx, cy - s);
                    path.lineTo(cx + s, cy + s * 0.8f);
                    path.lineTo(cx - s, cy + s * 0.8f);
                    break;
                case 1:
                    path.moveTo(cx, cy + s);
                    path.lineTo(cx + s, cy - s * 0.8f);
                    path.lineTo(cx - s, cy - s * 0.8f);
                    break;
                case 2:
                    path.moveTo(cx - s, cy);
                    path.lineTo(cx + s * 0.8f, cy + s);
                    path.lineTo(cx + s * 0.8f, cy - s);
                    break;
                default:
                    path.moveTo(cx + s, cy);
                    path.lineTo(cx - s * 0.8f, cy + s);
                    path.lineTo(cx - s * 0.8f, cy - s);
                    break;
            }
            path.close();
            fill.setColor(on ? 0xE6FFD9A0 : 0x66FFFFFF);
            c.drawPath(path, fill);
            p.setStyle(Paint.Style.STROKE);
            p.setStrokeWidth(1.2f);
            p.setColor(on ? 0xFFFFFFFF : 0x55FFFFFF);
            c.drawPath(path, p);
            p.setStyle(Paint.Style.FILL);
        }

        private static int argb(float a, int rgb) {
            int al = Math.min(255, Math.max(0, (int) (a * 255)));
            return al << 24 | (rgb & 0xFFFFFF);
        }

        private void drawCursor(Canvas c) {
            float x = getWidth() * 0.55f, y = getHeight() * 0.45f;
            p.setStyle(Paint.Style.STROKE);
            p.setStrokeWidth(2.4f);
            p.setColor(0xFFFFFFFF);
            c.drawCircle(x, y, 14, p);
            c.drawLine(x - 22, y, x - 8, y, p);
            c.drawLine(x + 8, y, x + 22, y, p);
            p.setStyle(Paint.Style.FILL);
        }

        private void drawStatus(Canvas c) {
            int w = getWidth(), h = getHeight();
            float u = Math.min(w, h);
            StringBuilder sb = new StringBuilder();
            String[] order = {"W", "A", "S", "D", "SPACE", "SHIFT", "LMB", "RMB", "E", "Q", "T", "ESC", "F3"};
            for (String key : order) if (down(key)) sb.append(key).append(' ');
            if (sb.length() == 0) sb.append("无输入");
            txt.setTextAlign(Paint.Align.LEFT);
            txt.setTextSize(u * 0.026f);
            txt.setColor(0xFFFFFFFF);
            c.drawText("按下：" + sb.toString(), w * 0.028f, h * 0.055f, txt);
            txt.setTextSize(u * 0.019f);
            txt.setColor(0xCCFFFFFF);
            c.drawText(String.format("X %.1f  Z %.1f  YAW %.0f°  PITCH %.0f°   热键栏 %d",
                    camX, camZ, yaw, pitch, selected + 1), w * 0.028f, h * 0.055f + u * 0.030f, txt);
            txt.setTextAlign(Paint.Align.RIGHT);
            txt.setColor(0xAAFFFFFF);
            c.drawText("PC 键位映射 · 方向键移动，右侧留白拖动转视角", w * 0.972f, h * 0.055f, txt);
        }

        /** 左下角「测试版」角标。 */
        private void drawBetaTag(Canvas c) {
            int w = getWidth(), h = getHeight();
            float u = Math.min(w, h);
            float ts = u * 0.024f;
            txt.setTextAlign(Paint.Align.LEFT);
            txt.setTextSize(ts);
            String label = "测试版 " + VersionCatalog.APP_VERSION;
            float tw = txt.measureText(label);
            float left = w * 0.026f, bottom = h - h * 0.022f;
            fill.setColor(0xCC3A2A12);
            c.drawRoundRect(new RectF(left - u * 0.010f, bottom - ts * 1.5f,
                    left + tw + u * 0.010f, bottom + ts * 0.45f), 6, 6, fill);
            p.setStyle(Paint.Style.STROKE);
            p.setStrokeWidth(1f);
            p.setColor(0x66FFD98A);
            c.drawRoundRect(new RectF(left - u * 0.010f, bottom - ts * 1.5f,
                    left + tw + u * 0.010f, bottom + ts * 0.45f), 6, 6, p);
            p.setStyle(Paint.Style.FILL);
            txt.setColor(0xFFFFD98A);
            c.drawText(label, left, bottom, txt);
        }

        private void drawPanel(Canvas c) {
            int w = getWidth(), h = getHeight();
            fill.setColor(0xE60A0F0C);
            c.drawRect(w * 0.22f, h * 0.08f, w * 0.78f, h * 0.78f, fill);
            p.setStyle(Paint.Style.STROKE);
            p.setStrokeWidth(1.4f);
            p.setColor(0x44FFFFFF);
            c.drawRect(w * 0.22f, h * 0.08f, w * 0.78f, h * 0.78f, p);
            p.setStyle(Paint.Style.FILL);
            txt.setTextAlign(Paint.Align.LEFT);
            txt.setColor(0xFFFFFFFF);
            txt.setTextSize(h * 0.045f);
            c.drawText("触屏设置", w * 0.30f, h * 0.155f, txt);
            String[] names = {"按键大小", "视角灵敏度", "控件透明度"};
            for (int i = 0; i < 3; i++) {
                RectF r = sliders[i];
                txt.setTextSize(h * 0.028f);
                txt.setColor(0xE6FFFFFF);
                c.drawText(names[i], r.left, r.top - h * 0.008f, txt);
                fill.setColor(0x33FFFFFF);
                c.drawRoundRect(r, r.height() / 2, r.height() / 2, fill);
                fill.setColor(0xFFD9A05B);
                float fw = r.width() * (vals[i] - 0.5f) / 1.5f;
                c.drawRoundRect(new RectF(r.left, r.top, r.left + Math.max(r.height(), fw), r.bottom),
                        r.height() / 2, r.height() / 2, fill);
                txt.setColor(0xFFFFFFFF);
                txt.setTextAlign(Paint.Align.RIGHT);
                c.drawText(String.format("%.2f", vals[i]), r.right, r.top - h * 0.008f, txt);
                txt.setTextAlign(Paint.Align.LEFT);
            }
            // 编辑键位布局入口
            fill.setColor(0xCC2C3A2E);
            c.drawRoundRect(editBtn, 10, 10, fill);
            p.setStyle(Paint.Style.STROKE);
            p.setStrokeWidth(1.4f);
            p.setColor(0x77FFFFFF);
            c.drawRoundRect(editBtn, 10, 10, p);
            p.setStyle(Paint.Style.FILL);
            txt.setColor(0xFFFFFFFF);
            txt.setTextSize(h * 0.030f);
            txt.setTextAlign(Paint.Align.CENTER);
            c.drawText("编辑键位布局（拖动 / 右下角缩放）", editBtn.centerX(),
                    editBtn.centerY() + h * 0.010f, txt);
            txt.setTextAlign(Paint.Align.LEFT);

            // 运行时进度（原画面上那个「正在准备 Java / 游戏文件」提示框挪到这里）
            drawRuntimeInfo(c);

            txt.setColor(0x99FFFFFF);
            txt.setTextSize(h * 0.024f);
            c.drawText("点面板外的区域关闭", w * 0.30f, h * 0.72f, txt);
        }

        /** 后台解压 JRE 并试跑 java -version（不阻塞渲染）。 */
        void startRuntime(final Context ctx) {
            if (rtBusy) return;
            rtBusy = true;
            rtStatus = "运行时：正在准备 JRE 17 …";
            new Thread(new Runnable() {
                @Override
                public void run() {
                    rtLine("设备 ABI：" + android.os.Build.SUPPORTED_ABIS[0]);
                    if (!RuntimeBoot.isArm64()) {
                        rtStatus = "运行时：当前设备不是 arm64，暂不支持";
                        rtBusy = false;
                        postInvalidate();
                        return;
                    }
                    File java = RuntimeBoot.ensure(ctx, new RuntimeBoot.Log() {
                        public void line(String s) {
                            rtLine(s);
                        }
                    });
                    if (java == null) {
                        rtStatus = "运行时：解压失败";
                    } else {
                        boolean ok = RuntimeBoot.probe(java, new RuntimeBoot.Log() {
                            public void line(String s) {
                                rtLine(s);
                            }
                        });
                        rtReady = ok;
                        if (ok) {
                            rtStatus = "运行时：JRE 17 就绪，正在自检原生栈 …";
                            rtLine("开始原生栈自检（GL4ES / LWJGL / OpenAL）");
                            boolean nat = RuntimeBoot.probeNative(ctx, java, new RuntimeBoot.Log() {
                                public void line(String s) {
                                    rtLine(s);
                                }
                            });
                            rtStatus = nat ? "运行时：JRE 17 + LWJGL/GL4ES 已加载"
                                    : "运行时：JRE 就绪，图形栈未通过自检";
                        } else {
                            rtStatus = "运行时：java 启动失败";
                        }
                    }
                    rtBusy = false;
                    postInvalidate();
                }
            }, "runtime-boot").start();
        }

        private void rtLine(String s) {
            synchronized (rtLog) {
                rtLog.append(s).append('\n');
                if (rtLog.length() > 4000) rtLog.delete(0, rtLog.length() - 3000);
            }
            postInvalidate();
        }

        /** 设置面板里的运行时进度区：状态一行 + 最近几行日志。 */
        private void drawRuntimeInfo(Canvas c) {
            int w = getWidth(), h = getHeight();
            float u = Math.min(w, h);
            String[] lines;
            synchronized (rtLog) {
                String[] all = rtLog.toString().split("\n");
                int keep = Math.min(4, all.length);
                lines = new String[keep];
                System.arraycopy(all, all.length - keep, lines, 0, keep);
            }
            float left = w * 0.28f, top = h * 0.425f, right = w * 0.72f, bottom = h * 0.585f;
            fill.setColor(0x99101612);
            c.drawRoundRect(new RectF(left, top, right, bottom), 8, 8, fill);
            p.setStyle(Paint.Style.STROKE);
            p.setStrokeWidth(1f);
            p.setColor(0x33FFFFFF);
            c.drawRoundRect(new RectF(left, top, right, bottom), 8, 8, p);
            p.setStyle(Paint.Style.FILL);
            txt.setTextAlign(Paint.Align.LEFT);
            txt.setTextSize(h * 0.024f);
            txt.setColor(0xFF9FD8FF);
            c.drawText("Java 运行环境", left + u * 0.014f, top + h * 0.030f, txt);
            txt.setColor(rtReady ? 0xFF8FE38F : 0xFFFFD98A);
            c.drawText(rtStatus, left + u * 0.014f, top + h * 0.062f, txt);
            txt.setColor(0xCCFFFFFF);
            txt.setTextSize(h * 0.019f);
            for (int i = 0; i < lines.length; i++) {
                c.drawText(lines[i], left + u * 0.014f, top + h * 0.090f + h * 0.0215f * i, txt);
            }
        }

        private void drawRuntimePanel(Canvas c) {
            int w = getWidth(), h = getHeight();
            float u = Math.min(w, h);
            String[] lines;
            synchronized (rtLog) {
                String[] all = rtLog.toString().split("\n");
                int keep = Math.min(6, all.length);
                lines = new String[keep];
                System.arraycopy(all, all.length - keep, lines, 0, keep);
            }
            float boxH = h * 0.030f * (lines.length + 2);
            float left = w * 0.028f, top = h * 0.50f;
            fill.setColor(0xCC080D0A);
            c.drawRect(left, top, left + w * 0.40f, top + boxH, fill);
            txt.setTextAlign(Paint.Align.LEFT);
            txt.setTextSize(u * 0.021f);
            txt.setColor(rtReady ? 0xFF8FE38F : 0xFFFFD98A);
            c.drawText(rtStatus, left + u * 0.012f, top + h * 0.034f, txt);
            txt.setColor(0xCCFFFFFF);
            txt.setTextSize(u * 0.017f);
            for (int i = 0; i < lines.length; i++) {
                c.drawText(lines[i], left + u * 0.012f, top + h * 0.030f * (i + 1.6f), txt);
            }
        }

        private void drawEditOverlay(Canvas c) {
            int w = getWidth(), h = getHeight();
            float u = Math.min(w, h);

            // 顶部提示条
            fill.setColor(0xE6101510);
            c.drawRect(0, 0, w, h * 0.125f, fill);
            txt.setTextAlign(Paint.Align.LEFT);
            txt.setColor(0xFFFFFFFF);
            txt.setTextSize(u * 0.030f);
            c.drawText("编辑键位布局", w * 0.030f, h * 0.048f, txt);
            txt.setColor(0xCCFFFFFF);
            txt.setTextSize(u * 0.019f);
            c.drawText("拖动按键 = 移动位置 · 拖右下角方块 = 缩放 · 松开即保存", w * 0.030f, h * 0.088f, txt);

            // 完成 / 恢复默认
            fill.setColor(0xD9457A52);
            c.drawRoundRect(editDone, 10, 10, fill);
            fill.setColor(0xD9363C42);
            c.drawRoundRect(editReset, 10, 10, fill);
            txt.setTextAlign(Paint.Align.CENTER);
            txt.setColor(0xFFFFFFFF);
            txt.setTextSize(u * 0.024f);
            c.drawText("完成", editDone.centerX(), editDone.centerY() + u * 0.008f, txt);
            c.drawText("恢复默认", editReset.centerX(), editReset.centerY() + u * 0.008f, txt);
            txt.setTextAlign(Paint.Align.LEFT);

            // 每个按键：虚线框 + 右下角缩放手柄
            dash.setStyle(Paint.Style.STROKE);
            dash.setStrokeWidth(1.6f);
            dash.setColor(0xFFFFD98A);
            float hs = u * 0.020f;
            for (Key k : keys) {
                float[] o = over.get(k.keyName + "#" + keys.indexOf(k));
                boolean moved = o != null;
                dash.setColor(moved ? 0xFFFFD98A : 0x88FFFFFF);
                c.drawRect(k.r, dash);
                fill.setColor(0xFFFFD98A);
                c.drawRect(k.r.right - hs, k.r.bottom - hs, k.r.right, k.r.bottom, fill);
            }
        }

        // ---------- 触摸 ----------
        @Override
        public boolean onTouchEvent(MotionEvent e) {
            int action = e.getActionMasked();
            int idx = e.getActionIndex();
            int id = e.getPointerId(idx);
            float x = e.getX(idx), y = e.getY(idx);

            switch (action) {
                case MotionEvent.ACTION_DOWN:
                case MotionEvent.ACTION_POINTER_DOWN: {
                    if (editMode) {
                        if (editDone.contains(x, y)) {
                            saveOverrides();
                            editMode = false;
                            invalidate();
                            return true;
                        }
                        if (editReset.contains(x, y)) {
                            resetLayout();
                            return true;
                        }
                        float u = Math.min(getWidth(), getHeight());
                        float hs = u * 0.030f;
                        for (Key k : keys) {
                            RectF handle = new RectF(k.r.right - hs, k.r.bottom - hs, k.r.right, k.r.bottom);
                            if (k.r.contains(x, y) && handle.contains(x, y)) {
                                sizeKey = k;
                                sizePtr = id;
                                sizeStartDist = (float) Math.hypot(x - k.r.centerX(), y - k.r.centerY());
                                if (sizeStartDist < 1f) sizeStartDist = 1f;
                                sizeStartW = k.r.width();
                                sizeStartH = k.r.height();
                                return true;
                            }
                        }
                        for (Key k : keys) {
                            if (k.r.contains(x, y)) {
                                dragKey = k;
                                dragPtr = id;
                                dragDx = k.r.centerX() - x;
                                dragDy = k.r.centerY() - y;
                                return true;
                            }
                        }
                        return true;
                    }
                    if (panel) {
                        for (int i = 0; i < 3; i++) {
                            if (sliders[i] != null && sliders[i].contains(x, y)) {
                                sliderPtr = id;
                                setSlider(i, x);
                                return true;
                            }
                        }
                        if (editBtn.contains(x, y)) {
                            editMode = true;
                            panel = false;
                            laidOut = false;
                            lastW = -1;
                            lastH = -1;
                            layoutHud();
                            invalidate();
                            return true;
                        }
                        RectF box = new RectF(getWidth() * 0.22f, getHeight() * 0.08f,
                                getWidth() * 0.78f, getHeight() * 0.78f);
                        if (!box.contains(x, y)) panel = false;
                        invalidate();
                        return true;
                    }
                    for (Key k : keys) {
                        if (k.pointer >= 0) continue;
                        if (k.r.contains(x, y)) {
                            if ("SETTINGS".equals(k.keyName)) {
                                panel = true;
                                invalidate();
                                return true;
                            }
                            k.pointer = id;
                            k.down = true;
                            if ("E".equals(k.keyName)) selected = (selected + 1) % 9;
                            invalidate();
                            return true;
                        }
                    }
                    if (fakeWorld && hotbar.contains(x, y)) {
                        int slot = (int) Math.floor((x - hotbar.left) / (hotbar.width() / 9f));
                        selected = Math.max(0, Math.min(8, slot));
                        invalidate();
                        return true;
                    }
                    lookPtr = id;
                    lookLastX = x;
                    lookLastY = y;
                    return true;
                }
                case MotionEvent.ACTION_MOVE: {
                    if (editMode) {
                        if (dragKey != null) {
                            int di = e.findPointerIndex(dragPtr);
                            if (di >= 0) {
                                int w = getWidth(), h = getHeight();
                                float u = Math.min(w, h);
                                float cx = e.getX(di) + dragDx;
                                float cy = e.getY(di) + dragDy;
                                float hw = dragKey.r.width() / 2f, hh = dragKey.r.height() / 2f;
                                cx = Math.max(hw, Math.min(w - hw, cx));
                                cy = Math.max(hh, Math.min(h - hh, cy));
                                dragKey.r.set(cx - hw, cy - hh, cx + hw, cy + hh);
                                storeKey(dragKey);
                                invalidate();
                            }
                        } else if (sizeKey != null) {
                            int si = e.findPointerIndex(sizePtr);
                            if (si >= 0) {
                                float u = Math.min(getWidth(), getHeight());
                                float dist = (float) Math.hypot(e.getX(si) - sizeKey.r.centerX(),
                                        e.getY(si) - sizeKey.r.centerY());
                                float f = Math.max(0.35f, Math.min(2.6f, dist / sizeStartDist));
                                float nw = sizeStartW * f, nh = sizeStartH * f;
                                float cx = sizeKey.r.centerX(), cy = sizeKey.r.centerY();
                                sizeKey.r.set(cx - nw / 2f, cy - nh / 2f, cx + nw / 2f, cy + nh / 2f);
                                storeKey(sizeKey);
                                invalidate();
                            }
                        }
                        return true;
                    }
                    if (sliderPtr >= 0) {
                        int si = e.findPointerIndex(sliderPtr);
                        if (si >= 0) setSliderByY(e.getX(si), e.getY(si));
                    }
                    for (int i = 0; i < e.getPointerCount(); i++) {
                        int pid = e.getPointerId(i);
                        if (pid == lookPtr) {
                            float dx = e.getX(i) - lookLastX;
                            float dy = e.getY(i) - lookLastY;
                            yaw += dx * 0.34f * vals[1];
                            pitch = Math.max(-60f, Math.min(60f, pitch - dy * 0.22f * vals[1]));
                            TouchState.lookDx = dx;
                            TouchState.lookDy = dy;
                            lookLastX = e.getX(i);
                            lookLastY = e.getY(i);
                        }
                    }
                    invalidate();
                    return true;
                }
                case MotionEvent.ACTION_UP:
                case MotionEvent.ACTION_POINTER_UP:
                case MotionEvent.ACTION_CANCEL: {
                    if (editMode) {
                        if (dragKey != null && id == dragPtr) {
                            storeKey(dragKey);
                            dragKey = null;
                            dragPtr = -1;
                        }
                        if (sizeKey != null && id == sizePtr) {
                            storeKey(sizeKey);
                            sizeKey = null;
                            sizePtr = -1;
                        }
                        invalidate();
                        return true;
                    }
                    for (Key k : keys) {
                        if (k.pointer == id) {
                            k.down = false;
                            k.pointer = -1;
                        }
                    }
                    if (action == MotionEvent.ACTION_CANCEL) {
                        for (Key k : keys) {
                            k.down = false;
                            k.pointer = -1;
                        }
                        lookPtr = -1;
                    }
                    if (id == lookPtr) lookPtr = -1;
                    if (id == sliderPtr) sliderPtr = -1;
                    invalidate();
                    return true;
                }
                default:
                    return true;
            }
        }

        private void setSliderByY(float x, float y) {
            for (int i = 0; i < 3; i++) {
                if (sliders[i] != null && y >= sliders[i].top - 12 && y <= sliders[i].bottom + 12) {
                    setSlider(i, x);
                    break;
                }
            }
        }

        private void setSlider(int i, float x) {
            RectF r = sliders[i];
            float f = (x - r.left) / r.width();
            f = Math.max(0f, Math.min(1f, f));
            vals[i] = 0.5f + f * 1.5f;
            if (i == 0) {
                lastW = -1;
                lastH = -1;
                laidOut = false;
                layoutHud();
            }
            invalidate();
        }
    }
}
