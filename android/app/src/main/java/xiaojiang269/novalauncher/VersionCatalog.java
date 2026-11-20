package com.xiaojiang269.novalauncher;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;

/**
 * 全量 MC 版本目录：从 Mojang 官方清单拉全部版本（正式版 + 快照 + 旧版），
 * 并按发布时间打出「这个版本该用哪个 Java」的标签。
 *
 * 规则（社区通行口径）：
 *  - 1.16.5 及更早 → Java 8
 *  - 1.17 ~ 1.20.4 → Java 17
 *  - 1.20.5 及以后 → Java 21
 */
public final class VersionCatalog {

    public static final String MANIFEST = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

    public static final class Ver {
        public String id;
        public String type;
        public String releaseTime;
        public int javaMajor;

        @Override
        public String toString() {
            return id + "|" + type + "|" + releaseTime + "|" + javaMajor;
        }
    }

    public static int javaFor(String releaseTime) {
        if (releaseTime == null || releaseTime.isEmpty()) return 17;
        String d = releaseTime.substring(0, Math.min(10, releaseTime.length()));
        if (d.compareTo("2021-06-01") < 0) return 8;
        if (d.compareTo("2024-04-22") < 0) return 17;
        return 21;
    }

    /** 拉清单并解析，失败返回空列表。 */
    public static ArrayList<Ver> fetch() {
        String[] urls = new String[]{
                MANIFEST,
                "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"};
        for (String u : urls) {
            ArrayList<Ver> list = fetchOne(u);
            if (!list.isEmpty()) return list;
        }
        return new ArrayList<Ver>();
    }

    private static ArrayList<Ver> fetchOne(String urlStr) {
        ArrayList<Ver> out = new ArrayList<Ver>();
        try {
            HttpURLConnection c = (HttpURLConnection) new URL(urlStr).openConnection();
            c.setConnectTimeout(15000);
            c.setReadTimeout(20000);
            c.setRequestProperty("User-Agent", "NovaLauncherApp/3.4.5");
            BufferedReader r = new BufferedReader(new InputStreamReader(c.getInputStream()));
            StringBuilder sb = new StringBuilder();
            String line;
            while ((line = r.readLine()) != null) sb.append(line);
            r.close();
            out = parse(sb.toString());
        } catch (Throwable ignore) {
        }
        return out;
    }

    /** 极简解析：只取 versions 数组里每项的 id / type / releaseTime。 */
    static ArrayList<Ver> parse(String json) {
        ArrayList<Ver> out = new ArrayList<Ver>();
        int arr = json.indexOf("\"versions\"");
        if (arr < 0) return out;
        int i = arr;
        while (true) {
            int p = json.indexOf("\"id\"", i);
            if (p < 0) break;
            Ver v = new Ver();
            v.id = value(json, p);
            int tp = json.indexOf("\"type\"", p);
            v.type = tp > 0 ? value(json, tp) : "release";
            int rt = json.indexOf("\"releaseTime\"", p);
            v.releaseTime = rt > 0 ? value(json, rt) : "";
            v.javaMajor = javaFor(v.releaseTime);
            out.add(v);
            i = rt > 0 ? rt + 10 : p + 10;
            if (out.size() > 1200) break;
        }
        return out;
    }

    private static String value(String json, int from) {
        int q = json.indexOf('"', json.indexOf(':', from));
        if (q < 0) return "";
        int e = json.indexOf('"', q + 1);
        if (e < 0) return "";
        return json.substring(q + 1, e);
    }

    /** 输出给 WebView 的 JSON。 */
    public static String toJson(ArrayList<Ver> list) {
        StringBuilder sb = new StringBuilder("[");
        for (int i = 0; i < list.size(); i++) {
            Ver v = list.get(i);
            if (i > 0) sb.append(',');
            sb.append("{\"id\":\"").append(v.id).append("\",\"type\":\"").append(v.type)
                    .append("\",\"time\":\"").append(v.releaseTime).append("\",\"java\":")
                    .append(v.javaMajor).append('}');
        }
        return sb.append(']').toString();
    }
}
