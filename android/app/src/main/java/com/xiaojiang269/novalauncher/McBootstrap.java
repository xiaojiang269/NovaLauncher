package com.xiaojiang269.novalauncher;

/**
 * 在真正进 Minecraft 之前，先把 LWJGL 的 OpenGL 库按名字加载好。
 *
 * 原因：org.lwjgl.opengl.GL.create() 在 Linux 分支会自动去找
 * libGLX.so.0 / libGL.so.1 —— Android 上根本没有这些桌面库，直接
 * UnsatisfiedLinkError: Failed to locate library: libGLX.so.0。
 * 先调 GL.create("libgl4es_114.so") 把库绑定到我们自己的 native，
 * 之后 MC 里 GL.createCapabilities() 发现库已加载就不会再走 GLX 分支。
 *
 * 这里用反射调 Main.main，避免 javac 阶段需要 MC 的 jar。
 */
public final class McBootstrap {
    public static void main(String[] args) throws Exception {
        try {
            org.lwjgl.opengl.GL.create("libgl4es_114.so");
            System.out.println("[Nova] preloaded liblwjgl_opengl.so, GLX path skipped");
        } catch (Throwable t) {
            System.out.println("[Nova] GL preload failed: " + t);
            t.printStackTrace(System.out);
        }
        try {
            com.xiaojiang269.novalauncher.NovaInputBridge.init();
            com.xiaojiang269.novalauncher.NovaInputBridge.start();
        } catch (Throwable t) {
            System.out.println("[Nova] input bridge start failed: " + t);
        }
        Class<?> mc = Class.forName("net.minecraft.client.main.Main");
        mc.getMethod("main", String[].class).invoke(null, (Object) args);
    }
}
